using BrimDeck.Core;

internal static class IslandExtrasTests
{
    public static void Run(string root, Action<string, bool> check)
    {
        check("utility pages are off by default", !new DeckSettings().MessagesPage && !new DeckSettings().AlarmPage && !new DeckSettings().PomodoroPage
            && !new DeckSettings().MemoPage && !new DeckSettings().ShortcutsPage && !new DeckSettings().ToolsPage);
        check("default page order ends with tools", DeckPages.Order[^1] == DeckPage.Tools && DeckPages.Order.Contains(DeckPage.Messages));

        var alarm = new AlarmItem { Minutes = 8 * 60 + 30, Days = 0, Enabled = true };
        var due = new DateTimeOffset(2026, 11, 6, 8, 30, 0, TimeSpan.FromHours(8));
        check("an enabled daily alarm is due at its minute", alarm.IsDue(due));
        alarm.FiredDate = "2026-11-06";
        check("a fired alarm is not due again the same day", !alarm.IsDue(due));
        var folder = Path.Combine(root, "alarms");
        var store = new AlarmStore(folder);
        alarm.FiredDate = null;
        store.Save([alarm]);
        var loaded = store.Load();
        check("alarms round-trip", loaded.Count == 1 && loaded[0].Minutes == alarm.Minutes && loaded[0].Enabled);

        var clock = new PomodoroClock();
        var settings = new DeckSettings { PomodoroFocusMinutes = 1, PomodoroBreakMinutes = 1, PomodoroLongBreakMinutes = 1, PomodoroLongEvery = 2 };
        var now = DateTimeOffset.Now;
        clock.StartFocus(settings, now);
        check("pomodoro starts in focus", clock.Phase == PomodoroPhase.Focus && clock.Running);
        check("pomodoro finishes focus into a break", clock.Tick(settings, now.AddMinutes(2), out var finished) && finished == PomodoroPhase.Focus && clock.Phase == PomodoroPhase.ShortBreak);

        check("json formats", TextTools.TryFormatJson("{\"a\":1}", true, out var pretty, out _) && pretty.Contains('\n'));
        check("base64 round-trips", TextTools.TryDecodeBase64(TextTools.EncodeBase64("hi"), out var plain, out _) && plain == "hi");
        check("hex round-trips", TextTools.TryDecodeHex(TextTools.EncodeHex("ok"), out var hex, out _) && hex == "ok");
        check("qr encodes pixels", TextTools.QrBgra("BrimDeck") is { Pixels.Length: > 0 });

        var memos = new MemoStore(Path.Combine(root, "memos"));
        memos.Save([new MemoItem { Title = "t", Body = "b" }]);
        check("memos round-trip", memos.Load() is [{ Title: "t", Body: "b" }]);

        check("shortcut rejects empty", !ShortcutItems.IsSafeTarget(""));
        check("shortcut accepts https", ShortcutItems.IsSafeTarget("https://example.com"));
        var shorts = new ShortcutStore(Path.Combine(root, "shortcuts"));
        shorts.Save([new ShortcutItem { Name = "x", Target = "https://example.com" }]);
        check("shortcuts round-trip", shorts.Load() is [{ Name: "x", Target: "https://example.com" }]);
    }
}
