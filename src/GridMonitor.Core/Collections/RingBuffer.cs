namespace GridMonitor.Core.Collections;

/// <summary>
/// Fixed-size circular buffer used for trend history. Adding is O(1) and memory stays
/// constant no matter how long the app runs.
/// </summary>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private int _start;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count { get; private set; }

    public void Add(T item)
    {
        if (Count < _items.Length)
        {
            _items[(_start + Count) % _items.Length] = item;
            Count++;
        }
        else
        {
            _items[_start] = item;
            _start = (_start + 1) % _items.Length;
        }
    }

    /// <summary>Copies the items, oldest first.</summary>
    public T[] ToArray()
    {
        var result = new T[Count];
        for (var i = 0; i < Count; i++)
        {
            result[i] = _items[(_start + i) % _items.Length];
        }

        return result;
    }
}
