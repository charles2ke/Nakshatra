using Nakshatra.Recommendation.Service;
using Nakshatra.Shared.Storage;

namespace Nakshatra.Tests;

public class CoPurchaseUpdaterTests
{
    [Fact]
    public async Task Counts_every_pair_in_a_basket()
    {
        var repo = new InMemoryDocumentRepository<CoPurchaseGraph>($"copurchase-{Guid.NewGuid():N}");

        await CoPurchaseUpdater.ApplyOrderAsync(repo, new[] { "a", "b", "c" });

        var graph = await repo.GetAsync("a");
        Assert.NotNull(graph);
        Assert.Equal(1, graph!.Counts["b"]);
        Assert.Equal(1, graph.Counts["c"]);
        Assert.False(graph.Counts.ContainsKey("a"));
    }

    [Fact]
    public async Task Accumulates_counts_across_baskets()
    {
        var repo = new InMemoryDocumentRepository<CoPurchaseGraph>($"copurchase-{Guid.NewGuid():N}");

        await CoPurchaseUpdater.ApplyOrderAsync(repo, new[] { "a", "b" });
        await CoPurchaseUpdater.ApplyOrderAsync(repo, new[] { "a", "b" });
        await CoPurchaseUpdater.ApplyOrderAsync(repo, new[] { "a", "c" });

        var graph = await repo.GetAsync("a");

        Assert.Equal(2, graph!.Counts["b"]);
        Assert.Equal(1, graph.Counts["c"]);
    }
}
