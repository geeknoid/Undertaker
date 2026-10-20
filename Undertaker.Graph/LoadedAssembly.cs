using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Undertaker.Graph;

/// <summary>
/// Opens a managed assembly for metadata analysis without executing its code.
/// </summary>
public sealed class LoadedAssembly : IDisposable
{
    /// <summary>
    /// Opens the assembly at the supplied file path.
    /// </summary>
    /// <param name="path">The path to a managed DLL or executable.</param>
    /// <exception cref="BadImageFormatException">The file is not a supported managed assembly.</exception>
    public LoadedAssembly(string path)
        : this(path, resolver: null)
    {
    }

    internal LoadedAssembly(string path, IAssemblyResolver? resolver)
    {
        try
        {
            var settings = new DecompilerSettings
            {
                AutoLoadAssemblyReferences = false,
                LoadInMemory = false,
                ThrowOnAssemblyResolveErrors = false,
            };
            Decompiler = resolver == null
                ? new CSharpDecompiler(path, settings)
                : new CSharpDecompiler(path, resolver, settings);

            Path = path;
        }
        catch (MetadataFileNotSupportedException ex)
        {
            throw new BadImageFormatException(ex.Message);
        }
    }

    public void Dispose()
    {
        // nop for now...
    }

    internal CSharpDecompiler Decompiler { get; }
    internal string Path { get; private set; }
}
