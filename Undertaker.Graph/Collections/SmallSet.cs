// NOT USED YET

using System.Collections;

namespace Undertaker.Graph.Collections;

internal struct SmallSet<T> : IReadOnlyCollection<T>, IEnumerable<T>
    where T: IComparable<T>
{
    private T _item0;
    private T _item1;
    private T[]? _items;
    private int[]? _buckets;

    public int Count { get; private set; }

    private const int InitialHashSize = 7;

    public bool Add(T item)
    {
        if (Count == 0)
        {
            _item0 = item;
            Count = 1;
            return true;
        }

        if (Count == 1)
        {
            if (_item0.CompareTo(item) == 0)
            {
                return false;
            }

            _item1 = item;
            Count = 2;
            return true;
        }

        if (Count == 2)
        {
            if (_item0.CompareTo(item) == 0 || _item1.CompareTo(item) == 0)
            {
                return false;
            }

            _items = new T[InitialHashSize];
            _buckets = new int[InitialHashSize];
            Array.Fill(_buckets, -1);
            Count = 0;

            AddToHashTable(_item0);
            AddToHashTable(_item1);
            _item0 = default!;
            _item1 = default!;
        }
        else if (IsInHashTable(item))
        {
            return false;
        }
        
        AddToHashTable(item);
        return true;
    }

    private void GrowHashTable()
    {
        int newSize = _buckets!.Length * 2 + 1;
        var oldItems = _items!;
        var oldCount = Count;

        _items = new T[newSize];

        if (newSize > 32)
        {
            // give some empty space to reduce the cost of scanning for empty slots
            newSize *= 2;
        }

        _buckets = new int[newSize];
        Array.Fill(_buckets, -1);

        Count = 0;
        for (int i = 0; i < oldCount; i++)
        {
            AddToHashTable(oldItems[i]);
        }
    }

    private void AddToHashTable(T item)
    {
        if (Count == _items!.Length)
        {
            GrowHashTable();
        }

        int hash = item.GetHashCode() & 0x7FFFFFFF;
        int bucket = hash % _buckets!.Length;
        while (_buckets[bucket] >= 0)
        {
            bucket = (bucket + 1) % _buckets.Length;
        }
        
        _items[Count] = item;
        _buckets[bucket] = Count;

        Count++;
    }

    private readonly bool IsInHashTable(T item)
    {
        int hash = item.GetHashCode() & 0x7FFFFFFF;
        int bucket = hash % _buckets!.Length;
        int start = bucket;

        do
        {
            int idx = _buckets[bucket];
            if (idx < 0)
            {
                return false;
            }

            if (_items![idx].CompareTo(item) == 0)
            {
                return true;
            }

            bucket = (bucket + 1) % _buckets.Length;
        } while (bucket != start);
        
        return false;
    }

    public void TrimExcess()
    {
        if (_items != null)
        {
            Array.Resize(ref _items, Count);
        }
    }

    public readonly IEnumerator<T> GetEnumerator()
    {
        if (Count == 0)
        {
            yield break;
        }
        
        if (Count == 1)
        {
            yield return _item0;
            yield break;
        }
        
        if (Count == 2)
        {
            yield return _item0;
            yield return _item1;
            yield break;
        }

        if (_items != null)
        {
            for (int i = 0; i < Count; i++)
            {
                yield return _items[i];
            }
        }
    }

    readonly IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
}
