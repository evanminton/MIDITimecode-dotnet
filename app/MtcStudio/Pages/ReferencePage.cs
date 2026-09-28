using MidiTimecode.Describe;

namespace MtcStudio.Pages;

/// <summary>Everything the specification defines, searchable, in plain English.</summary>
public sealed class ReferencePage : ContentPage
{
    private readonly VerticalStackLayout _list = new() { Spacing = 10 };
    private readonly Entry _filter = Ui.Entry("", "Filter (e.g. drop, cue, 7F, reverse)", 320);

    public ReferencePage()
    {
        Title = "Reference";
        BackgroundColor = Ui.Background;
        _filter.TextChanged += (_, _) => Fill();
        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Text("MIDI Time Code — MMA0001 / RP-004 / RP-008 (doc 4.2.1)", 16, bold: true),
                _filter,
                _list),
        };
        Fill();
    }

    private void Fill()
    {
        var f = (_filter.Text ?? "").Trim();
        _list.Children.Clear();
        foreach (var g in MtcOptions.Groups)
        {
            var options = g.Options.Where(o => f.Length == 0 || Match(g.Title, f) || Match(o.Value, f) || Match(o.Name, f) || Match(o.Description, f)).ToList();
            if (options.Count == 0) continue;
            var rows = new List<View> { Ui.Caption(g.Summary) };
            foreach (var o in options)
            {
                rows.Add(new VerticalStackLayout
                {
                    Spacing = 1,
                    Children =
                    {
                        Ui.MonoText($"{o.Value}   {o.Name}", 13, Ui.Foreground),
                        Ui.Text(o.Description, 12, color: Ui.Muted),
                    },
                });
            }
            _list.Children.Add(Ui.Section(g.Title, rows.ToArray()));
        }
    }

    private static bool Match(string text, string filter) => text.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
