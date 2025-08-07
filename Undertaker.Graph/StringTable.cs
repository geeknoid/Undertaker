// NOT YET USED

namespace Undertaker.Graph;

internal struct StringId(uint bucketNum, uint index)
{
    private readonly uint _id = (bucketNum << 20) | index;

    public uint BucketNum => (_id >> 20) & 0xfff;
    public uint Index => (_id & 0xfffff);
}


internal sealed class StringTable
{
    private const int BucketSize = 1024 * 1024;
    private const int MaxNumBuckets = 4096;

    internal sealed class Bucket
    {
        public readonly char[] Payload = new char[BucketSize];
    }

    private readonly List<Bucket> _buckets = [];
    private int _currentBucketIndex;

    public StringTable()
    {
        _buckets.Add(new Bucket());
    }

    public StringId AddString(string s)
    {
        var len = s.Length + 1;
        if (len > BucketSize)
        {
            throw new ArgumentException("String is too long to fit in a bucket.", nameof(s));
        }

        if (len > BucketSize - _currentBucketIndex)
        {
            _currentBucketIndex = 0;
            _buckets.Add(new Bucket());

            if (_buckets.Count > MaxNumBuckets)
            {
                throw new InvalidOperationException("Too many strings!");
            }
        }

        var bucket = _buckets.Last();
        var startIndex = _currentBucketIndex;

        bucket.Payload[_currentBucketIndex++] = (char)(s.Length & 0xFFFF);
        for (int i = 0; i < len - 1; i++)
        {
            bucket.Payload[_currentBucketIndex++] = s[i];
        }

        return new StringId((uint)(_buckets.Count - 1), (uint)startIndex);
    }
    
    public string GetString(StringId id)
    {
        var bucket = _buckets[(int)id.BucketNum];
        var startIndex = (int)id.Index;
        var len = bucket.Payload[startIndex];

        return new string(bucket.Payload, startIndex + 1, len);
    }
}
