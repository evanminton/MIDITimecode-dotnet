using Microsoft.Maui.ApplicationModel.DataTransfer;
using MidiTimecode.Describe;
using MidiTimecode.Sync;

namespace MtcExplorer.Pages;

/// <summary>Paste any MIDI bytes; every MTC message is explained byte by byte and a receiver is run over the stream.</summary>
public sealed class DecodePage : ContentPage
{
    private static readonly (string Name, string Hex)[] Examples =
    [
        ("Spec example: 01:37:52:16 quarter frames", "F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76"),
        ("Reverse quarter frames", "F1 76 F1 61 F1 52 F1 45 F1 33 F1 24 F1 11 F1 00"),
        ("Full Message", "F0 7F 7F 01 01 61 25 34 10 F7"),
        ("User Bits \"REEL\"", "F0 7F 7F 01 02 0C 04 05 04 05 04 02 05 00 F7"),
        ("NRT cue point with a Note On", "F0 7E 01 04 0C 60 00 0A 00 32 01 01 01 09 06 04 0F 07 F7"),
        ("NRT System Stop special", "F0 7E 7F 04 00 60 3B 00 00 00 04 00 F7"),
        ("RT System Stop", "F0 7F 7F 05 00 04 00 F7"),
        ("NAK", "F0 7E 7F 7E 00 F7"),
        ("Mixed stream with a Note On and a clock byte", "90 40 7F F1 00 F8 F1 11 F0 43 12 00 F7"),
    ];

    private readonly Editor _input = new()
    {
        Placeholder = "Hex bytes, e.g. F1 00 F1 11 …  (spaces, commas, 0x and H suffixes are fine)",
        AutoSize = EditorAutoSizeOption.TextChanges,
        MinimumHeightRequest = 90,
        FontFamily = Ui.Mono,
        TextColor = Ui.Foreground,
        PlaceholderColor = Ui.Muted,
        BackgroundColor = Ui.Background,
    };

    private readonly Label _summary = Ui.Caption("");
    private readonly Label _output = Ui.MonoText("");
    private readonly Picker _examples;

    public DecodePage()
    {
        Title = "Decode";
        BackgroundColor = Ui.Background;
        _examples = Ui.Picker("Load an example", Examples.Select(e => e.Name).ToList(), -1, 340);
        _examples.SelectedIndexChanged += (_, _) =>
        {
            if (_examples.SelectedIndex >= 0) _input.Text = Examples[_examples.SelectedIndex].Hex;
        };
        _input.TextChanged += (_, _) => Decode();

        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Section("Input", _input,
                    Ui.Wrap(_examples, Ui.Button("Paste", Paste), Ui.Button("Clear", () => _input.Text = ""))),
                Ui.Section("Decoded", _summary, _output)),
        };
    }

    private async void Paste()
    {
        var text = await Clipboard.Default.GetTextAsync();
        if (!string.IsNullOrEmpty(text)) _input.Text = text;
    }

    private void Decode()
    {
        var text = _input.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
        {
            _summary.Text = "";
            _output.Text = "";
            return;
        }
        if (!MtcHex.TryParse(text, out var bytes))
        {
            _summary.Text = "Not valid hex yet.";
            _summary.TextColor = Ui.Bad;
            return;
        }
        _summary.TextColor = Ui.Muted;
        var descriptions = MtcDescriber.DescribeStream(bytes);
        var mtc = MtcParser.ParseAll(bytes).Count;
        _summary.Text = $"{bytes.Length} bytes · {descriptions.Count} messages · {mtc} MIDI Time Code";
        _output.Text = string.Join("\n\n", descriptions.Select((d, i) => $"[{i + 1}] {d}"));
    }
}
