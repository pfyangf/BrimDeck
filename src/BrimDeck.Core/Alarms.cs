using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrimDeck.Core;

// One local alarm. TimeOfDay is minutes from midnight (0..1439). Days is a Sunday=0 bitmask (bit 0 = Sunday … bit 6 = Saturday);
// zero means every day. FiredDate (yyyy-MM-dd local) stops the same alarm from firing twice on one day.
public sealed class AlarmItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Label { get; set; } = "";
    public int Minutes { get; set; }
    public int Days { get; set; }
    public bool Enabled { get; set; } = true;
    public string? FiredDate { get; set; }

    [JsonIgnore] public int Hour => Math.Clamp(Minutes, 0, 24 * 60 - 1) / 60;
    [JsonIgnore] public int Minute => Math.Clamp(Minutes, 0, 24 * 60 - 1) % 60;

    public bool IsDue(DateTimeOffset now)
    {
        if (!Enabled) return false;
        int tod = now.Hour * 60 + now.Minute;
        if (tod != Math.Clamp(Minutes, 0, 24 * 60 - 1)) return false;
        int bit = 1 << (int)now.DayOfWeek;
        if (Days != 0 && (Days & bit) == 0) return false;
        string today = now.ToString("yyyy-MM-dd");
        return !string.Equals(FiredDate, today, StringComparison.Ordinal);
    }
}

public static class AlarmItems
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Export(IReadOnlyList<AlarmItem> items) => JsonSerializer.Serialize(items, Json);

    public static IReadOnlyList<AlarmItem> Import(string json)
    {
        try
        {
            var items = JsonSerializer.Deserialize<List<AlarmItem>>(json, Json) ?? [];
            foreach (var item in items)
            {
                if (item.Id == Guid.Empty) item.Id = Guid.NewGuid();
                item.Label = (item.Label ?? "").Trim();
                item.Minutes = Math.Clamp(item.Minutes, 0, 24 * 60 - 1);
                item.Days &= 0b1111111;
            }
            return items;
        }
        catch (JsonException) { return []; }
    }

    public static string TimeLabel(AlarmItem item) => $"{item.Hour:00}:{item.Minute:00}";

    public static string DaysLabel(AlarmItem item)
    {
        if (item.Days == 0) return Loc.T("每天", "Every day");
        if (item.Days == 0b0111110) return Loc.T("工作日", "Weekdays");
        if (item.Days == 0b1000001) return Loc.T("周末", "Weekend");
        string[] names = Loc.Language == Loc.Chinese
            ? ["日", "一", "二", "三", "四", "五", "六"]
            : ["Su", "Mo", "Tu", "We", "Th", "Fr", "Sa"];
        var parts = new List<string>();
        for (int i = 0; i < 7; i++) if ((item.Days & (1 << i)) != 0) parts.Add(names[i]);
        return string.Join(Loc.Language == Loc.Chinese ? "" : " ", parts);
    }
}

// alarms.json in the settings folder. Plain JSON (no secrets).
public sealed class AlarmStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    private string FilePath => Path.Combine(_directory, "alarms.json");

    public AlarmStore(string directory) => _directory = directory;

    public event Action? Changed;

    public IReadOnlyList<AlarmItem> Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return [];
            try { return AlarmItems.Import(File.ReadAllText(FilePath)); }
            catch (IOException) { return []; }
        }
    }

    public void Save(IReadOnlyList<AlarmItem> items)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, AlarmItems.Export(items));
            File.Move(temporary, FilePath, true);
        }
        Changed?.Invoke();
    }
}
