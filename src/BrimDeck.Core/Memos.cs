using System.Text.Json;

namespace BrimDeck.Core;

public sealed class MemoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;
}

public static class MemoItems
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Export(IReadOnlyList<MemoItem> items) => JsonSerializer.Serialize(items, Json);

    public static IReadOnlyList<MemoItem> Import(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<MemoItem>>(json, Json) ?? [];
            foreach (var item in items)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                item.Title = (item.Title ?? "").Trim();
                item.Body ??= "";
                if (item.Updated == default) item.Updated = DateTimeOffset.Now;
            }
            return items.OrderByDescending(item => item.Updated).ToList();
        }
        catch (JsonException) { return []; }
    }
}

public sealed class MemoStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    private string FilePath => Path.Combine(_directory, "memos.json");
    public MemoStore(string directory) => _directory = directory;
    public event Action? Changed;

    public IReadOnlyList<MemoItem> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return [];
            try { return MemoItems.Import(File.ReadAllText(FilePath)); }
            catch (IOException) { return []; }
        }
    }

    public void Save(IReadOnlyList<MemoItem> items)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, MemoItems.Export(items));
            File.Move(temporary, FilePath, true);
        }
        Changed?.Invoke();
    }
}
