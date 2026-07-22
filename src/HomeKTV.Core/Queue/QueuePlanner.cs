using HomeKTV.Core.Models;

namespace HomeKTV.Core.Queue;

public static class QueuePlanner
{
    public static IReadOnlyList<QueueItem> Order(IEnumerable<QueueItem> items, QueueOrderingMode mode)
    {
        var waiting = items.Where(x => x.State is QueueItemState.Waiting or QueueItemState.Loading)
            .OrderByDescending(x => x.IsPinned)
            .ThenBy(x => x.Position)
            .ThenBy(x => x.RequestedAt)
            .ThenBy(x => x.Id)
            .ToList();

        var pinned = waiting.Where(x => x.IsPinned).ToList();
        var regular = waiting.Where(x => !x.IsPinned).ToList();
        if (mode == QueueOrderingMode.FirstComeFirstServed)
            return pinned.Concat(regular.OrderBy(x => x.RequestedAt).ThenBy(x => x.Id)).ToList();

        var guestOrder = regular.GroupBy(x => x.GuestSessionId)
            .OrderBy(g => g.Min(x => x.RequestedAt))
            .ThenBy(g => g.Min(x => x.Id))
            .Select(g => new Queue<QueueItem>(g.OrderBy(x => x.RequestedAt).ThenBy(x => x.Position).ThenBy(x => x.Id)))
            .ToList();

        var result = new List<QueueItem>(waiting.Count);
        result.AddRange(pinned);
        while (guestOrder.Any(q => q.Count > 0))
        {
            foreach (var queue in guestOrder)
                if (queue.Count > 0) result.Add(queue.Dequeue());
        }
        return result;
    }
}

