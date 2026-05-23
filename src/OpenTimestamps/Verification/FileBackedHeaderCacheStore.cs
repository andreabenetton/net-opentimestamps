using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenTimestamps.Verification;

/// <summary>
/// JSON-lines file-backed implementation of <see cref="IHeaderCacheStore"/>.
/// Each line is one record: <c>{"height":N,"merkleRoot":"hex","time":ISO8601}</c>.
/// </summary>
/// <remarks>
/// <para>
/// On construction the file is loaded into an in-memory dictionary; reads
/// are lock-free. Writes append a single line to the underlying file under
/// a lock, then update the in-memory index. The format is append-only;
/// duplicate heights overwrite the in-memory entry but leave both lines on
/// disk (the last one wins on next load).
/// </para>
/// <para>
/// Concurrent writers across processes are not supported — pick one writer
/// per file. Concurrent readers across processes are fine.
/// </para>
/// </remarks>
public sealed class FileBackedHeaderCacheStore : IHeaderCacheStore, IDisposable
{
    private readonly string _path;
    private readonly object _writeLock = new();
    private readonly Dictionary<ulong, BlockHeader> _index = [];
    private StreamWriter? _writer;

    public FileBackedHeaderCacheStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        Load();
    }

    /// <summary>Number of unique heights currently cached on disk.</summary>
    public int Count
    {
        get
        {
            lock (_writeLock)
            {
                return _index.Count;
            }
        }
    }

    public BlockHeader? TryGet(ulong height)
    {
        lock (_writeLock)
        {
            return _index.TryGetValue(height, out BlockHeader? hit) ? hit : null;
        }
    }

    public void Put(BlockHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        header.Validate();

        string line = JsonSerializer.Serialize(new HeaderRecord(
            header.Height,
            Convert.ToHexString(header.MerkleRoot),
            header.Time.ToUnixTimeSeconds()));

        lock (_writeLock)
        {
            EnsureWriterOpen();
            _writer!.WriteLine(line);
            _writer!.Flush();
            _index[header.Height] = header;
        }
    }

    public void Dispose()
    {
        lock (_writeLock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Load()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        foreach (string raw in File.ReadAllLines(_path))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            HeaderRecord? rec;
            try
            {
                rec = JsonSerializer.Deserialize<HeaderRecord>(line);
            }
            catch (JsonException)
            {
                // Skip malformed lines rather than refusing to start — the
                // cache is advisory; a partial corruption (e.g. truncated
                // tail line from a crash) should not bring the app down.
                continue;
            }

            if (rec is null
                || string.IsNullOrEmpty(rec.MerkleRoot)
                || rec.MerkleRoot.Length != 64)
            {
                continue;
            }

            byte[] merkle;
            try
            {
                merkle = Convert.FromHexString(rec.MerkleRoot);
            }
            catch (FormatException)
            {
                continue;
            }

            var header = new BlockHeader(
                rec.Height,
                merkle,
                DateTimeOffset.FromUnixTimeSeconds(rec.Time));
            _index[rec.Height] = header;
        }
    }

    private void EnsureWriterOpen()
    {
        if (_writer is not null)
        {
            return;
        }

        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        FileStream fs = new(_path, FileMode.Append, FileAccess.Write, FileShare.Read);
        _writer = new StreamWriter(fs, Encoding.UTF8) { NewLine = "\n" };
    }

    private sealed record HeaderRecord(
        [property: System.Text.Json.Serialization.JsonPropertyName("height")] ulong Height,
        [property: System.Text.Json.Serialization.JsonPropertyName("merkleRoot")] string MerkleRoot,
        [property: System.Text.Json.Serialization.JsonPropertyName("time")] long Time);
}
