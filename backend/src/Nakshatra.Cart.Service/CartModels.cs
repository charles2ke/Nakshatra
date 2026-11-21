using Nakshatra.Shared.Http;
using Nakshatra.Shared.Models;
using ShoppingCart = Nakshatra.Shared.Models.Cart;

namespace Nakshatra.Cart.Service;

public record WishListItemRequest(string? ProductId);

/// <summary>A wish list entry with the product's live price and availability.</summary>
public record WishListEntry(
    string ProductId,
    string Name,
    decimal PriceWhenAdded,
    DateTime AddedAt,
    decimal? Price,
    string? Currency,
    int? Stock,
    string ImageUrl,
    bool PriceDropped);

public record WishListView(string Id, string UserId, IReadOnlyList<WishListEntry> Items);

public static class CartLines
{
    /// <summary>Adds a quantity of a product to a cart line, merging with an existing line at the latest price.</summary>
    public static void Add(List<CartItem> lines, string productId, string name, decimal price, int quantity)
    {
        var existing = lines.FirstOrDefault(i => i.ProductId == productId);
        if (existing is null)
        {
            lines.Add(new CartItem { ProductId = productId, Name = name, Price = price, Quantity = quantity });
            return;
        }

        existing.Quantity += quantity;
        existing.Name = name;
        existing.Price = price;
    }

    public static ShoppingCart Empty(string userId) => new() { Id = userId, UserId = userId };

    /// <summary>
    /// Joins wish list entries with the catalog. When the catalog cannot be reached the live fields
    /// are left empty and the stored name and price are still shown.
    /// </summary>
    public static async Task<WishListView> ToViewAsync(WishList list, CatalogClient catalog, CancellationToken ct)
    {
        var products = (await catalog.GetProductsAsync(ct)).ToDictionary(p => p.Id);
        var items = list.Items
            .OrderByDescending(i => i.AddedAt)
            .Select(item =>
            {
                products.TryGetValue(item.ProductId, out var product);
                return new WishListEntry(
                    item.ProductId,
                    product?.Name ?? item.Name,
                    item.PriceWhenAdded,
                    item.AddedAt,
                    product?.Price,
                    product?.Currency,
                    product?.Stock,
                    product?.ImageUrl ?? string.Empty,
                    product is not null && product.Price < item.PriceWhenAdded);
            })
            .ToList();

        return new WishListView(list.Id, list.UserId, items);
    }
}
