using System.Buffers;
using System.IO.Hashing;
using System.Text;

namespace Undertaker.Graph.Misc;

/// <summary>
/// Like a System.String, but stores UTF-8 bytes internally to save memory.        
/// </summary>
public readonly struct SkinnyString(string str) : IEquatable<SkinnyString>, IEquatable<String>, IComparable<SkinnyString>, IComparable<String>
{
    private readonly byte[] _data = Encoding.UTF8.GetBytes(str);
    public override int GetHashCode()
    {
        return (int)XxHash64.HashToUInt64(_data);
    }

    public override bool Equals(object? obj) => obj is SkinnyString other && _data.SequenceEqual(other._data);

    public override string ToString() => Encoding.UTF8.GetString(_data);

    bool IEquatable<SkinnyString>.Equals(SkinnyString other)
    {
        return _data.AsSpan().SequenceEqual(other._data);
    }

    int IComparable<SkinnyString>.CompareTo(SkinnyString other)
    {
        return _data.AsSpan().SequenceCompareTo(other._data);
    }

    bool IEquatable<string>.Equals(string? other)
    {
        if (other is null)
        {
            return false;
        }

        // Zero-allocation comparison: decode UTF-8 on-the-fly and compare with UTF-16
        int byteIndex = 0;
        int charIndex = 0;

        while (byteIndex < _data.Length && charIndex < other.Length)
        {
            // Decode the next UTF-8 code point
            byte b = _data[byteIndex];
            uint codePoint;
            int bytesConsumed;

            if ((b & 0x80) == 0)
            {
                // 1-byte sequence (0xxxxxxx)
                codePoint = b;
                bytesConsumed = 1;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                // 2-byte sequence (110xxxxx 10xxxxxx)
                if (byteIndex + 1 >= _data.Length)
                    return false;
                codePoint = ((uint)(b & 0x1F) << 6) | (uint)(_data[byteIndex + 1] & 0x3F);
                bytesConsumed = 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                // 3-byte sequence (1110xxxx 10xxxxxx 10xxxxxx)
                if (byteIndex + 2 >= _data.Length)
                    return false;
                codePoint = ((uint)(b & 0x0F) << 12) | ((uint)(_data[byteIndex + 1] & 0x3F) << 6) | (uint)(_data[byteIndex + 2] & 0x3F);
                bytesConsumed = 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                // 4-byte sequence (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
                if (byteIndex + 3 >= _data.Length)
                    return false;
                codePoint = ((uint)(b & 0x07) << 18) | ((uint)(_data[byteIndex + 1] & 0x3F) << 12) | ((uint)(_data[byteIndex + 2] & 0x3F) << 6) | (uint)(_data[byteIndex + 3] & 0x3F);
                bytesConsumed = 4;
            }
            else
            {
                // Invalid UTF-8
                return false;
            }

            // Compare with UTF-16
            if (codePoint <= 0xFFFF)
            {
                // BMP character - single UTF-16 code unit
                if (charIndex >= other.Length || other[charIndex] != (char)codePoint)
                    return false;
                charIndex++;
            }
            else
            {
                // Supplementary character - surrogate pair in UTF-16
                if (charIndex + 1 >= other.Length)
                    return false;
                
                uint temp = codePoint - 0x10000;
                char highSurrogate = (char)((temp >> 10) + 0xD800);
                char lowSurrogate = (char)((temp & 0x3FF) + 0xDC00);
                
                if (other[charIndex] != highSurrogate || other[charIndex + 1] != lowSurrogate)
                    return false;
                charIndex += 2;
            }

            byteIndex += bytesConsumed;
        }

        // Both should be exhausted for equality
        return byteIndex == _data.Length && charIndex == other.Length;
    }

    int IComparable<string>.CompareTo(string? other)
    {
        if (other is null)
        {
            return 1; // Non-null is greater than null
        }

        // Zero-allocation comparison: decode UTF-8 on-the-fly and compare with UTF-16
        int byteIndex = 0;
        int charIndex = 0;

        while (byteIndex < _data.Length && charIndex < other.Length)
        {
            // Decode the next UTF-8 code point
            byte b = _data[byteIndex];
            uint codePoint;
            int bytesConsumed;

            if ((b & 0x80) == 0)
            {
                // 1-byte sequence (0xxxxxxx)
                codePoint = b;
                bytesConsumed = 1;
            }
            else if ((b & 0xE0) == 0xC0)
            {
                // 2-byte sequence (110xxxxx 10xxxxxx)
                if (byteIndex + 1 >= _data.Length)
                    return -1; // Invalid UTF-8, consider this less
                codePoint = ((uint)(b & 0x1F) << 6) | (uint)(_data[byteIndex + 1] & 0x3F);
                bytesConsumed = 2;
            }
            else if ((b & 0xF0) == 0xE0)
            {
                // 3-byte sequence (1110xxxx 10xxxxxx 10xxxxxx)
                if (byteIndex + 2 >= _data.Length)
                    return -1;
                codePoint = ((uint)(b & 0x0F) << 12) | ((uint)(_data[byteIndex + 1] & 0x3F) << 6) | (uint)(_data[byteIndex + 2] & 0x3F);
                bytesConsumed = 3;
            }
            else if ((b & 0xF8) == 0xF0)
            {
                // 4-byte sequence (11110xxx 10xxxxxx 10xxxxxx 10xxxxxx)
                if (byteIndex + 3 >= _data.Length)
                    return -1;
                codePoint = ((uint)(b & 0x07) << 18) | ((uint)(_data[byteIndex + 1] & 0x3F) << 12) | ((uint)(_data[byteIndex + 2] & 0x3F) << 6) | (uint)(_data[byteIndex + 3] & 0x3F);
                bytesConsumed = 4;
            }
            else
            {
                // Invalid UTF-8
                return -1;
            }

            // Compare with UTF-16
            if (codePoint <= 0xFFFF)
            {
                // BMP character - single UTF-16 code unit
                if (charIndex >= other.Length)
                    return 1; // This string is longer
                
                int comparison = ((char)codePoint).CompareTo(other[charIndex]);
                if (comparison != 0)
                    return comparison;
                
                charIndex++;
            }
            else
            {
                // Supplementary character - surrogate pair in UTF-16
                if (charIndex + 1 >= other.Length)
                    return 1; // This string is longer
                
                uint temp = codePoint - 0x10000;
                char highSurrogate = (char)((temp >> 10) + 0xD800);
                char lowSurrogate = (char)((temp & 0x3FF) + 0xDC00);
                
                int comparison = highSurrogate.CompareTo(other[charIndex]);
                if (comparison != 0)
                    return comparison;
                
                comparison = lowSurrogate.CompareTo(other[charIndex + 1]);
                if (comparison != 0)
                    return comparison;
                
                charIndex += 2;
            }

            byteIndex += bytesConsumed;
        }

        // If we've exhausted one string, the longer one is greater
        if (byteIndex < _data.Length)
            return 1; // This string has more characters
        if (charIndex < other.Length)
            return -1; // Other string has more characters
        
        return 0; // Equal
    }

    public bool Contains(char ch)
    {
        return IndexOf(ch) >= 0;
    }

    public bool Contains(string value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        return IndexOf(value) >= 0;
    }

    public int IndexOf(char ch)
    {
        ReadOnlySpan<byte> utf8Text = _data;

        int charIndex = 0;
        Rune needleRune = new(ch);
        ReadOnlySpan<byte> remainingSlice = utf8Text;

        while (!remainingSlice.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remainingSlice, out Rune sourceRune, out int bytesConsumed);

            if (status != OperationStatus.Done)
            {
                return -1; // Invalid UTF-8 or end of span
            }

            if (sourceRune.Value == needleRune.Value)
            {
                return charIndex;
            }

            remainingSlice = remainingSlice.Slice(bytesConsumed);
            charIndex++;
        }

        return -1;
    }

    public int IndexOf(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        ReadOnlySpan<byte> utf8Text = _data;

        if (value.Length > utf8Text.Length)
        {
            return -1;
        }

        ReadOnlySpan<byte> remainingHaystack = utf8Text;
        int currentCharIndex = 0;

        while (remainingHaystack.Length >= value.Length) // Quick check
        {
            if (StartsWith(remainingHaystack, value))
            {
                return currentCharIndex;
            }

            OperationStatus status = Rune.DecodeFromUtf8(remainingHaystack, out _, out int bytesConsumed);

            if (status != OperationStatus.Done)
            {
                break;
            }

            if (bytesConsumed == 0)
            {
                bytesConsumed = 1; // Avoid infinite loop
            }

            remainingHaystack = remainingHaystack.Slice(bytesConsumed);
            currentCharIndex++;
        }

        return -1;
    }

    public int LastIndexOf(char ch)
    {
        ReadOnlySpan<byte> utf8Text = _data;

        Rune needleRune = new Rune(ch);
        int lastFoundCharIndex = -1;
        int currentCharIndex = 0;
        ReadOnlySpan<byte> remainingSlice = utf8Text;

        while (!remainingSlice.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remainingSlice, out Rune sourceRune, out int bytesConsumed);

            if (status != OperationStatus.Done)
            {
                // Invalid UTF-8, stop searching.
                break;
            }

            if (sourceRune.Value == needleRune.Value)
            {
                lastFoundCharIndex = currentCharIndex;
            }

            remainingSlice = remainingSlice.Slice(bytesConsumed);
            currentCharIndex++;
        }

        return lastFoundCharIndex;
    }

    public int LastIndexOf(char ch, int startIndex)
    {
        ReadOnlySpan<byte> utf8Text = _data;

        if (utf8Text.IsEmpty)
        {
            return -1;
        }

        // The public API for SkinnyString works with character indices.
        // So, startIndex is a character index.
        if ((uint)startIndex >= (uint)utf8Text.Length) // A simple byte-length check is a reasonable upper bound.
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex));
        }

        Rune needleRune = new(ch);
        int lastFoundCharIndex = -1;
        int currentCharIndex = 0;
        ReadOnlySpan<byte> remainingSlice = utf8Text;

        while (!remainingSlice.IsEmpty && currentCharIndex <= startIndex)
        {
            OperationStatus status = Rune.DecodeFromUtf8(remainingSlice, out Rune sourceRune, out int bytesConsumed);

            if (status != OperationStatus.Done)
            {
                // Invalid UTF-8, stop searching.
                break;
            }

            if (sourceRune.Value == needleRune.Value)
            {
                lastFoundCharIndex = currentCharIndex;
            }

            remainingSlice = remainingSlice.Slice(bytesConsumed);
            currentCharIndex++;
        }

        return lastFoundCharIndex;
    }


    public bool StartsWith(string value)
    {
        return StartsWith(_data, value);
    }

    private static bool StartsWith(ReadOnlySpan<byte> utf8Text, string value)
    { 
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        // We will advance this slice as we consume bytes from it.
        ReadOnlySpan<byte> utf8Slice = utf8Text;

        // string.EnumerateRunes() correctly handles surrogate pairs (e.g., for emojis)
        // and provides an allocation-free way to iterate over Unicode scalar values.
        foreach (Rune prefixRune in value.EnumerateRunes())
        {
            // Attempt to decode a single Rune from the current position in our source UTF-8 data.
            OperationStatus status = Rune.DecodeFromUtf8(utf8Slice, out Rune sourceRune, out int bytesConsumed);

            // If the source data runs out or contains invalid UTF-8 sequences,
            // it cannot possibly match the prefix.
            if (status != OperationStatus.Done)
            {
                return false;
            }

            // Compare the Unicode scalar value of the characters.
            // If they don't match, we're done.
            if (prefixRune.Value != sourceRune.Value)
            {
                return false;
            }

            // The runes matched, so advance our position in the source byte span.
            utf8Slice = utf8Slice.Slice(bytesConsumed);
        }

        // If the loop completes, it means every Rune in the prefix string
        // was successfully found at the beginning of the UTF-8 data.
        return true;
    }

    #region operators

    public static implicit operator string(SkinnyString skinnyString)
    {
        return skinnyString.ToString();
    }

    public static bool operator ==(SkinnyString left, SkinnyString right)
    {
        return left._data.SequenceEqual(right._data);
    }

    public static bool operator !=(SkinnyString left, SkinnyString right)
    {
        return !left._data.SequenceEqual(right._data);
    }

    public static bool operator ==(SkinnyString left, string? right)
    {
        return ((IEquatable<string>)left).Equals(right);
    }

    public static bool operator !=(SkinnyString left, string? right)
    {
        return !((IEquatable<string>)left).Equals(right);
    }

    public static bool operator ==(string? left, SkinnyString right)
    {
        return ((IEquatable<string>)right).Equals(left);
    }

    public static bool operator !=(string? left, SkinnyString right)
    {
        return !((IEquatable<string>)right).Equals(left);
    }

    public static bool operator <(SkinnyString left, SkinnyString right)
    {
        return ((IComparable<SkinnyString>)left).CompareTo(right) < 0;
    }

    public static bool operator <=(SkinnyString left, SkinnyString right)
    {
        return ((IComparable<SkinnyString>)left).CompareTo(right) <= 0;
    }

    public static bool operator >(SkinnyString left, SkinnyString right)
    {
        return ((IComparable<SkinnyString>)left).CompareTo(right) > 0;
    }

    public static bool operator >=(SkinnyString left, SkinnyString right)
    {
        return ((IComparable<SkinnyString>)left).CompareTo(right) >= 0;
    }

    public static bool operator <(SkinnyString left, string? right)
    {
        return ((IComparable<string>)left).CompareTo(right) < 0;
    }

    public static bool operator <=(SkinnyString left, string? right)
    {
        return ((IComparable<string>)left).CompareTo(right) <= 0;
    }

    public static bool operator >(SkinnyString left, string? right)
    {
        return ((IComparable<string>)left).CompareTo(right) > 0;
    }

    public static bool operator >=(SkinnyString left, string? right)
    {
        return ((IComparable<string>)left).CompareTo(right) >= 0;
    }

    public static bool operator <(string? left, SkinnyString right)
    {
        return ((IComparable<string>)right).CompareTo(left) > 0;
    }

    public static bool operator <=(string? left, SkinnyString right)
    {
        return ((IComparable<string>)right).CompareTo(left) >= 0;
    }

    public static bool operator >(string? left, SkinnyString right)
    {
        return ((IComparable<string>)right).CompareTo(left) < 0;
    }

    public static bool operator >=(string? left, SkinnyString right)
    {
        return ((IComparable<string>)right).CompareTo(left) <= 0;
    }

    #endregion
}
