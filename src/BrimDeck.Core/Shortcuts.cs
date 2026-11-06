using System.Text.Json;

namespace BrimDeck.Core;

public sealed class ShortcutItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    // .exe / .lnk path, or a shell URI (https:, steam:, shell:...).
    public string Target { get; set; } = "";
}

public static class ShortcutItems
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Export(IReadOnlyList<ShortcutItem> items) => JsonSerializer.Serialize(items, Json);

    public static IReadOnlyList<ShortcutItem> Import(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<ShortcutItem>>(json, Json) ?? [];
            foreach (var item in items)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                item.Name = (item.Name ?? "").Trim();
                item.Target = (item.Target ?? "").Trim();
            }
            return items.Where(item => item.Target.Length > 0).ToList();
        }
        catch (JsonException) { return []; }
    }

    // Rejects empty targets and paths with nulls. Process.Start is still UseShellExecute=true for .lnk / URIs.
    public static bool IsSafeTarget(string? target)
    {
        string value = (target ?? "").Trim();
        if (value.Length == 0 || value.Contains('\0')) return false;
        if (value.Contains("://", StringComparison.Ordinal) || value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("ms-", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            string full = Path.GetFullPath(value);
            string ext = Path.GetExtension(full).ToLowerInvariant();
            return ext is ".exe" or ".lnk" or ".bat" or ".cmd" or ".url";
        }
        catch (Exception) { return false; }
    }
}

public sealed class ShortcutStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    private string FilePath => Path.Combine(_directory, "shortcuts.json");
    public ShortcutStore(string directory) => _directory = directory;
    public event Action? Changed;

    public IReadOnlyList<ShortcutItem> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return [];
            try { return ShortcutItems.Import(File.ReadAllText(FilePath)); }
            catch (IOException) { return []; }
        }
    }

    public void Save(IReadOnlyList<ShortcutItem> items)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, ShortcutItems.Export(items));
            File.Move(temporary, FilePath, true);
        }
        Changed?.Invoke();
    }
}
