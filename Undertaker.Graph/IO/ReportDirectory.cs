using System.Security.Cryptography;
using System.Text.Json;

namespace Undertaker.Graph.IO;

internal sealed class ReportDirectory
{
    private readonly string _directory;
    private readonly string _manifestPath;
    private readonly string _journalPath;
    private readonly Dictionary<string, string> _previous;
    private readonly Dictionary<string, string> _current;

    public ReportDirectory(string directory, string family, StringComparer? filenameComparer = null)
    {
        _directory = Directory.CreateDirectory(directory).FullName;
        ValidateFilename(family);
        _manifestPath = Path.Combine(_directory, $".undertaker-{family}.json");
        _journalPath = Path.Combine(_directory, $".undertaker-{family}.jsonl");
        filenameComparer ??= DetectFilenameComparer(_directory);
        _previous = new(filenameComparer);
        _current = new(filenameComparer);
        var manifest = File.Exists(_manifestPath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_manifestPath))
                ?? throw new InvalidDataException($"Invalid report ownership manifest: {_manifestPath}")
            : new(StringComparer.Ordinal);
        foreach (var (filename, hash) in manifest)
        {
            ValidateFilename(filename);
            if (!_previous.TryAdd(filename, hash))
            {
                throw new InvalidDataException($"Colliding filenames in report ownership manifest: {filename}");
            }
        }

        if (File.Exists(_journalPath))
        {
            foreach (var line in File.ReadLines(_journalPath))
            {
                var entry = JsonSerializer.Deserialize<KeyValuePair<string, string>>(line);
                ValidateFilename(entry.Key);
                _previous[entry.Key] = entry.Value;
            }
        }
    }

    public void Write(string name, string extension, Action<Stream> write)
    {
        var filename = name + extension;
        ValidateFilename(filename);
        if (_current.ContainsKey(filename))
        {
            throw new InvalidDataException($"Multiple reports map to the same filename: {filename}");
        }

        var path = Path.Combine(_directory, filename);
        ReportFile.Write(path, write);
        var hash = Hash(path);
        using (var journal = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            journal.Write(JsonSerializer.SerializeToUtf8Bytes(new KeyValuePair<string, string>(filename, hash)));
            journal.WriteByte((byte)'\n');
            journal.Flush(flushToDisk: true);
        }

        _current.Add(filename, hash);
        _previous[filename] = hash;
    }

    public void Complete()
    {
        foreach (var (filename, hash) in _previous)
        {
            if (_current.ContainsKey(filename))
            {
                continue;
            }

            var path = Path.Combine(_directory, filename);
            if (File.Exists(path))
            {
                if (!string.Equals(Hash(path), hash, StringComparison.Ordinal))
                {
                    throw new IOException($"Obsolete report was modified outside Undertaker; refusing to delete it: {path}");
                }

                File.Delete(path);
            }
        }

        ReportFile.WriteJson(_manifestPath, _current, JsonSerializerOptions.Default);
        File.Delete(_journalPath);
    }

    private static StringComparer DetectFilenameComparer(string directory)
    {
        var name = $".undertaker-{Guid.NewGuid():N}.case";
        var path = Path.Combine(directory, name);
        try
        {
            using var probe = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return File.Exists(Path.Combine(directory, name.ToUpperInvariant()))
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ValidateFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name is "." or ".."
            || name.StartsWith(".undertaker-", StringComparison.OrdinalIgnoreCase)
            || name.Any(ch => char.IsControl(ch) || "<>:\"/\\|?*".Contains(ch))
            || Path.IsPathRooted(name)
            || name.EndsWith(' ')
            || name.EndsWith('.'))
        {
            throw new InvalidDataException($"Invalid report filename: {name}");
        }
    }
}
