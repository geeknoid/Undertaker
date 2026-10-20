using System.Text;
using System.Text.Json;

namespace Undertaker.Graph.IO;

internal static class ReportFile
{
    public static void Write(string path, Action<Stream> write)
    {
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".undertaker-{Guid.NewGuid():N}.tmp");
        var exists = File.Exists(destination);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        UnixFileMode? permissions = null;
        if (!OperatingSystem.IsWindows() && exists)
        {
            permissions = File.GetUnixFileMode(destination);
            options.UnixCreateMode = permissions;
        }

        try
        {
            using (var stream = new FileStream(temporary, options))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows() && permissions.HasValue)
            {
                File.SetUnixFileMode(temporary, permissions.Value);
            }

            if (exists)
            {
                File.Replace(temporary, destination, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporary, destination);
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public static void WriteText(string path, Action<TextWriter> write) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
            write(writer);
        });

    public static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
        Write(path, stream => JsonSerializer.Serialize(stream, value, options));
}
