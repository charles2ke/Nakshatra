using Nakshatra.Shared;
using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNakshatraInfrastructure(builder.Configuration);
builder.Services.AddHttpClient<CatalogClient>();

var app = builder.Build();
app.UseNakshatraDefaults("search-service");

// Full text-ish product search over name, description, category and tags, with storefront filters
// (rating, availability, deals) and sort orders.
app.MapGet("/api/search", async (
    string? q,
    string? category,
    decimal? minPrice,
    decimal? maxPrice,
    double? minRating,
    bool? inStock,
    bool? deals,
    string? sort,
    int? page,
    int? pageSize,
    CatalogClient catalog,
    CancellationToken ct) =>
{
    var sortOrder = string.IsNullOrWhiteSpace(sort) ? "relevance" : sort.Trim().ToLowerInvariant();
    if (!SortOrders.Contains(sortOrder))
    {
        return Results.BadRequest(new { error = $"Unknown sort '{sort}'. Use one of: {string.Join(", ", SortOrders)}." });
    }

    if (minRating is < 0 or > 5)
    {
        return Results.BadRequest(new { error = "minRating must be between 0 and 5." });
    }

    var currentPage = Math.Max(page ?? 1, 1);
    var size = Math.Clamp(pageSize ?? 12, 1, 100);

    var products = await catalog.GetProductsAsync(ct);
    var filtered = products.Where(p =>
        Matches(p, q, category, minPrice, maxPrice)
        && (minRating is null || p.AverageRating >= minRating)
        && (inStock != true || p.Stock > 0)
        && (deals != true || p.IsOnDeal()));
    var matches = Sort(filtered, q, sortOrder).ToList();

    return Results.Ok(new
    {
        items = matches.Skip((currentPage - 1) * size).Take(size).ToList(),
        total = matches.Count,
        page = currentPage,
        pageSize = size,
        sort = sortOrder
    });
});

// Type-ahead suggestions for the search box.
app.MapGet("/api/search/suggest", async (string? q, CatalogClient catalog, CancellationToken ct) =>
{
    var products = await catalog.GetProductsAsync(ct);
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.Ok(products.Select(p => p.Name).Take(5).ToList());
    }

    var suggestions = products
        .SelectMany(p => new[] { p.Name, p.Category }.Concat(p.Tags))
        .Where(term => !string.IsNullOrWhiteSpace(term) && term.Contains(q, StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(8)
        .ToList();

    return Results.Ok(suggestions);
});

app.Run();

static IEnumerable<Product> Sort(IEnumerable<Product> products, string? q, string sortOrder) => sortOrder switch
{
    "price-asc" => products.OrderBy(p => p.Price).ThenBy(p => p.Name),
    "price-desc" => products.OrderByDescending(p => p.Price).ThenBy(p => p.Name),
    "rating" => products.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount).ThenBy(p => p.Name),
    "newest" => products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name),
    _ => products.OrderByDescending(p => Score(p, q)).ThenBy(p => p.Name)
};

static bool Matches(Product product, string? q, string? category, decimal? minPrice, decimal? maxPrice)
{
    if (!string.IsNullOrWhiteSpace(category) && !string.Equals(product.Category, category, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (minPrice is not null && product.Price < minPrice)
    {
        return false;
    }

    if (maxPrice is not null && product.Price > maxPrice)
    {
        return false;
    }

    return string.IsNullOrWhiteSpace(q) || Score(product, q) > 0;
}

static int Score(Product product, string? q)
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return 0;
    }

    var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var score = 0;
    foreach (var term in terms)
    {
        if (product.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (product.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            score += 3;
        }

        if (product.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 2;
        }

        if (product.Description.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }
    }

    return score;
}

public partial class Program
{
    private static readonly string[] SortOrders = ["relevance", "price-asc", "price-desc", "rating", "newest"];
}
