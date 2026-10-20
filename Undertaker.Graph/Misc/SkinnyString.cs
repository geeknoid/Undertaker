using System.IO.Hashing;
using System.Text;
using System.Text.Json.Serialization;

namespace Undertaker.Graph.Misc;

/// <summary>
/// Stores valid Unicode as UTF-8 bytes. The default value is empty.
/// Comparisons use ordinal UTF-16 ordering, like <see cref="StringComparer.Ordinal"/>.
/// Search indices count Unicode scalar values, not UTF-16 code units or bytes.
/// Null search strings are treated as empty strings.
/// </summary>
[JsonConverter(typeof(SkinnyStringConverter))]
public readonly struct SkinnyString : IEquatable<SkinnyString>, IEquatable<string>, IComparable<SkinnyString>, IComparable<string>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[]? _data;
    private ReadOnlySpan<byte> Data => _data;

    /// <summary>Encodes a string without replacing invalid UTF-16.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="str"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="str"/> contains an unpaired surrogate.</exception>
    public SkinnyString(string str)
    {
        ArgumentNullException.ThrowIfNull(str);
        _data = StrictUtf8.GetBytes(str);
    }

    /// <summary>Length in bytes, not UTF-16 code units or Unicode scalar values.</summary>
    public int Length => Data.Length;

    public override int GetHashCode() => (int)XxHash64.HashToUInt64(Data);
    public override bool Equals(object? obj) => obj is SkinnyString other && Data.SequenceEqual(other.Data);
    public override string ToString() => Encoding.UTF8.GetString(Data);
    bool IEquatable<SkinnyString>.Equals(SkinnyString other) => Data.SequenceEqual(other.Data);
    bool IEquatable<string>.Equals(string? other) => other is not null && CompareTo(other) == 0;
    int IComparable<SkinnyString>.CompareTo(SkinnyString other) => CompareTo(other);
    int IComparable<string>.CompareTo(string? other) => CompareTo(other);

    private int CompareTo(SkinnyString other)
    {
        var leftIndex = 0;
        var rightIndex = 0;
        char leftPending = default;
        char rightPending = default;
        while (true)
        {
            bool hasLeft = NextUtf16(Data, ref leftIndex, ref leftPending, out char left);
            bool hasRight = NextUtf16(other.Data, ref rightIndex, ref rightPending, out char right);
            if (!hasLeft || !hasRight)
            {
                return hasLeft.CompareTo(hasRight);
            }

            int comparison = left.CompareTo(right);
            if (comparison != 0)
            {
                return comparison;
            }
        }
    }

    private int CompareTo(string? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byteIndex = 0;
        var charIndex = 0;
        char pending = default;
        while (NextUtf16(Data, ref byteIndex, ref pending, out char current))
        {
            if (charIndex == other.Length)
            {
                return 1;
            }

            int comparison = current.CompareTo(other[charIndex++]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return charIndex == other.Length ? 0 : -1;
    }

    private static bool NextUtf16(ReadOnlySpan<byte> data, ref int byteIndex, ref char pending, out char current)
    {
        if (pending != default)
        {
            current = pending;
            pending = default;
            return true;
        }

        if (byteIndex == data.Length)
        {
            current = default;
            return false;
        }

        // Only the strict constructor populates the bytes, so decoding always succeeds.
        _ = Rune.DecodeFromUtf8(data[byteIndex..], out Rune rune, out int consumed);
        byteIndex += consumed;
        if (rune.IsBmp)
        {
            current = (char)rune.Value;
        }
        else
        {
            int supplementary = rune.Value - 0x10000;
            current = (char)(0xD800 + (supplementary >> 10));
            pending = (char)(0xDC00 + (supplementary & 0x3FF));
        }

        return true;
    }

    public bool Contains(char ch) => IndexOf(ch) >= 0;
    public bool Contains(string value) => IndexOf(value) >= 0;

    /// <summary>Returns a Unicode scalar index, or -1. Surrogate characters are rejected.</summary>
    public int IndexOf(char ch)
    {
        Rune needle = new(ch);
        var index = 0;
        ReadOnlySpan<byte> remaining = Data;
        while (!remaining.IsEmpty)
        {
            _ = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if (rune == needle)
            {
                return index;
            }

            remaining = remaining[consumed..];
            index++;
        }

        return -1;
    }

    /// <summary>Returns a Unicode scalar index, or -1.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains an unpaired surrogate.</exception>
    public int IndexOf(string value)
    {
        ValidateSearch(value);
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var index = 0;
        ReadOnlySpan<byte> remaining = Data;
        while (!remaining.IsEmpty)
        {
            if (StartsWithCore(remaining, value))
            {
                return index;
            }

            _ = Rune.DecodeFromUtf8(remaining, out _, out int consumed);
            remaining = remaining[consumed..];
            index++;
        }

        return -1;
    }

    /// <summary>Returns a Unicode scalar index, or -1. Surrogate characters are rejected.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ch"/> is a surrogate character.</exception>
    public int LastIndexOf(char ch)
    {
        Rune needle = new(ch);
        var index = 0;
        var found = -1;
        ReadOnlySpan<byte> remaining = Data;
        while (!remaining.IsEmpty)
        {
            _ = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if (rune == needle)
            {
                found = index;
            }

            remaining = remaining[consumed..];
            index++;
        }

        return found;
    }

    /// <summary>
    /// Searches through the inclusive Unicode scalar <paramref name="startIndex"/>.
    /// It must be within the scalar count; an empty value accepts only -1 or 0.
    /// Surrogate characters are rejected.
    /// </summary>
    /// <param name="ch">The Unicode scalar character to find.</param>
    /// <param name="startIndex">The inclusive zero-based Unicode scalar index, not a byte or UTF-16 code-unit index.</param>
    /// <returns>The last matching Unicode scalar index at or before <paramref name="startIndex"/>, or -1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="ch"/> is a surrogate character, or <paramref name="startIndex"/> is outside the scalar index range.
    /// For an empty value, only -1 and 0 are accepted.
    /// </exception>
    public int LastIndexOf(char ch, int startIndex)
    {
        Rune needle = new(ch);
        if (Data.IsEmpty && startIndex is -1 or 0)
        {
            return -1;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        var index = 0;
        var found = -1;
        ReadOnlySpan<byte> remaining = Data;
        while (!remaining.IsEmpty && index <= startIndex)
        {
            _ = Rune.DecodeFromUtf8(remaining, out Rune rune, out int consumed);
            if (rune == needle)
            {
                found = index;
            }

            remaining = remaining[consumed..];
            index++;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, index);
        return found;
    }

    /// <summary>Tests a scalar-aligned prefix without replacing invalid UTF-16.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains an unpaired surrogate.</exception>
    public bool StartsWith(string value)
    {
        ValidateSearch(value);
        return string.IsNullOrEmpty(value) || StartsWithCore(Data, value);
    }

    private static void ValidateSearch(string? value)
    {
        if (value is not null)
        {
            _ = StrictUtf8.GetByteCount(value);
        }
    }

    private static bool StartsWithCore(ReadOnlySpan<byte> data, string value)
    {
        foreach (Rune prefix in value.EnumerateRunes())
        {
            if (data.IsEmpty)
            {
                return false;
            }

            _ = Rune.DecodeFromUtf8(data, out Rune source, out int consumed);
            if (prefix != source)
            {
                return false;
            }

            data = data[consumed..];
        }

        return true;
    }

    public static implicit operator string(SkinnyString value) => value.ToString();
    public static bool operator ==(SkinnyString left, SkinnyString right) => left.Data.SequenceEqual(right.Data);
    public static bool operator !=(SkinnyString left, SkinnyString right) => !(left == right);
    public static bool operator ==(SkinnyString left, string? right) => right is not null && left.CompareTo(right) == 0;
    public static bool operator !=(SkinnyString left, string? right) => !(left == right);
    public static bool operator ==(string? left, SkinnyString right) => right == left;
    public static bool operator !=(string? left, SkinnyString right) => !(right == left);
    public static bool operator <(SkinnyString left, SkinnyString right) => left.CompareTo(right) < 0;
    public static bool operator <=(SkinnyString left, SkinnyString right) => left.CompareTo(right) <= 0;
    public static bool operator >(SkinnyString left, SkinnyString right) => left.CompareTo(right) > 0;
    public static bool operator >=(SkinnyString left, SkinnyString right) => left.CompareTo(right) >= 0;
    public static bool operator <(SkinnyString left, string? right) => left.CompareTo(right) < 0;
    public static bool operator <=(SkinnyString left, string? right) => left.CompareTo(right) <= 0;
    public static bool operator >(SkinnyString left, string? right) => left.CompareTo(right) > 0;
    public static bool operator >=(SkinnyString left, string? right) => left.CompareTo(right) >= 0;
    public static bool operator <(string? left, SkinnyString right) => right.CompareTo(left) > 0;
    public static bool operator <=(string? left, SkinnyString right) => right.CompareTo(left) >= 0;
    public static bool operator >(string? left, SkinnyString right) => right.CompareTo(left) < 0;
    public static bool operator >=(string? left, SkinnyString right) => right.CompareTo(left) <= 0;
}
