using System.Collections.ObjectModel;
using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MidiTimecode.Describe;
using MtcStudio.Services;

namespace MtcStudio.Pages;

/// <summary>Live MIDI traffic, in and out, one line per message in plain English. Select a line for the byte-by-byte breakdown.</summary>
public sealed class MonitorPage : ContentPage
{
    private const int MaxShown = 2000;

    private readonly MtcEngine _engine;
    private readonly IDispatcherTimer _timer;
    private readonly List<LogEntry> _all = [];
    private readonly List<LogEntry> _incoming = [];
    private readonly ObservableCollection<LogEntry> _shown = [];
    private readonly CollectionView _list;
    private readonly Entry _filter = Ui.Entry("", "Filter (e.g. Full, F0 7F, IN, cue)", 300);
    private readonly CheckBox _logQuarterFrames = new() { Color = Ui.Accent };
    private readonly CheckBox _follow = new() { Color = Ui.Accent, IsChecked = true };
    private readonly Button _pause;
    private readonly Label _counts = Ui.Caption("");
    private readonly Label _detail = Ui.MonoText("Select a message to see every byte explained.", 13, Ui.Muted);
    private bool _paused;

    public MonitorPage(MtcEngine engine)
    {
        _engine = engine;
        Title = "Monitor";
        BackgroundColor = Ui.Background;

        _logQuarterFrames.IsChecked = _engine.LogQuarterFrames;
        _logQuarterFrames.CheckedChanged += (_, e) => { _engine.LogQuarterFrames = e.Value; Settings.LogQuarterFrames = e.Value; };
        _follow.CheckedChanged += (_, e) => _list!.ItemsUpdatingScrollMode = e.Value ? ItemsUpdatingScrollMode.KeepLastItemInView : ItemsUpdatingScrollMode.KeepItemsInView;
        _filter.TextChanged += (_, _) => Rebuild();
        _pause = Ui.Button("Pause", TogglePause);

        _list = new CollectionView
        {
            ItemsSource = _shown,
            SelectionMode = SelectionMode.Single,
            ItemsUpdatingScrollMode = ItemsUpdatingScrollMode.KeepLastItemInView,
            BackgroundColor = Ui.Background,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = Ui.MonoText("", 12);
                label.LineBreakMode = LineBreakMode.NoWrap;
                label.Padding = new Thickness(8, 1);
                label.BindingContextChanged += (_, _) =>
                {
                    if (label.BindingContext is not LogEntry e) return;
                    label.Text = e.Line;
                    label.TextColor = e.Direction == LogDirection.In ? Ui.Good : Ui.Accent;
                };
                return label;
            }),
        };
        _list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is LogEntry entry)
            {
                _detail.TextColor = Ui.Foreground;
                _detail.Text = $"{entry.Time:HH:mm:ss.fff}  {(entry.Direction == LogDirection.In ? "received" : "sent")}\n{MtcDescriber.Describe(entry.Bytes)}";
            }
        };

        var toolbar = Ui.Wrap(
            _filter,
            Ui.Row(_logQuarterFrames, Ui.Caption("Log quarter frames")),
            Ui.Row(_follow, Ui.Caption("Follow newest")),
            _pause,
            Ui.Button("Clear", Clear),
            Ui.Button("Copy", Copy));

        var legend = Ui.Caption("Green = received (IN), amber = sent (OUT). Quarter frames are counted on the Studio page; tick \"Log quarter frames\" to list them too (up to 120 a second).");

        var detail = Ui.Section("Selected message", new ScrollView { Content = _detail, HeightRequest = 220 });

        var grid = new Grid
        {
            Padding = new Thickness(16),
            RowSpacing = 8,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(new VerticalStackLayout { Spacing = 4, Children = { toolbar, legend } }, 0, 0);
        grid.Add(_counts, 0, 1);
        grid.Add(new Border { Stroke = Ui.Border, BackgroundColor = Ui.Background, Content = _list }, 0, 2);
        grid.Add(detail, 0, 3);
        Content = grid;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => Pull();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _logQuarterFrames.IsChecked = _engine.LogQuarterFrames;
        _timer.Start();
        Pull();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private void Pull()
    {
        if (!_paused)
        {
            _incoming.Clear();
            _engine.DrainLog(_incoming);
            if (_incoming.Count > 0)
            {
                _all.AddRange(_incoming);
                if (_all.Count > MtcEngine.MaxLogEntries) _all.RemoveRange(0, _all.Count - MtcEngine.MaxLogEntries);
                var filter = (_filter.Text ?? "").Trim();
                foreach (var e in _incoming)
                    if (Matches(e, filter)) _shown.Add(e);
                while (_shown.Count > MaxShown) _shown.RemoveAt(0);
            }
        }
        _counts.Text = $"{_shown.Count:N0} shown of {_all.Count:N0} kept{(_paused ? " · PAUSED (new traffic is held, up to " + MtcEngine.MaxLogEntries.ToString("N0") + " messages)" : "")}";
    }

    private void Rebuild()
    {
        var filter = (_filter.Text ?? "").Trim();
        _shown.Clear();
        foreach (var e in _all.Where(e => Matches(e, filter)).TakeLast(MaxShown)) _shown.Add(e);
        Pull();
    }

    private static bool Matches(LogEntry e, string filter) =>
        filter.Length == 0 || e.Line.Contains(filter, StringComparison.OrdinalIgnoreCase);

    private void TogglePause()
    {
        _paused = !_paused;
        _pause.Text = _paused ? "Resume" : "Pause";
        _pause.BackgroundColor = _paused ? Ui.Warn : Ui.Border;
        _pause.TextColor = _paused ? Colors.Black : Ui.Foreground;
        Pull();
    }

    private void Clear()
    {
        _engine.ClearLog();
        _all.Clear();
        _shown.Clear();
        _detail.Text = "Select a message to see every byte explained.";
        _detail.TextColor = Ui.Muted;
        Pull();
    }

    private async void Copy()
    {
        var sb = new StringBuilder();
        foreach (var e in _shown) sb.AppendLine(e.Line);
        try
        {
            await Clipboard.Default.SetTextAsync(sb.ToString());
        }
        catch (Exception ex)
        {
            _counts.Text = $"Could not copy to the clipboard: {ex.Message}";
        }
    }
}
