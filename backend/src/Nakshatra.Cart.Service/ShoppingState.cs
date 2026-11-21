using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;
using ShoppingCart = Nakshatra.Shared.Models.Cart;

namespace Nakshatra.Cart.Service;

public enum StateChange { Missing, Unchanged, Changed }

/// <summary>Copy-on-write updates keep the cart's move receipts and changes to either document atomic.</summary>
public static class ShoppingState
{
    public static async Task<(ShoppingCart? Cart, bool Changed)> ChangeCartAsync(
        IDocumentRepository<ShoppingCart> repository, string userId, bool create,
        Func<ShoppingCart, StateChange> change, CancellationToken ct)
    {
        while (true)
        {
            var current = await repository.GetAsync(userId, ct);
            if (current is null && !create) return (null, false);
            var cart = current is null ? CartLines.Empty(userId) : Copy(current);
            var result = change(cart);
            if (result == StateChange.Missing) return (null, false);
            if (result == StateChange.Unchanged) return (cart, false);
            var revision = current?.Revision;
            cart.Revision = revision.GetValueOrDefault() + 1;
            var written = current is null
                ? await repository.TryCreateAsync(cart, ct)
                : await repository.TryReplaceAsync(x => x.Id == userId && x.Revision == revision, cart, ct);
            if (written) return (cart, true);
        }
    }

    public static async Task<WishList?> ChangeWishListAsync(
        IDocumentRepository<WishList> repository, string userId, bool create,
        Func<WishList, StateChange> change, CancellationToken ct)
    {
        while (true)
        {
            var current = await repository.GetAsync(userId, ct);
            if (current is null && !create) return null;
            var list = current is null ? new WishList { Id = userId, UserId = userId } : Copy(current);
            var result = change(list);
            if (result == StateChange.Missing) return null;
            if (result == StateChange.Unchanged) return list;
            var revision = current?.Revision;
            list.Revision = revision.GetValueOrDefault() + 1;
            var written = current is null
                ? await repository.TryCreateAsync(list, ct)
                : await repository.TryReplaceAsync(x => x.Id == userId && x.Revision == revision, list, ct);
            if (written) return list;
        }
    }

    private static ShoppingCart Copy(ShoppingCart cart) => new()
    {
        Id = cart.Id, UserId = cart.UserId, Revision = cart.Revision,
        Items = cart.Items.Select(Copy).ToList(),
        SavedForLater = cart.SavedForLater.Select(Copy).ToList(),
        AppliedWishListMoves = cart.AppliedWishListMoves.ToList()
    };

    private static CartItem Copy(CartItem item) => new()
    {
        ProductId = item.ProductId, Name = item.Name, Price = item.Price, Quantity = item.Quantity
    };

    private static WishList Copy(WishList list) => new()
    {
        Id = list.Id, UserId = list.UserId, Revision = list.Revision,
        Items = list.Items.Select(item => new WishListItem
        {
            ProductId = item.ProductId, Name = item.Name, PriceWhenAdded = item.PriceWhenAdded,
            AddedAt = item.AddedAt, EntryId = item.EntryId
        }).ToList()
    };
}
