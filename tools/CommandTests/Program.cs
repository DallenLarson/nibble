using Nibble.Controls;
using Nibble.Services;
using System.Windows.Controls;
using System.Windows.Media;

// Headless checks for the command bar's brains: arithmetic, unit conversion, scopes and
// matching. No UI, so it runs in a second and can be re-run any time.
var failures = 0;

void Check(string label, bool condition, string detail = "")
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {label}{(detail.Length > 0 ? "   -> " + detail : string.Empty)}");
    if (!condition) failures++;
}

// ---- arithmetic ---------------------------------------------------------------------
Check("2+2 = 4", Calculator.TryEvaluate("2+2", out var v1) && Math.Abs(v1 - 4) < 1e-9, Calculator.Format(v1));
Check("=2*3+4 = 10", Calculator.TryEvaluate("=2*3+4", out var v2) && Math.Abs(v2 - 10) < 1e-9, Calculator.Format(v2));
Check("(1+2)*3 = 9", Calculator.TryEvaluate("(1+2)*3", out var v3) && Math.Abs(v3 - 9) < 1e-9, Calculator.Format(v3));
Check("2^10 = 1024", Calculator.TryEvaluate("2^10", out var v4) && Math.Abs(v4 - 1024) < 1e-9, Calculator.Format(v4));
Check("1,250+1 = 1251", Calculator.TryEvaluate("1,250+1", out var v5) && Math.Abs(v5 - 1251) < 1e-9, Calculator.Format(v5));
Check("10/0 rejected", !Calculator.TryEvaluate("10/0", out _));
Check("'nibble' rejected", !Calculator.TryEvaluate("nibble", out _));
Check("'2+' rejected", !Calculator.TryEvaluate("2+", out _));

// ---- unit conversion ---------------------------------------------------------------
Check("12 km to mi", Calculator.TryConvert("12 km to mi", out var c1) && c1.StartsWith("7.45"), c1);
Check("100 f to c", Calculator.TryConvert("100 f to c", out var c2) && c2.StartsWith("37.77"), c2);
Check("3 tbsp to ml", Calculator.TryConvert("3 tbsp to ml", out var c3) && c3.StartsWith("44.36"), c3);
Check("5 kg in lb", Calculator.TryConvert("5 kg in lb", out var c4) && c4.StartsWith("11.02"), c4);
Check("2 gb to mb", Calculator.TryConvert("2 gb to mb", out var c5) && c5.StartsWith("2,048"), c5);
Check("5 km to kg rejected", !Calculator.TryConvert("5 km to kg", out _));

// ---- scopes and matching -----------------------------------------------------------
var inputs = new CommandInputs
{
    Tabs =
    [
        ("YouTube — music", "https://youtube.com", new object()),
        ("Nibble repository", "https://github.com/nibble", new object())
    ],
    History = [new HistoryEntry { Title = "YouTube video", Url = "https://youtube.com/watch?v=1" }],
    Bookmarks = [new Bookmark { Title = "Hacker News", Url = "https://news.ycombinator.com" }],
    RecentlyClosed = [("Closed thing", "https://example.com/gone")],
    Commands =
    [
        new BrowserCommand("Close duplicate tabs", "", Icons.Copy, () => { }, "dedupe"),
        new BrowserCommand("Sleep background tabs now", "", Icons.Sparkle, () => { }, "nap", "memory")
    ],
    SearchEngineId = "duckduckgo"
};

var tabs = CommandEngine.Query("@tabs you", inputs);
Check("@tabs you finds the tab", tabs.Count == 1 && tabs[0].Kind == ResultKind.Tab, tabs.FirstOrDefault()?.Title ?? "none");

var bookmarks = CommandEngine.Query("@bookmarks hacker", inputs);
Check("@bookmarks hacker finds the bookmark", bookmarks.Count == 1 && bookmarks[0].Kind == ResultKind.Bookmark);

var closed = CommandEngine.Query("@closed", inputs);
Check("@closed lists recently closed", closed.Count == 1 && closed[0].Kind == ResultKind.Closed);

var dedupe = CommandEngine.Query("> dedupe", inputs);
Check("> dedupe finds the command", dedupe.Count == 1 && dedupe[0].Kind == ResultKind.Command, dedupe.FirstOrDefault()?.Title ?? "none");

var fuzz = CommandEngine.Query("> nap", inputs);
Check("> nap finds sleep", fuzz.Count == 1 && fuzz[0].Title.Contains("Sleep"), fuzz.FirstOrDefault()?.Title ?? "none");

var math = CommandEngine.Query("=8*8", inputs);
Check("calculator shows 64", math[0].Kind == ResultKind.Calculator && math[0].Note == "64", math[0].Note);

var unit = CommandEngine.Query("10 km to mi", inputs);
Check("converter row first", unit[0].Kind == ResultKind.Conversion && unit[0].Note.StartsWith("6.21"), unit[0].Note);

var web = CommandEngine.Query("tasty pixel art", inputs);
Check("web search fallback last", web[^1].Kind == ResultKind.Web && web[^1].Payload.Contains("duckduckgo"), web[^1].Payload);

var empty = CommandEngine.Query("", inputs);
Check("empty query lists commands", empty.Count > 0 && empty[0].Kind == ResultKind.Command);

// ---- motion primitives -------------------------------------------------------------
// A popup's card is reused every time a menu opens, so the transform helpers have to be
// idempotent: the earlier version wrapped a new scale around the old one on every open,
// leaving each previous squash baked into the chain (menus looked worse the more you
// opened them).
{
    // WPF elements need an STA thread.
    var motion = new Thread(() =>
    {
        var element = new Border();
        var scale1 = Juice.EnsureScale(element);
        var slide1 = Juice.EnsureSlide(element);
        var scale2 = Juice.EnsureScale(element);
        var slide2 = Juice.EnsureSlide(element);
        var group = element.RenderTransform as TransformGroup;

        Check("EnsureScale reuses one transform", ReferenceEquals(scale1, scale2));
        Check("EnsureSlide reuses one transform", ReferenceEquals(slide1, slide2));
        Check("transform chain stays flat (2 children)", group is not null && group.Children.Count == 2,
            $"children={(element.RenderTransform as TransformGroup)?.Children.Count.ToString() ?? "?"}");

        scale1.ScaleX = 1.08;
        scale1.ScaleY = 0.84;
        slide1.Y = 6;
        element.Opacity = 0.2;
        Juice.Reset(element);
        Check("Reset returns the card to rest",
            Math.Abs(scale1.ScaleX - 1) < 1e-6 && Math.Abs(scale1.ScaleY - 1) < 1e-6 &&
            Math.Abs(slide1.Y) < 1e-6 && Math.Abs(element.Opacity - 1) < 1e-6);
    });
    motion.SetApartmentState(ApartmentState.STA);
    motion.Start();
    motion.Join();
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "ALL COMMAND TESTS PASSED" : $"{failures} TEST(S) FAILED");
return failures == 0 ? 0 : 1;
