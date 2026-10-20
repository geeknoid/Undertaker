using System.Xml.Linq;

namespace Undertaker.Graph.Tests;

public class DocumentationTests
{
    [Theory]
    [InlineData("T:Undertaker.Graph.LoadedAssembly")]
    [InlineData("T:Undertaker.Graph.Misc.SkinnyStringConverter")]
    [InlineData("T:Undertaker.Graph.Reporting.DuplicateAssembly")]
    public void PublishedTypesHaveConsumerVisibleSummaries(string name)
    {
        var member = Member(name);
        Assert.False(string.IsNullOrWhiteSpace(member.Element("summary")?.Value));
    }

    [Theory]
    [InlineData("M:Undertaker.Graph.AssemblyGraph.RecordRootAssembly(System.String)")]
    [InlineData("M:Undertaker.Graph.AssemblyGraph.RecordReflectionSymbol(System.String,System.String)")]
    [InlineData("M:Undertaker.Graph.AssemblyGraph.RecordTestMethodAttribute(System.String)")]
    [InlineData("M:Undertaker.Graph.AssemblyGraph.RecordReflectionMarkerAttribute(System.String)")]
    [InlineData("M:Undertaker.Graph.AssemblyGraph.MergeAssembly(Undertaker.Graph.LoadedAssembly)")]
    public void GraphPhaseExceptionsAreDocumented(string name)
    {
        Assert.Contains(Member(name).Elements("exception"),
            exception => (string?)exception.Attribute("cref") == "T:System.InvalidOperationException");
    }

    [Fact]
    public void BoundedSearchDocumentsIndexUnitsAndRangeException()
    {
        var member = Member("M:Undertaker.Graph.Misc.SkinnyString.LastIndexOf(System.Char,System.Int32)");
        var parameter = Assert.Single(member.Elements("param"), element => (string?)element.Attribute("name") == "startIndex");
        Assert.Contains("scalar", parameter.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(member.Elements("exception"),
            exception => (string?)exception.Attribute("cref") == "T:System.ArgumentOutOfRangeException");
    }

    private static XElement Member(string name)
    {
        var path = Path.ChangeExtension(typeof(AssemblyGraph).Assembly.Location, ".xml");
        var document = XDocument.Load(path);
        return Assert.Single(document.Descendants("member"), member => (string?)member.Attribute("name") == name);
    }
}
