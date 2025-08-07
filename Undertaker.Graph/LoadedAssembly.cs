using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;

namespace Undertaker.Graph;

public sealed class LoadedAssembly : IDisposable
{
    public LoadedAssembly(string path)
    {
        try
        {
            Decompiler = new CSharpDecompiler(path, new DecompilerSettings
            {
                AutoLoadAssemblyReferences = false,
                LoadInMemory = false,
                ThrowOnAssemblyResolveErrors = false,
            });

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
