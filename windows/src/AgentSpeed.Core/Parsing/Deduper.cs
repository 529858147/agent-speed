namespace AgentSpeed.Core.Parsing;

/// <summary>
/// Global bounded set of response keys shared by all files of all sources, remembering which file saw each key
/// first (METRICS 3.4). Not thread-safe: the monitor applies parsed lines on a single thread.
/// </summary>
internal sealed class Deduper
{
    public const int DefaultCapacity = 65_536;

    private readonly int _capacity;
    private readonly Dictionary<string, string> _owners;
    private readonly Queue<string> _order;

    public Deduper(int capacity = DefaultCapacity)
    {
        _capacity = Math.Max(1, capacity);
        _owners = new Dictionary<string, string>(Math.Min(_capacity, 4096), StringComparer.Ordinal);
        _order = new Queue<string>(Math.Min(_capacity, 4096));
    }

    public int Count => _owners.Count;

    /// <summary>Claims <paramref name="key"/> for <paramref name="owner"/>; returns false with the first owner when seen.</summary>
    public bool TryClaim(string key, string owner, out string firstOwner)
    {
        if (_owners.TryGetValue(key, out var existing))
        {
            firstOwner = existing;
            return false;
        }

        if (_owners.Count >= _capacity && _order.TryDequeue(out var oldest))
        {
            _owners.Remove(oldest);
        }

        _owners[key] = owner;
        _order.Enqueue(key);
        firstOwner = owner;
        return true;
    }
}
