using System.Diagnostics;
using Avalonia.Win32;

namespace UnVault.App.Services;

/// <summary>ANGLE owns compatibility checks; this is a bounded, disposable store for its opaque WARP binaries.</summary>
internal sealed class WarpProgramCache : IWin32AngleProgramCache, IDisposable
{
    private const uint Magic = 0x31504357; // WCP1, storage format only; ANGLE versions its own keys and binaries.
    internal const int MaxEntries = 128;
    internal const int MaxBinaryBytes = 1024 * 1024;
    private const int MaxPayloadBytes = 4 * 1024 * 1024;
    private readonly string _path;
    private readonly object _gate = new(), _writeGate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private Timer? _saveTimer;
    private long _sequence, _revision, _persistedRevision;
    private int _bytes;
    private bool _disposed;
    private sealed record Entry(byte[] Key, byte[] Binary, long Sequence);

    public WarpProgramCache(string path)
    {
        _path = Path.GetFullPath(path);
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > MaxPayloadBytes + MaxEntries * 8 + 8)
                return;
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != Magic)
                return;
            int count = reader.ReadInt32();
            if (count is < 0 or > MaxEntries)
                return;
            // A partial/truncated file is a miss, not a partially accepted cache.
            var loaded = new List<Entry>(count);
            int bytes = 0;
            for (int i = 0; i < count; i++)
            {
                int keySize = reader.ReadInt32(), size = reader.ReadInt32();
                if (keySize is <= 0 or > 128 || size is <= 0 or > MaxBinaryBytes || (bytes += keySize + size) > MaxPayloadBytes)
                    return;
                byte[] key = reader.ReadBytes(keySize), binary = reader.ReadBytes(size);
                if (key.Length != keySize || binary.Length != size)
                    return;
                loaded.Add(new Entry(key, binary, i));
            }
            if (stream.Position != stream.Length)
                return;
            foreach (var entry in loaded)
                _entries[Convert.ToHexString(entry.Key)] = entry;
            _bytes = _entries.Values.Sum(entry => entry.Key.Length + entry.Binary.Length);
            _sequence = count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Missing, unreadable or interrupted cache files must never prevent startup.
        }
    }

    public ReadOnlyMemory<byte> Load(ReadOnlySpan<byte> key)
    {
        lock (_gate)
            return _entries.TryGetValue(Convert.ToHexString(key), out var entry) ? entry.Binary : ReadOnlyMemory<byte>.Empty;
    }

    public void Store(ReadOnlySpan<byte> key, ReadOnlySpan<byte> binary)
    {
        if (key.Length is <= 0 or > 128 || binary.Length is <= 0 or > MaxBinaryBytes)
            return;
        lock (_gate)
        {
            if (_disposed)
                return;
            string name = Convert.ToHexString(key);
            if (_entries.TryGetValue(name, out var old))
            {
                if (old.Binary.AsSpan().SequenceEqual(binary))
                    return;
                _bytes -= old.Key.Length + old.Binary.Length;
            }
            _entries[name] = new Entry(key.ToArray(), binary.ToArray(), ++_sequence);
            _bytes += key.Length + binary.Length;
            while (_entries.Count > MaxEntries || _bytes > MaxPayloadBytes)
            {
                var oldest = _entries.MinBy(pair => pair.Value.Sequence);
                _entries.Remove(oldest.Key);
                _bytes -= oldest.Value.Key.Length + oldest.Value.Binary.Length;
            }
            _revision++;
            _saveTimer?.Change(250, Timeout.Infinite);
        }
    }

    /// <summary>No disk writes are scheduled until the first frame has been revealed.</summary>
    public void EnablePersistence()
    {
        lock (_gate)
        {
            if (_disposed || _saveTimer is not null)
                return;
            _saveTimer = new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
            if (_revision != _persistedRevision)
                _saveTimer.Change(250, Timeout.Infinite);
        }
    }

    private void Save()
    {
        // Serialize timer/shutdown saves so an older snapshot cannot replace a newer one.
        lock (_writeGate)
        {
            Entry[] entries;
            long revision;
            lock (_gate)
            {
                if (_revision == _persistedRevision)
                    return;
                revision = _revision;
                entries = _entries.Values.OrderBy(entry => entry.Sequence).ToArray();
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temporary = _path + $".{Environment.ProcessId}.tmp";
                using (var writer = new BinaryWriter(File.Create(temporary)))
                {
                    writer.Write(Magic);
                    writer.Write(entries.Length);
                    foreach (var entry in entries)
                    {
                        writer.Write(entry.Key.Length);
                        writer.Write(entry.Binary.Length);
                        writer.Write(entry.Key);
                        writer.Write(entry.Binary);
                    }
                }
                File.Move(temporary, _path, overwrite: true);
                lock (_gate)
                    _persistedRevision = revision;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning("Could not persist the WARP program cache: {0}", ex.Message);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _saveTimer?.Dispose();
        }
        Save();
    }
}
