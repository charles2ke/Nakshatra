using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.User.Service;

public record AddressRequest(
    string? FullName,
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Phone,
    bool? IsDefault);

/// <summary>Rules for a customer's saved shipping addresses. A non-empty address book always has exactly one default.</summary>
public static class AddressBook
{
    private const int MaxFieldLength = 200;

    public static string? Validate(AddressRequest request)
    {
        var required = new (string Name, string? Value)[]
        {
            ("Full name", request.FullName),
            ("Address line 1", request.Line1),
            ("City", request.City),
            ("Postal code", request.PostalCode),
            ("Country", request.Country)
        };

        var missing = required.FirstOrDefault(f => string.IsNullOrWhiteSpace(f.Value));
        if (missing.Name is not null)
        {
            return $"{missing.Name} is required.";
        }

        var all = new[] { request.FullName, request.Line1, request.Line2, request.City, request.State, request.PostalCode, request.Country, request.Phone };
        return all.Any(v => v?.Trim().Length > MaxFieldLength)
            ? $"Address fields must be at most {MaxFieldLength} characters."
            : null;
    }

    public static void Apply(Address address, AddressRequest request)
    {
        address.FullName = request.FullName!.Trim();
        address.Line1 = request.Line1!.Trim();
        address.Line2 = request.Line2?.Trim() ?? string.Empty;
        address.City = request.City!.Trim();
        address.State = request.State?.Trim() ?? string.Empty;
        address.PostalCode = request.PostalCode!.Trim();
        address.Country = request.Country!.Trim();
        address.Phone = request.Phone?.Trim() ?? string.Empty;
    }

    /// <summary>Default address first, then the most recently added.</summary>
    public static List<Address> Ordered(IEnumerable<Address> addresses) =>
        addresses.OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt).ToList();

    public static async Task MakeDefaultAsync(IDocumentRepository<Address> repo, Address address, CancellationToken ct)
    {
        foreach (var other in await repo.FindAsync(a => a.UserId == address.UserId && a.Id != address.Id && a.IsDefault, ct))
        {
            other.IsDefault = false;
            await repo.UpsertAsync(other, ct);
        }

        address.IsDefault = true;
        await repo.UpsertAsync(address, ct);
    }

    /// <summary>After the default address is deleted, the most recently added remaining address takes over.</summary>
    public static async Task PromoteNewestAsync(IDocumentRepository<Address> repo, string userId, CancellationToken ct)
    {
        var remaining = await repo.FindAsync(a => a.UserId == userId, ct);
        if (remaining.Count == 0 || remaining.Any(a => a.IsDefault))
        {
            return;
        }

        var newest = remaining.OrderByDescending(a => a.CreatedAt).First();
        newest.IsDefault = true;
        await repo.UpsertAsync(newest, ct);
    }
}
