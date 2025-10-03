using Undertaker.Graph.Misc;
using System.Text;

namespace Undertaker.Graph.Tests;

#pragma warning disable CA1707 // Identifiers should not contain underscores - using common xUnit naming convention

public class SkinnyStringTests
{
    #region Constructor and ToString Tests

    [Fact]
    public void Constructor_EmptyString_CreatesEmpty()
    {
        var skinny = new SkinnyString("");
        Assert.Equal("", skinny.ToString());
    }

    [Fact]
    public void Constructor_AsciiString_PreservesValue()
    {
        var original = "Hello World";
        var skinny = new SkinnyString(original);
        Assert.Equal(original, skinny.ToString());
    }

    [Fact]
    public void Constructor_UnicodeString_PreservesValue()
    {
        var original = "Hello ?? ??";
        var skinny = new SkinnyString(original);
        Assert.Equal(original, skinny.ToString());
    }

    [Fact]
    public void Constructor_MultiByteCharacters_PreservesValue()
    {
        var original = "Café"; // é is a multi-byte UTF-8 character
        var skinny = new SkinnyString(original);
        Assert.Equal(original, skinny.ToString());
    }

    [Fact]
    public void Constructor_SurrogatePairs_PreservesValue()
    {
        var original = "??????????"; // Mathematical bold characters (surrogate pairs)
        var skinny = new SkinnyString(original);
        Assert.Equal(original, skinny.ToString());
    }

    [Fact]
    public void Constructor_EmojiSequence_PreservesValue()
    {
        var original = "Hello ??????????? Family";
        var skinny = new SkinnyString(original);
        Assert.Equal(original, skinny.ToString());
    }

    #endregion

    #region GetHashCode Tests

    [Fact]
    public void GetHashCode_SameStrings_ReturnsSameHash()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("test");
        Assert.Equal(skinny1.GetHashCode(), skinny2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentStrings_ReturnsDifferentHash()
    {
        var skinny1 = new SkinnyString("test1");
        var skinny2 = new SkinnyString("test2");
        Assert.NotEqual(skinny1.GetHashCode(), skinny2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_EmptyString_ReturnsValidHash()
    {
        var skinny = new SkinnyString("");
        // Should not throw
        var hash = skinny.GetHashCode();
    }

    #endregion

    #region Equals Tests

    [Fact]
    public void Equals_SkinnyString_SameValue_ReturnsTrue()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("test");
        Assert.True(((IEquatable<SkinnyString>)skinny1).Equals(skinny2));
    }

    [Fact]
    public void Equals_SkinnyString_DifferentValue_ReturnsFalse()
    {
        var skinny1 = new SkinnyString("test1");
        var skinny2 = new SkinnyString("test2");
        Assert.False(((IEquatable<SkinnyString>)skinny1).Equals(skinny2));
    }

    [Fact]
    public void Equals_String_SameValue_ReturnsTrue()
    {
        var skinny = new SkinnyString("test");
        Assert.True(((IEquatable<string>)skinny).Equals("test"));
    }

    [Fact]
    public void Equals_String_DifferentValue_ReturnsFalse()
    {
        var skinny = new SkinnyString("test1");
        Assert.False(((IEquatable<string>)skinny).Equals("test2"));
    }

    [Fact]
    public void Equals_String_Null_ReturnsFalse()
    {
        var skinny = new SkinnyString("test");
        Assert.False(((IEquatable<string>)skinny).Equals(null));
    }

    [Fact]
    public void Equals_String_Unicode_ReturnsTrue()
    {
        var original = "Hello ??";
        var skinny = new SkinnyString(original);
        Assert.True(((IEquatable<string>)skinny).Equals(original));
    }

    [Fact]
    public void Equals_String_SurrogatePairs_ReturnsTrue()
    {
        var original = "??????????";
        var skinny = new SkinnyString(original);
        Assert.True(((IEquatable<string>)skinny).Equals(original));
    }

    [Fact]
    public void Equals_Object_SkinnyString_ReturnsTrue()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("test");
        Assert.True(skinny1.Equals((object)skinny2));
    }

    [Fact]
    public void Equals_Object_NotSkinnyString_ReturnsFalse()
    {
        var skinny = new SkinnyString("test");
        Assert.False(skinny.Equals("test"));
    }

    #endregion

    #region CompareTo Tests

    [Fact]
    public void CompareTo_SkinnyString_Equal_ReturnsZero()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("test");
        Assert.Equal(0, ((IComparable<SkinnyString>)skinny1).CompareTo(skinny2));
    }

    [Fact]
    public void CompareTo_SkinnyString_Less_ReturnsNegative()
    {
        var skinny1 = new SkinnyString("a");
        var skinny2 = new SkinnyString("b");
        Assert.True(((IComparable<SkinnyString>)skinny1).CompareTo(skinny2) < 0);
    }

    [Fact]
    public void CompareTo_SkinnyString_Greater_ReturnsPositive()
    {
        var skinny1 = new SkinnyString("b");
        var skinny2 = new SkinnyString("a");
        Assert.True(((IComparable<SkinnyString>)skinny1).CompareTo(skinny2) > 0);
    }

    [Fact]
    public void CompareTo_SkinnyString_DifferentLengths_ShorterFirst()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("testing");
        Assert.True(((IComparable<SkinnyString>)skinny1).CompareTo(skinny2) < 0);
    }

    [Fact]
    public void CompareTo_String_Equal_ReturnsZero()
    {
        var skinny = new SkinnyString("test");
        Assert.Equal(0, ((IComparable<string>)skinny).CompareTo("test"));
    }

    [Fact]
    public void CompareTo_String_Less_ReturnsNegative()
    {
        var skinny = new SkinnyString("a");
        Assert.True(((IComparable<string>)skinny).CompareTo("b") < 0);
    }

    [Fact]
    public void CompareTo_String_Greater_ReturnsPositive()
    {
        var skinny = new SkinnyString("b");
        Assert.True(((IComparable<string>)skinny).CompareTo("a") > 0);
    }

    [Fact]
    public void CompareTo_String_Null_ReturnsPositive()
    {
        var skinny = new SkinnyString("test");
        Assert.True(((IComparable<string>)skinny).CompareTo(null) > 0);
    }

    [Fact]
    public void CompareTo_String_Unicode_WorksCorrectly()
    {
        var skinny1 = new SkinnyString("??");
        var skinny2 = new SkinnyString("??");
        Assert.Equal(0, ((IComparable<string>)skinny1).CompareTo(skinny2.ToString()));
    }

    [Fact]
    public void CompareTo_String_SurrogatePairs_WorksCorrectly()
    {
        var skinny1 = new SkinnyString("??????????");
        var skinny2 = new SkinnyString("??????????");
        Assert.Equal(0, ((IComparable<string>)skinny1).CompareTo(skinny2.ToString()));
    }

    #endregion

    #region Contains(char) Tests

    [Fact]
    public void Contains_Char_Ascii_Found_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.True(skinny.Contains('H'));
        Assert.True(skinny.Contains('o'));
        Assert.True(skinny.Contains(' '));
    }

    [Fact]
    public void Contains_Char_Ascii_NotFound_ReturnsFalse()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.False(skinny.Contains('x'));
        Assert.False(skinny.Contains('Z'));
    }

    [Fact]
    public void Contains_Char_Unicode_Found_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello ??");
        Assert.True(skinny.Contains('?'));
        Assert.True(skinny.Contains('?'));
    }

    [Fact]
    public void Contains_Char_MultiByteUtf8_Found_ReturnsTrue()
    {
        var skinny = new SkinnyString("Café");
        Assert.True(skinny.Contains('é'));
    }

    [Fact]
    public void Contains_Char_EmptyString_ReturnsFalse()
    {
        var skinny = new SkinnyString("");
        Assert.False(skinny.Contains('a'));
    }

    #endregion

    #region Contains(string) Tests

    [Fact]
    public void Contains_String_EmptyString_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.Contains(""));
    }

    [Fact]
    public void Contains_String_Null_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.Contains(null!));
    }

    [Fact]
    public void Contains_String_Found_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.True(skinny.Contains("Hello"));
        Assert.True(skinny.Contains("World"));
        Assert.True(skinny.Contains("o W"));
    }

    [Fact]
    public void Contains_String_NotFound_ReturnsFalse()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.False(skinny.Contains("hello"));
        Assert.False(skinny.Contains("xyz"));
    }

    [Fact]
    public void Contains_String_Unicode_Found_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello ??");
        Assert.True(skinny.Contains("??"));
    }

    [Fact]
    public void Contains_String_FullString_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.Contains("Hello"));
    }

    #endregion

    #region IndexOf Tests

    [Fact]
    public void IndexOf_Char_Found_ReturnsCorrectIndex()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(0, skinny.IndexOf('H'));
        Assert.Equal(4, skinny.IndexOf('o'));
        Assert.Equal(6, skinny.IndexOf('W'));
    }

    [Fact]
    public void IndexOf_Char_NotFound_ReturnsNegativeOne()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(-1, skinny.IndexOf('x'));
    }

    [Fact]
    public void IndexOf_Char_MultiByteUtf8_ReturnsCorrectCharIndex()
    {
        var skinny = new SkinnyString("Café");
        Assert.Equal(3, skinny.IndexOf('é'));
    }

    [Fact]
    public void IndexOf_Char_EmptyString_ReturnsNegativeOne()
    {
        var skinny = new SkinnyString("");
        Assert.Equal(-1, skinny.IndexOf('a'));
    }

    [Fact]
    public void IndexOf_String_Found_ReturnsCorrectIndex()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(0, skinny.IndexOf("Hello"));
        Assert.Equal(6, skinny.IndexOf("World"));
        Assert.Equal(4, skinny.IndexOf("o W"));
    }

    [Fact]
    public void IndexOf_String_NotFound_ReturnsNegativeOne()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(-1, skinny.IndexOf("xyz"));
    }

    [Fact]
    public void IndexOf_String_EmptyString_ReturnsZero()
    {
        var skinny = new SkinnyString("Hello");
        Assert.Equal(0, skinny.IndexOf(""));
    }

    #endregion

    #region LastIndexOf Tests

    [Fact]
    public void LastIndexOf_Char_Found_ReturnsCorrectIndex()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(7, skinny.LastIndexOf('o')); // Last 'o' is in "World"
        Assert.Equal(10, skinny.LastIndexOf('d'));
    }

    [Fact]
    public void LastIndexOf_Char_NotFound_ReturnsNegativeOne()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.Equal(-1, skinny.LastIndexOf('x'));
    }

    [Fact]
    public void LastIndexOf_Char_WithStartIndex_ReturnsCorrectIndex()
    {
        var skinny = new SkinnyString("Hello Hello");
        Assert.Equal(4, skinny.LastIndexOf('o', 5));
    }

    #endregion

    #region StartsWith Tests

    [Fact]
    public void StartsWith_EmptyString_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.StartsWith(""));
    }

    [Fact]
    public void StartsWith_Null_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.StartsWith(null!));
    }

    [Fact]
    public void StartsWith_Matching_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.True(skinny.StartsWith("Hello"));
        Assert.True(skinny.StartsWith("H"));
    }

    [Fact]
    public void StartsWith_NotMatching_ReturnsFalse()
    {
        var skinny = new SkinnyString("Hello World");
        Assert.False(skinny.StartsWith("World"));
        Assert.False(skinny.StartsWith("hello"));
    }

    [Fact]
    public void StartsWith_Unicode_Matching_ReturnsTrue()
    {
        var skinny = new SkinnyString("?? Hello");
        Assert.True(skinny.StartsWith("??"));
    }

    [Fact]
    public void StartsWith_SurrogatePairs_Matching_ReturnsTrue()
    {
        var skinny = new SkinnyString("?????????? World");
        Assert.True(skinny.StartsWith("????"));
    }

    [Fact]
    public void StartsWith_FullString_ReturnsTrue()
    {
        var skinny = new SkinnyString("Hello");
        Assert.True(skinny.StartsWith("Hello"));
    }

    [Fact]
    public void StartsWith_LongerThanString_ReturnsFalse()
    {
        var skinny = new SkinnyString("Hi");
        Assert.False(skinny.StartsWith("Hello"));
    }

    #endregion

    #region Operator Tests

    [Fact]
    public void ImplicitConversion_ToString_WorksCorrectly()
    {
        var skinny = new SkinnyString("Hello");
        string str = skinny;
        Assert.Equal("Hello", str);
    }

    [Fact]
    public void EqualityOperator_SkinnyString_Equal_ReturnsTrue()
    {
        var skinny1 = new SkinnyString("test");
        var skinny2 = new SkinnyString("test");
        Assert.True(skinny1 == skinny2);
    }

    [Fact]
    public void EqualityOperator_SkinnyString_NotEqual_ReturnsFalse()
    {
        var skinny1 = new SkinnyString("test1");
        var skinny2 = new SkinnyString("test2");
        Assert.False(skinny1 == skinny2);
    }

    [Fact]
    public void InequalityOperator_SkinnyString_NotEqual_ReturnsTrue()
    {
        var skinny1 = new SkinnyString("test1");
        var skinny2 = new SkinnyString("test2");
        Assert.True(skinny1 != skinny2);
    }

    [Fact]
    public void EqualityOperator_SkinnyStringAndString_Equal_ReturnsTrue()
    {
        var skinny = new SkinnyString("test");
        Assert.True(skinny == "test");
        Assert.True("test" == skinny);
    }

    [Fact]
    public void EqualityOperator_SkinnyStringAndString_NotEqual_ReturnsFalse()
    {
        var skinny = new SkinnyString("test1");
        Assert.False(skinny == "test2");
        Assert.False("test2" == skinny);
    }

    [Fact]
    public void InequalityOperator_SkinnyStringAndString_NotEqual_ReturnsTrue()
    {
        var skinny = new SkinnyString("test1");
        Assert.True(skinny != "test2");
        Assert.True("test2" != skinny);
    }

    [Fact]
    public void EqualityOperator_SkinnyStringAndNull_ReturnsFalse()
    {
        var skinny = new SkinnyString("test");
        Assert.False(skinny == (string?)null);
        Assert.False((string?)null == skinny);
    }

    [Fact]
    public void ComparisonOperators_SkinnyString_WorkCorrectly()
    {
        var skinny1 = new SkinnyString("a");
        var skinny2 = new SkinnyString("b");
        
        Assert.True(skinny1 < skinny2);
        Assert.True(skinny1 <= skinny2);
        Assert.True(skinny2 > skinny1);
        Assert.True(skinny2 >= skinny1);
        
        var skinny3 = new SkinnyString("a");
        Assert.True(skinny1 <= skinny3);
        Assert.True(skinny1 >= skinny3);
    }

    [Fact]
    public void ComparisonOperators_SkinnyStringAndString_WorkCorrectly()
    {
        var skinny = new SkinnyString("b");
        
        Assert.True(skinny < "c");
        Assert.True(skinny <= "c");
        Assert.True(skinny > "a");
        Assert.True(skinny >= "a");
        
        Assert.True("a" < skinny);
        Assert.True("a" <= skinny);
        Assert.True("c" > skinny);
        Assert.True("c" >= skinny);
    }

    #endregion

    #region Edge Cases and Special Scenarios

    [Fact]
    public void EdgeCase_VeryLongString_WorksCorrectly()
    {
        var longString = new string('a', 10000);
        var skinny = new SkinnyString(longString);
        Assert.Equal(longString, skinny.ToString());
        Assert.True(skinny.Contains('a'));
        Assert.Equal(0, skinny.IndexOf('a'));
        Assert.Equal(9999, skinny.LastIndexOf('a'));
    }

    [Fact]
    public void EdgeCase_AllUnicode_WorksCorrectly()
    {
        var unicode = "?????????????";
        var skinny = new SkinnyString(unicode);
        Assert.Equal(unicode, skinny.ToString());
        Assert.True(skinny.Contains('?'));
        Assert.True(skinny.StartsWith("?????"));
    }

    [Fact]
    public void EdgeCase_MixedContent_WorksCorrectly()
    {
        var mixed = "ASCII 123 ?? ?? Café";
        var skinny = new SkinnyString(mixed);
        Assert.Equal(mixed, skinny.ToString());
        Assert.True(skinny.Contains('1'));
        Assert.True(skinny.Contains('?'));
        Assert.True(skinny.Contains('é'));
    }

    [Fact]
    public void EdgeCase_RepeatedCharacters_WorksCorrectly()
    {
        var repeated = "aaabbbccc";
        var skinny = new SkinnyString(repeated);
        Assert.Equal(0, skinny.IndexOf('a'));
        Assert.Equal(2, skinny.LastIndexOf('a'));
        Assert.Equal(3, skinny.IndexOf('b'));
        Assert.Equal(5, skinny.LastIndexOf('b'));
    }

    [Fact]
    public void EdgeCase_SingleCharacter_WorksCorrectly()
    {
        var skinny = new SkinnyString("a");
        Assert.Equal("a", skinny.ToString());
        Assert.True(skinny.Contains('a'));
        Assert.Equal(0, skinny.IndexOf('a'));
        Assert.Equal(0, skinny.LastIndexOf('a'));
        Assert.True(skinny.StartsWith("a"));
    }

    [Fact]
    public void EdgeCase_OnlySpaces_WorksCorrectly()
    {
        var skinny = new SkinnyString("   ");
        Assert.Equal("   ", skinny.ToString());
        Assert.True(skinny.Contains(' '));
        Assert.Equal(0, skinny.IndexOf(' '));
    }

    [Fact]
    public void EdgeCase_SpecialCharacters_WorksCorrectly()
    {
        var special = "!@#$%^&*()_+-=[]{}|;':\",./<>?";
        var skinny = new SkinnyString(special);
        Assert.Equal(special, skinny.ToString());
        Assert.True(skinny.Contains('@'));
        Assert.True(skinny.Contains(';'));
    }

    #endregion

    #region Multiple Operations Tests

    [Fact]
    public void MultipleOperations_ChainedCalls_WorkCorrectly()
    {
        var skinny = new SkinnyString("Hello World");
        
        Assert.True(skinny.Contains('H'));
        Assert.True(skinny.Contains("World"));
        Assert.True(skinny.StartsWith("Hello"));
        Assert.Equal(0, skinny.IndexOf('H'));
        Assert.Equal(7, skinny.LastIndexOf('o'));
        Assert.Equal("Hello World", skinny.ToString());
    }

    [Fact]
    public void MultipleOperations_Comparisons_WorkCorrectly()
    {
        var skinny1 = new SkinnyString("apple");
        var skinny2 = new SkinnyString("banana");
        var skinny3 = new SkinnyString("apple");
        
        Assert.True(skinny1 == skinny3);
        Assert.True(skinny1 != skinny2);
        Assert.True(skinny1 < skinny2);
        Assert.True(skinny2 > skinny1);
        Assert.Equal(0, ((IComparable<SkinnyString>)skinny1).CompareTo(skinny3));
    }

    [Fact]
    public void MultipleOperations_UnicodeComparisons_WorkCorrectly()
    {
        var skinny1 = new SkinnyString("??");
        var skinny2 = new SkinnyString("??");
        
        Assert.True(skinny1 == skinny2);
        Assert.True(((IEquatable<string>)skinny1).Equals("??"));
        Assert.Equal(0, ((IComparable<string>)skinny1).CompareTo("??"));
        Assert.True(skinny1.Contains('?'));
        Assert.True(skinny1.Contains("??"));
    }

    #endregion

    #region Debug and Verification Tests

    [Fact]
    public void Debug_VerifyActualBehavior()
    {
        // Test IndexOf
        var str1 = "Hello ??";
        var skinny1 = new SkinnyString(str1);
        
        var expectedIndexOf1 = str1.IndexOf('?');  // Should be 6
        var expectedIndexOf2 = str1.IndexOf('?');  // Should be 7
        var actualIndexOf1 = skinny1.IndexOf('?');
        var actualIndexOf2 = skinny1.IndexOf('?');
        
        Assert.Equal(expectedIndexOf1, actualIndexOf1);
        Assert.Equal(expectedIndexOf2, actualIndexOf2);
        
        // Test LastIndexOf
        var str2 = "????";
        var skinny2 = new SkinnyString(str2);
        
        var expectedLastIndexOf1 = str2.LastIndexOf('?');  // Should be 2
        var expectedLastIndexOf2 = str2.LastIndexOf('?');  // Should be 3
        var actualLastIndexOf1 = skinny2.LastIndexOf('?');
        var actualLastIndexOf2 = skinny2.LastIndexOf('?');
        
        Assert.Equal(expectedLastIndexOf1, actualLastIndexOf1);
        Assert.Equal(expectedLastIndexOf2, actualLastIndexOf2);
        
        // Test Contains
        var expectedContains1 = str1.Contains('?');  // Should be false
        var actualContains1 = skinny1.Contains('?');
        
        Assert.Equal(expectedContains1, actualContains1);
    }

    #endregion
}

#pragma warning restore CA1707
