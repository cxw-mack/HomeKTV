using HomeKTV.Core.Models;
using HomeKTV.Core.Queue;

namespace HomeKTV.Core.Tests;

public sealed class QueuePlannerTests
{
    [Fact]
    public void FirstComeModeOrdersByRequestTime()
    {
        var start = DateTimeOffset.UtcNow;
        var items = new[] { Item(2, "B", start.AddSeconds(2)), Item(1, "A", start) };
        Assert.Equal([1L, 2L], QueuePlanner.Order(items, QueueOrderingMode.FirstComeFirstServed).Select(x => x.Id));
    }

    [Fact]
    public void FairRotationMatchesDocumentedExample()
    {
        var start = DateTimeOffset.UtcNow;
        var items = new[]
        {
            Item(1, "A", start), Item(2, "A", start.AddSeconds(3)), Item(3, "A", start.AddSeconds(5)),
            Item(4, "B", start.AddSeconds(1)), Item(5, "B", start.AddSeconds(4)), Item(6, "C", start.AddSeconds(2))
        };
        Assert.Equal([1L, 4L, 6L, 2L, 5L, 3L], QueuePlanner.Order(items, QueueOrderingMode.FairRotation).Select(x => x.Id));
    }

    [Fact]
    public void PinnedItemsAlwaysLead()
    {
        var start = DateTimeOffset.UtcNow;
        var regular = Item(1, "A", start);
        var pinned = Item(2, "B", start.AddSeconds(1));
        pinned.IsPinned = true;
        Assert.Equal(2, QueuePlanner.Order([regular, pinned], QueueOrderingMode.FairRotation)[0].Id);
    }

    private static QueueItem Item(long id, string guest, DateTimeOffset requestedAt) =>
        new() { Id = id, GuestSessionId = guest, RequestedBy = guest, RequestedAt = requestedAt, Position = id };
}

