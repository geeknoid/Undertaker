using System.Collections;

namespace Undertaker.Graph.Collections;

internal struct SmallList<T> : IReadOnlyCollection<T>, IEnumerable<T>
{
    private T _item0;
    private T _item1;
    private T[]? _items;

    public int Count { get; private set; }

    public void Add(T item)
    {
        if (Count == 0)
        {
            _item0 = item;
        }
        else if (Count == 1)
        {
            _item1 = item;
        }
        else
        {
            if (_items == null)
            {
                _items = new T[2];
            }
            else if (Count - 2 == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[Count - 2] = item;
        }

        Count++;
    }

    public void TrimExcess()
    {
        if (_items != null)
        {
            Array.Resize(ref _items, Count - 2);
        }
    }

    public readonly IEnumerator<T> GetEnumerator()
    {
        if (Count > 0)
        {
            yield return _item0;

            if (Count > 1)
            {
                yield return _item1;

                if (_items != null)
                {
                    for (int i = 0; i < Count - 2; i++)
                    {
                        yield return _items[i];
                    }
                }
            }
        }
    }

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
