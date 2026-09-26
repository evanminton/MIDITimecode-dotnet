using System.Globalization;
using System.Text;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using MidiTimecode;
using MidiTimecode.Cueing;
using MidiTimecode.Describe;
using MidiTimecode.Messages;
using MidiTimecode.Sync;
using MtcExplorer.Services;

namespace MtcExplorer.Pages;

/// <summary>
/// Builds any MTC message — quarter frames, Full, User Bits, both cueing forms (every set-up and
/// special type), NAK — and explains every byte. Also shows time arithmetic for the entered time.
/// </summary>
public sealed class EncodePage : ContentPage
{
    private static readonly string[] Kinds =
    [
        "Quarter Frame sequence (forward)",
        "Quarter Frame sequence (reverse)",
        "Full Message",
        "User Bits Message",
        "MTC Cueing — Non-Real Time",
        "MTC Cueing — Real Time",
        "NAK",
        "Time information",
    ];

    private readonly MtcSimulator _sim;
    private readonly Picker _kind;
    private readonly Entry _time = Ui.Entry("01:37:52:16", "HH:MM:SS:FF[.ff]", 170);
    private readonly Picker _rate;
    private readonly Entry _device = Ui.Entry("7F", "00-7F", 70);
    private readonly Picker _setup;
    private readonly Picker _special;
    private readonly Entry _event = Ui.Entry("1", "0-16383", 90);
    private readonly Entry _info = Ui.Entry("91 46 7F", "hex MIDI bytes", 200);
    private readonly Entry _name = Ui.Entry("Door slam", "event name", 200);
    private readonly Entry _userBits = Ui.Entry("12:34:56:78", "8 hex digits or 4 chars", 170);
    private readonly CheckBox _flagJ = new() { Color = Ui.Accent };
    private readonly CheckBox _flagI = new() { Color = Ui.Accent };
    private readonly View _timeRow, _deviceRow, _cueRow, _userBitsRow;
    private readonly Label _hex = Ui.MonoText("", 15, Ui.Accent);
    private readonly Label _output = Ui.MonoText("");
    private readonly Label _error = Ui.Text("", 13, color: Ui.Bad);
    private byte[] _messages = [];
    private IMtcMessage? _single;
    private IReadOnlyList<IMtcMessage> _sequence = [];

    public EncodePage(MtcSimulator simulator)
    {
        _sim = simulator;
        Title = "Encode";
        BackgroundColor = Ui.Background;

        _kind = Ui.Picker("Message", Kinds, 0, 280);
        _rate = Ui.Picker("SMPTE type", MtcFrameRateExtensions.All.Select(r => r.DisplayName()).ToList(), (int)MtcFrameRate.Fps30);
        _setup = Ui.Picker("Set-up type", CueingSetupTypeExtensions.All.Select(t => $"{(byte)t:X2}  {t.DisplayName()}").ToList(), (int)CueingSetupType.CuePoint, 300);
        _special = Ui.Picker("Special type", CueingSetupTypeExtensions.AllSpecial.Select(s => $"{s.SpecBytes()}  {s.DisplayName()}").ToList(), (int)CueingSpecialType.SystemStop, 260);

        _timeRow = Ui.Wrap(Ui.Labeled("Time (sub-frames .ff for cueing)", _time), Ui.Labeled("SMPTE type", _rate));
        _deviceRow = Ui.Wrap(Ui.Labeled("Device ID (hex)", _device));
        _cueRow = Ui.Wrap(Ui.Labeled("Set-up type", _setup), Ui.Labeled("Special (type 00)", _special),
            Ui.Labeled("Event number", _event), Ui.Labeled("Additional info (MIDI hex)", _info), Ui.Labeled("Event name (type 0E)", _name));
        _userBitsRow = Ui.Wrap(Ui.Labeled("User bits", _userBits),
            Ui.Row(_flagJ, Ui.Caption("j (bit 59)")), Ui.Row(_flagI, Ui.Caption("i (bit 43)")));

        foreach (var e in new[] { _time, _device, _event, _info, _name, _userBits }) e.TextChanged += (_, _) => Rebuild();
        foreach (var p in new[] { _kind, _rate, _setup, _special }) p.SelectedIndexChanged += (_, _) => Rebuild();
        _flagJ.CheckedChanged += (_, _) => Rebuild();
        _flagI.CheckedChanged += (_, _) => Rebuild();

        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Section("Message", Ui.Labeled("Kind", _kind), _timeRow, _deviceRow, _cueRow, _userBitsRow, _error),
                Ui.Section("Bytes", _hex,
                    Ui.Wrap(Ui.Button("Copy hex", CopyHex), Ui.Button("Send to simulator", SendToSimulator, Ui.Accent))),
                Ui.Section("Explained", _output)),
        };

        Rebuild();
    }

    private MtcFrameRate Rate => _rate.SelectedIndex < 0 ? MtcFrameRate.Fps30 : (MtcFrameRate)_rate.SelectedIndex;

    private byte Device()
    {
        var t = (_device.Text ?? "").Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) && b <= 0x7F) return b;
        throw new FormatException("Device ID must be hex 00-7F.");
    }

    private Timecode Time() =>
        Timecode.TryParse(_time.Text, out var tc, Rate) ? tc : throw new FormatException($"'{_time.Text}' is not a valid time at {Rate.DisplayName()}.");

    private void Rebuild()
    {
        if (_kind is null || _rate is null || _setup is null || _special is null || _timeRow is null) return;
        var kind = Math.Max(0, _kind.SelectedIndex);
        var isCue = kind is 4 or 5;
        _timeRow.IsVisible = kind is 0 or 1 or 2 or 4 or 7;
        _deviceRow.IsVisible = kind is 2 or 3 or 4 or 5 or 6;
        _cueRow.IsVisible = isCue;
        _userBitsRow.IsVisible = kind == 3;
        _special.IsVisible = isCue && _setup.SelectedIndex == (int)CueingSetupType.Special;

        try
        {
            _error.Text = "";
            _single = null;
            _sequence = [];
            switch (kind)
            {
                case 0:
                case 1:
                    BuildSequence(Time(), reverse: kind == 1);
                    break;
                case 7:
                    BuildTimeInfo(Time());
                    break;
                default:
                    _single = BuildSingle(kind);
                    _messages = _single.ToBytes();
                    _hex.Text = MtcHex.Format(_messages);
                    _output.Text = MtcDescriber.Describe(_single).ToString();
                    break;
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            _error.Text = ex.Message;
            _messages = [];
            _hex.Text = "";
            _output.Text = "";
        }
    }

    private IMtcMessage BuildSingle(int kind)
    {
        switch (kind)
        {
            case 2:
                return new FullTimecodeMessage(Time(), Device());
            case 3:
            {
                var text = _userBits.Text ?? "";
                var i = _flagI.IsChecked;
                var j = _flagJ.IsChecked;
                if (UserBits.TryParseDigits(text, out var bits)) return new UserBitsMessage(new UserBits(bits.Packed, i, j), Device());
                if (text.Length <= 4) return new UserBitsMessage(UserBits.FromAscii(text, i, j), Device());
                throw new FormatException("User bits: 8 hex digits (group 8 → 1, e.g. 12:34:56:78) or up to 4 characters.");
            }
            case 4:
            case 5:
            {
                var type = (CueingSetupType)Math.Max(0, _setup.SelectedIndex);
                int evt;
                if (type == CueingSetupType.Special) evt = Math.Max(0, _special.SelectedIndex);
                else if (!int.TryParse(_event.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out evt) || evt is < 0 or > MtcConstants.MaxEventNumber)
                    throw new FormatException("Event number must be 0-16383.");

                byte[] info = [];
                if (type.AdditionalInfoIsText()) info = Nibblizer.TextToBytes(_name.Text ?? "");
                else if (type.HasAdditionalInfo()) info = MtcHex.Parse(_info.Text ?? "");

                return kind == 4
                    ? new NonRealTimeCueingMessage(Device(), type, Time(), evt, info)
                    : new RealTimeCueingMessage(Device(), type, evt, info);
            }
            case 6:
                return new NakMessage(Device());
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private void BuildSequence(Timecode tc, bool reverse)
    {
        tc = tc.WithSubFrames(0);
        var seq = QuarterFrameMessage.CreateSequence(tc);
        var ordered = reverse ? seq.AsEnumerable().Reverse().ToArray() : seq;
        _sequence = ordered.Cast<IMtcMessage>().ToArray();
        _messages = ordered.SelectMany(m => m.ToBytes()).ToArray();
        _hex.Text = MtcHex.Format(_messages);

        var sb = new StringBuilder();
        sb.AppendLine($"{tc} — {tc.Rate.DisplayName()}, sent {(reverse ? "in reverse, F1 7X first" : "forward, F1 0X first")}.");
        sb.AppendLine($"One message every {tc.Rate.QuarterFrameDuration().TotalMilliseconds:0.###} ms; the 8 messages span 2 frames.");
        sb.AppendLine();
        foreach (var qf in ordered) sb.AppendLine($"F1 {qf.DataByte:X2}  {qf.Describe()}");
        sb.AppendLine();
        sb.AppendLine($"Frames {tc.Frames} = 0x{tc.Frames:X2}, seconds {tc.Seconds} = 0x{tc.Seconds:X2}, minutes {tc.Minutes} = 0x{tc.Minutes:X2}, hours byte 0x{tc.HoursByte:X2} (hours {tc.Hours}, type {(int)tc.Rate}).");
        sb.AppendLine(reverse
            ? $"Complete on F1 0X; a receiver shows {tc.AddFrames(-1)} (the frame entered below the boundary)."
            : $"Complete on F1 7X, when the time is 2 frames old; a receiver shows {tc.AddFrames(2)}.");
        if (tc.Rate != MtcFrameRate.Fps25 && tc.Frames % 2 == 1)
            sb.AppendLine("Note: at this rate a running generator only ever sends even sequence frame numbers.");
        _output.Text = sb.ToString().TrimEnd();
    }

    private void BuildTimeInfo(Timecode tc)
    {
        var r = tc.Rate;
        _messages = new FullTimecodeMessage(tc).ToBytes();
        _hex.Text = MtcHex.Format(_messages) + "   (as a Full Message)";
        var sb = new StringBuilder();
        sb.AppendLine($"{tc} — {r.DisplayName()}");
        sb.AppendLine($"Frame count since midnight : {tc.TotalFrames:N0} of {r.FramesPerDay():N0}");
        sb.AppendLine($"Real time since midnight   : {tc.ToTimeSpan():hh\\:mm\\:ss\\.fff}");
        sb.AppendLine($"Previous / next frame      : {tc.AddFrames(-1)} / {tc.AddFrames(1)}");
        sb.AppendLine($"Frame / quarter frame      : {r.FrameDuration().TotalMilliseconds:0.###} ms / {r.QuarterFrameDuration().TotalMilliseconds:0.###} ms");
        sb.AppendLine($"Hours byte 0yyzzzzz        : 0x{tc.HoursByte:X2}");
        var sequenceStart = tc.AddFrames(-(tc.TotalFrames & 1));
        sb.AppendLine($"Quarter-frame sequence     : encodes {sequenceStart}; this frame starts on piece {((tc.TotalFrames & 1) == 0 ? 0 : 4)}");
        sb.AppendLine();
        sb.AppendLine("Same moment at other rates:");
        foreach (var other in MtcFrameRateExtensions.All.Where(o => o != r))
            sb.AppendLine($"  {other.DisplayName(),-32} {tc.ConvertTo(other)}");
        if (r.IsDropFrame())
        {
            sb.AppendLine();
            sb.AppendLine("Drop-frame skips labels ;00 and ;01 at the start of every minute except 00, 10, 20, 30, 40 and 50.");
        }
        _output.Text = sb.ToString().TrimEnd();
    }

    private async void CopyHex()
    {
        if (_messages.Length == 0) return;
        try
        {
            await Clipboard.Default.SetTextAsync(MtcHex.Format(_messages));
        }
        catch (Exception ex)
        {
            _error.Text = $"Could not copy to the clipboard: {ex.Message}";
        }
    }

    private void SendToSimulator()
    {
        if (_single is not null)
        {
            _sim.Transmitter.Send(_single);
        }
        else if (_sequence.Count > 0)
        {
            if (_sim.Transmitter.IsPlaying) _sim.Transmitter.Stop();
            foreach (var m in _sequence) _sim.Transmitter.Send(m);
        }
        else return;
        _sim.Log("(sent from the Encode page — see the Simulator tab)");
    }
}
