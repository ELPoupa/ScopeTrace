using System.Text.Json;

namespace ScopeTrace.Storage;

/// <summary>
/// Keeps every capture on disk as &lt;id&gt;.plt (the exact bytes received) plus &lt;id&gt;.json
/// (name, date, rotation), so captures survive restarts.
/// </summary>
public sealed class CaptureStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public CaptureStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScopeTrace", "Captures");

    public void Save(CaptureRecord record, byte[] raw)
    {
        WriteAtomic(RawPath(record.Id), raw);
        SaveMetadata(record);
    }

    public void SaveMetadata(CaptureRecord record) =>
        WriteAtomic(MetaPath(record.Id), JsonSerializer.SerializeToUtf8Bytes(record, Json));

    public void Delete(Guid id)
    {
        File.Delete(RawPath(id));
        File.Delete(MetaPath(id));
    }

    /// <summary>Loads all captures, newest first. Unreadable entries are skipped.</summary>
    public List<(CaptureRecord Record, byte[] Raw)> LoadAll()
    {
        var result = new List<(CaptureRecord, byte[])>();
        foreach (var meta in System.IO.Directory.EnumerateFiles(Directory, "*.json"))
        {
            try
            {
                var record = JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(meta), Json);
                if (record is null || !File.Exists(RawPath(record.Id)))
                    continue;
                result.Add((record, File.ReadAllBytes(RawPath(record.Id))));
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A damaged file should not prevent the others from loading.
            }
        }
        result.Sort((a, b) => b.Item1.CreatedAt.CompareTo(a.Item1.CreatedAt));
        return result;
    }

    private string RawPath(Guid id) => Path.Combine(Directory, id.ToString("N") + ".plt");
    private string MetaPath(Guid id) => Path.Combine(Directory, id.ToString("N") + ".json");

    private static void WriteAtomic(string path, byte[] data)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, overwrite: true);
    }
}
