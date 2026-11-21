using MongoDB.Driver;
using Nakshatra.Shared;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Catalog.Service;

public sealed class QuestionAnswers(IDocumentRepository<ProductQuestion> questions, IServiceProvider services)
{
    private static readonly SemaphoreSlim InMemoryLock = new(1, 1);
    private readonly IMongoDatabase? database = questions is MongoDocumentRepository<ProductQuestion> ? services.GetService<IMongoDatabase>() : null;

    public async Task<ProductQuestion?> AppendAsync(string productId, string questionId, ProductAnswer answer, CancellationToken ct)
    {
        if (database is not null)
        {
            var collection = database.GetCollection<ProductQuestion>(ServiceDefaults.CollectionNameFor(typeof(ProductQuestion)));
            return await collection.FindOneAndUpdateAsync(
                q => q.Id == questionId && q.ProductId == productId,
                Builders<ProductQuestion>.Update.Push(q => q.Answers, answer),
                new FindOneAndUpdateOptions<ProductQuestion> { ReturnDocument = ReturnDocument.After }, ct);
        }

        await InMemoryLock.WaitAsync(ct);
        try
        {
            var question = await questions.GetAsync(questionId, ct);
            if (question is null || question.ProductId != productId)
                return null;

            var updated = new ProductQuestion
            {
                Id = question.Id, ProductId = question.ProductId, UserId = question.UserId,
                UserName = question.UserName, Text = question.Text, CreatedAt = question.CreatedAt,
                Answers = new List<ProductAnswer>(question.Answers) { answer }
            };
            return await questions.UpsertAsync(updated, ct);
        }
        finally
        {
            InMemoryLock.Release();
        }
    }
}
