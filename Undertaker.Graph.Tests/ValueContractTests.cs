using System.Text;
using System.Text.Json;
using Undertaker.Graph.Misc;
using Undertaker.Graph.Reporting;

namespace Undertaker.Graph.Tests;

#pragma warning disable CA1707 // Common xUnit naming convention

public class ValueContractTests
{
    [Fact]
    public void SkinnyString_DefaultAndZeroInitializedValues_AreEmptyAcrossValueApis()
    {
        SkinnyString[] values = [default, new(), (new SkinnyString[1])[0], new("")];
        foreach (SkinnyString value in values)
        {
            Assert.Equal(0, value.Length);
            Assert.Equal("", value.ToString());
            string converted = value;
            Assert.Equal("", converted);
            Assert.False(value.Equals(null));
            Assert.False(value.Equals(""));
            Assert.False(((IEquatable<string>)value).Equals(null));
            Assert.True(((IEquatable<string>)value).Equals(""));
            Assert.Equal(0, ((IComparable<string>)value).CompareTo(""));
            Assert.True(((IComparable<string>)value).CompareTo(null) > 0);
            Assert.False(value.Contains('a'));
            Assert.True(value.Contains(""));
            Assert.True(value.Contains(null!));
            Assert.Equal(-1, value.IndexOf('a'));
            Assert.Equal(0, value.IndexOf(""));
            Assert.Equal(0, value.IndexOf(null!));
            Assert.Equal(-1, value.LastIndexOf('a'));
            Assert.True(value.StartsWith(""));
            Assert.True(value.StartsWith(null!));
            Assert.False(value.StartsWith("a"));
            Assert.Equal("\"\"", JsonSerializer.Serialize(value));

            foreach (SkinnyString other in values)
            {
                Assert.True(value.Equals((object)other));
                Assert.True(((IEquatable<SkinnyString>)value).Equals(other));
                Assert.Equal(value.GetHashCode(), other.GetHashCode());
                Assert.Equal(0, ((IComparable<SkinnyString>)value).CompareTo(other));
                AssertOperators(value, other, "", "");
            }

            AssertOperators(value, new("a"), "", "a");
            AssertMixedOperators(value, "", null);
        }

        var lookup = new Dictionary<SkinnyString, string> { [default] = "empty" };
        Assert.Equal("empty", lookup[new SkinnyString("")]);
        Assert.Single(new HashSet<SkinnyString>(values));
    }

    [Fact]
    public void SkinnyString_TrueUnicodeCorpus_RoundTripsAndPreservesEqualHashes()
    {
        string[] corpus =
        [
            "", "\0", "ASCII", "Caf\u00E9", "\u4E16\u754C", "\u03A9\u20AC",
            "\uD7FF\uE000\uFFFF", "\U00010000", "\U0001D400", "\U0001F600",
            "\U0010FFFF", "A\U0001F600\u4E16\u00E9\0"
        ];

        var lookup = new Dictionary<SkinnyString, string>();
        foreach (string text in corpus)
        {
            var value = new SkinnyString(text);
            var equal = new SkinnyString(text);
            Assert.Equal(text, value.ToString());
            Assert.Equal(Encoding.UTF8.GetByteCount(text), value.Length);
            Assert.True(((IEquatable<string>)value).Equals(text));
            Assert.True(((IEquatable<SkinnyString>)value).Equals(equal));
            Assert.True(value.Equals((object)equal));
            Assert.Equal(value.GetHashCode(), equal.GetHashCode());
            lookup.Add(value, text);
            Assert.Equal(text, lookup[equal]);
        }

        Assert.Equal(corpus.Length, lookup.Count);
    }

    [Fact]
    public void SkinnyString_OrdinalOrdering_AgreesAcrossRepresentationsAndOperators()
    {
        string[] corpus =
        [
            "", "\0", "a", "a\0", "\u00E9", "\u4E16", "\uD7FF",
            "\U00010000", "\U00010001", "\U00010400", "\U0001F600",
            "\U0010FFFF", "\uE000", "\uE000x", "\uFFFF"
        ];

        foreach (string left in corpus)
        {
            foreach (string right in corpus)
            {
                var a = new SkinnyString(left);
                var b = new SkinnyString(right);
                int expected = Math.Sign(string.CompareOrdinal(left, right));
                Assert.Equal(expected, Math.Sign(((IComparable<SkinnyString>)a).CompareTo(b)));
                Assert.Equal(expected, Math.Sign(((IComparable<string>)a).CompareTo(right)));
                Assert.Equal(-expected, Math.Sign(((IComparable<string>)b).CompareTo(left)));
                AssertOperators(a, b, left, right);
            }
        }

        for (int index = 0; index < corpus.Length - 2; index++)
        {
            var first = new SkinnyString(corpus[index]);
            var second = new SkinnyString(corpus[index + 1]);
            var third = new SkinnyString(corpus[index + 2]);
            Assert.True(first < second);
            Assert.True(second < third);
            Assert.True(first < third);
        }
    }

    [Fact]
    public void SkinnyString_SupplementaryVsSingleUtf16Unit_ComparesBeforeCheckingSuffixLength()
    {
        var supplementary = new SkinnyString("\U00010000");
        Assert.True(supplementary < "\uE000");
        Assert.True(supplementary < "\uE000x");
        Assert.True(supplementary > "\uD7FF");
        Assert.True(supplementary > "\uD800");
        Assert.True(supplementary < "\uD801");
        Assert.True(supplementary < "\uD800\uDC01");
        Assert.False(((IEquatable<string>)supplementary).Equals("\uD800"));
    }

    [Fact]
    public void SkinnyString_NullOperands_AreDistinctAndLessThanEveryValue()
    {
        SkinnyString[] values = [default, new(""), new("\U00010000")];
        foreach (SkinnyString value in values)
        {
            Assert.False(((IEquatable<string>)value).Equals(null));
            Assert.True(((IComparable<string>)value).CompareTo(null) > 0);
            AssertMixedOperators(value, value.ToString(), null);
        }

        Assert.Throws<ArgumentNullException>(() => new SkinnyString(null!));
    }

    [Fact]
    public void SkinnyString_AllMalformedSurrogateBranches_AreRejectedWithoutReplacementCollisions()
    {
        string[] malformed =
        [
            "\uD800", "\uDBFF", "\uDC00", "\uDFFF",
            "\uD800A", "A\uD800", "A\uDC00", "\uDC00A",
            "\uD800\uD801", "\uDC00\uDC01", "\uDC00\uD800",
            "\U00010000\uD800", "\U00010000\uDC00", "\uD800\U00010000"
        ];
        SkinnyString[] haystacks = [default, new("\uFFFD"), new("prefix\uFFFDsuffix")];
        foreach (string invalid in malformed)
        {
            Assert.ThrowsAny<ArgumentException>(() => new SkinnyString(invalid));
            foreach (SkinnyString haystack in haystacks)
            {
                Assert.ThrowsAny<ArgumentException>(() => haystack.StartsWith(invalid));
                Assert.ThrowsAny<ArgumentException>(() => haystack.Contains(invalid));
                Assert.ThrowsAny<ArgumentException>(() => haystack.IndexOf(invalid));
                Assert.False(((IEquatable<string>)haystack).Equals(invalid));
                AssertMixedOperators(haystack, haystack.ToString(), invalid);
            }
        }

        Assert.Equal("\uFFFD", new SkinnyString("\uFFFD").ToString());
        Assert.Equal("\U00010000", new SkinnyString("\uD800\uDC00").ToString());
    }

    [Fact]
    public void SkinnyString_SurrogateCharSearches_RejectBothHalves()
    {
        foreach (char invalid in new[] { '\uD800', '\uDBFF', '\uDC00', '\uDFFF' })
        {
            SkinnyString[] values = [default, new("\U00010000\uFFFD")];
            foreach (SkinnyString value in values)
            {
                Assert.ThrowsAny<ArgumentException>(() => value.Contains(invalid));
                Assert.ThrowsAny<ArgumentException>(() => value.IndexOf(invalid));
                Assert.ThrowsAny<ArgumentException>(() => value.LastIndexOf(invalid));
                Assert.ThrowsAny<ArgumentException>(() => value.LastIndexOf(invalid, 0));
            }
        }
    }

    [Fact]
    public void SkinnyString_UnicodeSearchAndLength_UseDocumentedByteAndScalarUnits()
    {
        var value = new SkinnyString("\u00E9\U0001F600\u4E16\U0001F600\u00E9");
        Assert.Equal(15, value.Length);
        Assert.Equal(0, value.IndexOf('\u00E9'));
        Assert.Equal(2, value.IndexOf('\u4E16'));
        Assert.Equal(1, value.IndexOf("\U0001F600"));
        Assert.Equal(3, value.IndexOf("\U0001F600\u00E9"));
        Assert.Equal(4, value.LastIndexOf('\u00E9'));
        Assert.Equal(2, value.LastIndexOf('\u4E16'));
        Assert.Equal(-1, value.LastIndexOf('\u4E17'));
        Assert.True(value.StartsWith("\u00E9\U0001F600"));
        Assert.False(value.StartsWith("\u00E9\U0001F601"));
        Assert.True(value.Contains("\U0001F600\u4E16"));
        Assert.False(value.Contains("\U0001F601"));
        Assert.Equal(-1, value.IndexOf("\u4E17"));
        Assert.Equal(0, value.LastIndexOf('\u00E9', 0));
        Assert.Equal(-1, value.LastIndexOf('\u4E16', 1));
        Assert.Equal(2, value.LastIndexOf('\u4E16', 2));
        Assert.Equal(4, value.LastIndexOf('\u00E9', 4));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => value.LastIndexOf('\u00E9', -1));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => value.LastIndexOf('\u00E9', 5));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => value.LastIndexOf('\u00E9', 14));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => value.LastIndexOf('\u00E9', int.MaxValue));
    }

    [Fact]
    public void SkinnyString_BoundedSearch_ValidatesEmptyAndAsciiScalarBounds()
    {
        SkinnyString empty = default;
        Assert.Equal(-1, empty.LastIndexOf('a', -1));
        Assert.Equal(-1, empty.LastIndexOf('a', 0));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => empty.LastIndexOf('a', -2));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => empty.LastIndexOf('a', 1));

        var ascii = new SkinnyString("aba");
        Assert.Equal(0, ascii.LastIndexOf('a', 0));
        Assert.Equal(0, ascii.LastIndexOf('a', 1));
        Assert.Equal(2, ascii.LastIndexOf('a', 2));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => ascii.LastIndexOf('a', -1));
        Assert.Throws<ArgumentOutOfRangeException>("startIndex", () => ascii.LastIndexOf('a', 3));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("\u00E9\u4E16\u754C")]
    [InlineData("\U00010000\U0001F600\U0010FFFF")]
    [InlineData("quote\" slash\\ newline\n tab\t null\0")]
    public void SkinnyString_JsonStrings_RoundTrip(string text)
    {
        string json = JsonSerializer.Serialize(new SkinnyString(text));
        SkinnyString restored = JsonSerializer.Deserialize<SkinnyString>(json);
        Assert.Equal(text, restored.ToString());
        Assert.Equal(new SkinnyString(text).GetHashCode(), restored.GetHashCode());
    }

    [Fact]
    public void SkinnyString_JsonEscapedUnicode_DecodesIndependentInput()
    {
        SkinnyString value = JsonSerializer.Deserialize<SkinnyString>("\"\\u00E9\\u4E16\\uD800\\uDC00\"");
        Assert.Equal("\u00E9\u4E16\U00010000", value.ToString());
        Assert.Equal("", JsonSerializer.Deserialize<SkinnyString>("\"\"").ToString());
        Assert.Null(JsonSerializer.Deserialize<SkinnyString?>("null"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("\"\\uD800\"")]
    [InlineData("\"\\uDC00\"")]
    [InlineData("\"\\uD800x\"")]
    [InlineData("\"\\uD800\\uD800\"")]
    [InlineData("\"\\uDC00\\uD800\"")]
    [InlineData("\"\\uDC00\\uDC00\"")]
    [InlineData("\"x\\uD800\"")]
    [InlineData("\"\\uD800\\uDC00\\uD800\"")]
    [InlineData("\"\\x\"")]
    [InlineData("\"unfinished")]
    [InlineData("")]
    public void SkinnyString_JsonNullNonStringAndMalformedInput_ThrowsJsonException(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<SkinnyString>(json));
    }

    [Fact]
    public void SkinnyString_JsonInvalidUtf8_ThrowsJsonException()
    {
        byte[] json = [(byte)'"', 0xC3, 0x28, (byte)'"'];
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<SkinnyString>(json));
    }

    [Fact]
    public void DuplicateAssemblyReport_CaseInsensitiveEquality_HasMatchingHashAndDictionaryLookup()
    {
        DuplicateAssemblyReport first = CreateReport("Library", new Version(1, 2));
        DuplicateAssemblyReport equal = CreateReport("lIBRARY", new Version(1, 2));
        DuplicateAssemblyReport differentName = CreateReport("Other", new Version(1, 2));
        DuplicateAssemblyReport differentVersion = CreateReport("Library", new Version(1, 3));

        Assert.True(first.Equals(equal));
        Assert.True(equal.Equals(first));
        Assert.Equal(0, first.CompareTo(equal));
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.False(first.Equals(differentName));
        Assert.False(first.Equals(differentVersion));
        var lookup = new Dictionary<DuplicateAssemblyReport, string> { [first] = "found" };
        Assert.Equal("found", lookup[equal]);
        Assert.Single(new HashSet<DuplicateAssemblyReport> { first, equal });
        lookup.Add(differentName, "other name");
        lookup.Add(differentVersion, "other version");
        Assert.Equal(3, lookup.Count);
    }

    private static DuplicateAssemblyReport CreateReport(string name, Version version) =>
        new(name, version, "", Array.Empty<DuplicateAssembly>());

    private static void AssertOperators(SkinnyString left, SkinnyString right, string leftText, string rightText)
    {
        int comparison = string.CompareOrdinal(leftText, rightText);
        Assert.Equal(comparison == 0, left == right);
        Assert.Equal(comparison != 0, left != right);
        Assert.Equal(comparison < 0, left < right);
        Assert.Equal(comparison <= 0, left <= right);
        Assert.Equal(comparison > 0, left > right);
        Assert.Equal(comparison >= 0, left >= right);
        AssertMixedOperators(left, leftText, rightText);
        AssertMixedOperators(right, rightText, leftText);
    }

    private static void AssertMixedOperators(SkinnyString left, string leftText, string? right)
    {
        int comparison = string.CompareOrdinal(leftText, right);
        Assert.Equal(comparison == 0, left == right);
        Assert.Equal(comparison != 0, left != right);
        Assert.Equal(comparison < 0, left < right);
        Assert.Equal(comparison <= 0, left <= right);
        Assert.Equal(comparison > 0, left > right);
        Assert.Equal(comparison >= 0, left >= right);
        Assert.Equal(comparison == 0, right == left);
        Assert.Equal(comparison != 0, right != left);
        Assert.Equal(comparison < 0, right > left);
        Assert.Equal(comparison <= 0, right >= left);
        Assert.Equal(comparison > 0, right < left);
        Assert.Equal(comparison >= 0, right <= left);
    }
}

#pragma warning restore CA1707
