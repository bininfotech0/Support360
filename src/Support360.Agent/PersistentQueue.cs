using System.Text.Json;

namespace Support360.Agent;

public sealed class PersistentQueue<T>
{
    private readonly string _path;
    private readonly int _maxEvents;
    private readonly int _maxBatch;
    private readonly string _invalidJsonMessage;
    private readonly object _gate = new();
    private List<T> _events;

    private PersistentQueue(string path, int maxEvents, int maxBatch, List<T> events, string invalidJsonMessage)
    {
        _path = path;
        _maxEvents = maxEvents;
        _maxBatch = maxBatch;
        _events = events;
        _invalidJsonMessage = invalidJsonMessage;
    }

    public static PersistentQueue<T> Open(string path, int maxEvents, int maxBatch, string invalidJsonMessage)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("queue path is required");
        }

        List<T> events = [];
        byte[]? data = null;
        try
        {
            if (File.Exists(path))
            {
                data = File.ReadAllBytes(path);
            }
        }
        catch (IOException)
        {
            throw;
        }

        if (data is { Length: > 0 })
        {
            try
            {
                events = JsonSerializer.Deserialize<List<T>>(data, AgentJson.Options) ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(invalidJsonMessage, ex);
            }
            if (events.Count > maxEvents)
            {
                events = events[^maxEvents..];
            }
        }

        var queue = new PersistentQueue<T>(path, maxEvents, maxBatch, events, invalidJsonMessage);
        queue.Persist();
        return queue;
    }

    public void Add(IReadOnlyList<T> samples)
    {
        lock (_gate)
        {
            _events.AddRange(samples);
            if (_events.Count > _maxEvents)
            {
                _events = _events[^_maxEvents..];
            }
            Persist();
        }
    }

    public IReadOnlyList<T> Batch()
    {
        lock (_gate)
        {
            var count = Math.Min(_events.Count, _maxBatch);
            return _events[..count].ToArray();
        }
    }

    public void Acknowledge(int count)
    {
        lock (_gate)
        {
            if (count < 0 || count > _events.Count)
            {
                throw new InvalidOperationException("invalid queue acknowledgement");
            }
            _events = _events[count..];
            Persist();
        }
    }

    public int Len
    {
        get
        {
            lock (_gate)
            {
                return _events.Count;
            }
        }
    }

    private void Persist()
    {
        var data = JsonSerializer.Serialize(_events, AgentJson.Options);
        AtomicIO.WriteFileAtomic(_path, data, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
