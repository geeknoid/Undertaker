using System.Text.Json.Serialization;
using ICSharpCode.Decompiler.TypeSystem;
using Undertaker.Graph.Misc;

namespace Undertaker.Graph.Reporting;

/// <summary>
/// Information about a symbol.
/// </summary>
public sealed class UnreferencedReportSymbol
{
    /// <summary>
    /// The name of the symbol.
    /// </summary>
    public SkinnyString Name { get; }

    /// <summary>
    /// The kind of symbol, e.g. "class", "method", "property".
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Accessibility of the symbol.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Accessibility Access { get; }

    internal UnreferencedReportSymbol(SkinnyString symbol, string kind, Accessibility accessibility)
    {
        Name = symbol;
        Kind = kind;
        Access = accessibility;
    }
}
