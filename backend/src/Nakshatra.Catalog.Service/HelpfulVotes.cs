using MongoDB.Driver;
using Nakshatra.Shared;
using Nakshatra.Shared.Models;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

public sealed class HelpfulVotes(IDocumentRepository<Review> reviews, IServiceProvider services)
{
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);
    private readonly IMongoDatabase? database = services.GetService<IMongoDatabase>();

    public async Task<Review?> RecordAsync(string productId, string reviewId, string userId, CancellationToken ct)
    {
        if (database is not null)
        {
            var collection = database.GetCollection<Review>(ServiceDefaults.CollectionNameFor(typeof(Review)));
            var filter = Builders<Review>.Filter;
            var updated = await collection.FindOneAndUpdateAsync(
                filter.Eq(r => r.Id, reviewId) &
                filter.Eq(r => r.ProductId, productId) &
                filter.Ne(r => r.UserId, userId) &
                filter.Not(filter.AnyEq(r => r.HelpfulVoterIds, userId)),
                Builders<Review>.Update.AddToSet(r => r.HelpfulVoterIds, userId).Inc(r => r.HelpfulCount, 1),
                new FindOneAndUpdateOptions<Review> { ReturnDocument = ReturnDocument.After },
                ct);
            return updated ?? await reviews.GetAsync(reviewId, ct);
        }

        await InMemoryLock.WaitAsync(ct);
        try
        {
            var review = await reviews.GetAsync(reviewId, ct);
            if (review is null || review.ProductId != productId || review.UserId == userId ||
                review.HelpfulVoterIds.Contains(userId))
            {
                return review;
            }

            var updated = new Review
            {
                Id = review.Id,
                ProductId = review.ProductId,
                UserId = review.UserId,
                Title = review.Title,
                Body = review.Body,
                Rating = review.Rating,
                MediaIds = review.MediaIds,
                CreatedAt = review.CreatedAt,
                VerifiedPurchase = review.VerifiedPurchase,
                HelpfulVoterIds = new List<string>(review.HelpfulVoterIds) { userId },
                HelpfulCount = review.HelpfulVoterIds.Count + 1
            };
            return await reviews.UpsertAsync(updated, ct);
        }
        finally
        {
            InMemoryLock.Release();
        }
    }
}
