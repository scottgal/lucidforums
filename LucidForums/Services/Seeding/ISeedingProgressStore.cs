using System.Collections.Concurrent;

namespace LucidForums.Services.Seeding;

public interface ISeedingProgressStore
{
    void Append(ForumSeedingProgress progress);
    IReadOnlyList<ForumSeedingProgress> Get(Guid jobId);
    void Complete(Guid jobId);
    bool IsComplete(Guid jobId);
}

public class InMemorySeedingProgressStore : ISeedingProgressStore
{
    private readonly ConcurrentDictionary<Guid, List<ForumSeedingProgress>> _items = new();
    private readonly ConcurrentDictionary<Guid, bool> _done = new();

    public void Append(ForumSeedingProgress progress)
    {
        var list = _items.GetOrAdd(progress.JobId, _ => new List<ForumSeedingProgress>());
        lock (list)
        {
            list.Add(progress);
        }
        if (progress.Stage is "done" or "error")
        {
            _done[progress.JobId] = true;
        }
    }

    public IReadOnlyList<ForumSeedingProgress> Get(Guid jobId)
    {
        if (_items.TryGetValue(jobId, out var list))
        {
            lock (list)
            {
                return list.ToList();
            }
        }
        return Array.Empty<ForumSeedingProgress>();
    }

    public void Complete(Guid jobId)
    {
        _done[jobId] = true;
    }

    public bool IsComplete(Guid jobId)
    {
        return _done.TryGetValue(jobId, out var done) && done;
    }
}
