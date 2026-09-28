using System.Globalization;
using MidiTimecode;
using MidiTimecode.Messages;
using MidiTimecode.Sync;
using MtcStudio.Services;

namespace MtcStudio.Pages;

/// <summary>
/// Generator (left) and reader (right) side by side: pick the MIDI output and input, run the
/// transport, and watch what comes back — lock state, direction, rate, user bits and counters.
/// </summary>
public sealed class StudioPage : ContentPage
{
    private readonly MtcEngine _engine;
    private readonly IDispatcherTimer _timer;
    private bool _updatingPickers;

    // Generator
    private readonly Picker _outPicker = Ui.Picker("MIDI output", [MtcEngine.NoPort], 0, 340);
    private readonly Label _outStatus = Ui.Caption("");
    private readonly Label _genTime = Ui.Display(76);
    private readonly Label _genMode = Ui.Text("", 14, color: Ui.Muted);
    private readonly Button _forward, _reverse, _playButton, _shuttleBack, _shuttleFwd;
    private readonly Picker _rate;
    private readonly Entry _start = Ui.Entry("", "HH:MM:SS:FF", 150);
    private readonly Entry _device = Ui.Entry("", "00-7F", 70);
    private readonly Slider _speed = new() { Minimum = 0.1, Maximum = 4, WidthRequest = 260, MinimumTrackColor = Ui.Accent, ThumbColor = Ui.Accent };
    private readonly Label _speedText = Ui.MonoText("1.00×", 14);
    private readonly CheckBox _fullBeforePlay = new() { Color = Ui.Accent };
    private readonly Entry _userBits = Ui.Entry("", "12:34:56:78 or REEL", 170);
    private readonly CheckBox _flagJ = new() { Color = Ui.Accent };
    private readonly CheckBox _flagI = new() { Color = Ui.Accent };
    private readonly Label _genCounters = Ui.Caption("");
    private readonly Label _genError = Ui.Text("", 13, color: Ui.Bad);

    // Reader
    private readonly Picker _inPicker = Ui.Picker("Reader source", [MtcEngine.NoPort], 0, 340);
    private readonly Label _inStatus = Ui.Caption("");
    private readonly Label _rxTime = Ui.Display(76);
    private readonly Label _rxState = Ui.Text("", 14, color: Ui.Muted);
    private readonly Label _rxDetails = Ui.MonoText("", 13);
    private readonly Entry _rxDevice = Ui.Entry("", "all", 70);
    private readonly Entry _dropout = Ui.Entry("", "ms", 80);
    private readonly Label _rxError = Ui.Text("", 13, color: Ui.Bad);

    public StudioPage(MtcEngine engine)
    {
        _engine = engine;
        Title = "Studio";
        BackgroundColor = Ui.Background;

        // ---------------- generator
        _forward = Ui.Button("Forward ▶", () => _engine.SetDirection(MtcDirection.Forward));
        _reverse = Ui.Button("◀ Reverse", () => _engine.SetDirection(MtcDirection.Reverse));
        _playButton = Ui.Button("Play", () => _engine.Play(), Ui.Good);
        _playButton.TextColor = Colors.Black;
        _shuttleBack = Ui.Button("◀◀ Rewind", () => ToggleShuttle(-1));
        _shuttleFwd = Ui.Button("Fast fwd ▶▶", () => ToggleShuttle(+1));

        _rate = Ui.Picker("SMPTE type", MtcFrameRateExtensions.All.Select(r => r.DisplayName()).ToList(), (int)_engine.Transmitter.Rate, 260);
        _rate.SelectedIndexChanged += (_, _) =>
        {
            if (_rate.SelectedIndex < 0) return;
            _engine.SetRate((MtcFrameRate)_rate.SelectedIndex);
            _start.Text = _engine.StartTime.ToString();
        };

        _start.Text = _engine.StartTime.ToString();
        _start.Completed += (_, _) => SetStart(locate: false);
        _device.Text = _engine.Transmitter.DeviceId.ToString("X2", CultureInfo.InvariantCulture);
        _device.Completed += (_, _) => ApplyDevice();
        _device.Unfocused += (_, _) => ApplyDevice();

        _speed.Value = Math.Clamp(_engine.Transmitter.Speed, _speed.Minimum, _speed.Maximum);
        _speed.ValueChanged += (_, e) => _engine.SetSpeed(Math.Round(e.NewValue / 0.05) * 0.05);

        _fullBeforePlay.IsChecked = _engine.FullBeforePlay;
        _fullBeforePlay.CheckedChanged += (_, e) => { _engine.FullBeforePlay = e.Value; Settings.FullBeforePlay = e.Value; };

        _userBits.Text = Settings.UserBits;

        _outPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingPickers || _outPicker.SelectedItem is not string name) return;
            _engine.SelectOutput(name);
            UpdatePortStatus();
        };

        var generator = Ui.Column(
            Ui.Section("Generator → MIDI output",
                Ui.Wrap(_outPicker, Ui.Button("Refresh ports", RefreshPorts)),
                _outStatus),
            Ui.Section("Time code out",
                _genTime,
                _genMode,
                Ui.Wrap(
                    Ui.Button("⏮ Start", () => _engine.GoToStart()),
                    _shuttleBack,
                    _playButton,
                    Ui.Button("Stop", () => _engine.Stop()),
                    Ui.Button("Stop + NAK", () => _engine.Stop(sendNak: true)),
                    _shuttleFwd),
                Ui.Wrap(
                    Ui.Button("−10 s", () => Nudge(-10, seconds: true)),
                    Ui.Button("−1 s", () => Nudge(-1, seconds: true)),
                    Ui.Button("−1 f", () => Nudge(-1, seconds: false)),
                    Ui.Button("+1 f", () => Nudge(1, seconds: false)),
                    Ui.Button("+1 s", () => Nudge(1, seconds: true)),
                    Ui.Button("+10 s", () => Nudge(10, seconds: true))),
                Ui.Wrap(_reverse, _forward),
                _genError),
            Ui.Section("Settings",
                Ui.Wrap(
                    Ui.Labeled("SMPTE type", _rate),
                    Ui.Labeled("Start time", _start),
                    Ui.Labeled(" ", Ui.Button("Set start", () => SetStart(locate: false))),
                    Ui.Labeled(" ", Ui.Button("Locate", () => SetStart(locate: true), Ui.Accent)),
                    Ui.Labeled("Device ID (hex)", _device)),
                Ui.Wrap(
                    Ui.Labeled("Speed (vari-speed)", _speed),
                    Ui.Labeled(" ", _speedText),
                    Ui.Labeled(" ", Ui.Button("0.5×", () => _speed.Value = 0.5)),
                    Ui.Labeled(" ", Ui.Button("1×", () => _speed.Value = 1)),
                    Ui.Labeled(" ", Ui.Button("2×", () => _speed.Value = 2))),
                Ui.Row(_fullBeforePlay, Ui.Caption("Send a Full Message before Play (readers jump straight to the position)")),
                Ui.Caption($"Rewind / fast forward send Full Messages only (no quarter frames) at {MtcEngine.ShuttleMultiple:0}× speed, as the specification asks for shuttling.")),
            Ui.Section("User bits",
                Ui.Wrap(
                    Ui.Labeled("8 hex digits (group 8 → 1) or up to 4 characters", _userBits),
                    Ui.Row(_flagJ, Ui.Caption("j (bit 59)")),
                    Ui.Row(_flagI, Ui.Caption("i (bit 43)")),
                    Ui.Button("Send user bits", SendUserBits))),
            _genCounters);

        // ---------------- reader
        _inPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingPickers || _inPicker.SelectedItem is not string name) return;
            _engine.SelectInput(name);
            UpdatePortStatus();
        };
        _rxDevice.Text = _engine.Receiver.DeviceId is { } d ? d.ToString("X2", CultureInfo.InvariantCulture) : "";
        _rxDevice.Completed += (_, _) => ApplyReaderDevice();
        _rxDevice.Unfocused += (_, _) => ApplyReaderDevice();
        _dropout.Text = ((int)_engine.Receiver.DropoutTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
        _dropout.Completed += (_, _) => ApplyDropout();
        _dropout.Unfocused += (_, _) => ApplyDropout();

        var reader = Ui.Column(
            Ui.Section("Reader ← MIDI input",
                Ui.Wrap(_inPicker, Ui.Button("Refresh ports", RefreshPorts)),
                _inStatus,
                Ui.Caption("\"Generator (internal loopback)\" reads the generator's own output: no cable or virtual port needed.")),
            Ui.Section("Time code in",
                _rxTime,
                _rxState,
                Ui.Wrap(Ui.Button("Reset reader", () => _engine.ResetReader()))),
            Ui.Section("Details", _rxDetails),
            Ui.Section("Reader settings",
                Ui.Wrap(
                    Ui.Labeled("Device ID filter (hex, blank = all)", _rxDevice),
                    Ui.Labeled("Dropout (ms without quarter frames = stopped)", _dropout)),
                Ui.Caption("Messages addressed to 7F (all devices) are always accepted."),
                _rxError));

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 0,
        };
        grid.Add(new ScrollView { Content = generator }, 0);
        grid.Add(new ScrollView { Content = reader }, 1);
        Content = grid;

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => Refresh();

        RefreshPorts();
        Refresh();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    // ---------------------------------------------------------------- actions

    private void RefreshPorts()
    {
        _updatingPickers = true;
        try
        {
            Fill(_outPicker, MtcEngine.OutputChoices(), _engine.OutputName);
            Fill(_inPicker, MtcEngine.InputChoices(), _engine.InputName);
        }
        finally { _updatingPickers = false; }
        UpdatePortStatus();
    }

    private static void Fill(Picker picker, IReadOnlyList<string> items, string current)
    {
        var list = items.ToList();
        picker.ItemsSource = list;
        picker.SelectedIndex = Math.Max(0, list.IndexOf(current));
    }

    private void UpdatePortStatus()
    {
        _outStatus.Text = _engine.OutputError ?? (_engine.OutputName == MtcEngine.NoPort
            ? "No output: the generator runs, but nothing leaves the PC. Pick a port (for another app on this PC use a virtual port such as loopMIDI)."
            : $"Sending to \"{_engine.OutputName}\".");
        _outStatus.TextColor = _engine.OutputError is null ? Ui.Muted : Ui.Bad;
        _inStatus.Text = _engine.InputError ?? _engine.InputName switch
        {
            MtcEngine.NoPort => "No source: the reader is idle.",
            MtcEngine.LoopbackSource => "Reading the generator's output directly.",
            var n => $"Listening on \"{n}\".",
        };
        _inStatus.TextColor = _engine.InputError is null ? Ui.Muted : Ui.Bad;
    }

    private void ToggleShuttle(int direction)
    {
        if (_engine.IsShuttling && Math.Sign(_engine.ShuttleFramesPerSecond) == direction) _engine.Stop();
        else _engine.Shuttle(direction);
    }

    private void Nudge(int amount, bool seconds)
    {
        var frames = seconds ? (long)amount * _engine.Transmitter.Rate.FramesPerSecond() : amount;
        _engine.Nudge(frames);
    }

    private void SetStart(bool locate)
    {
        _genError.Text = "";
        var rate = _engine.Transmitter.Rate;
        if (!Timecode.TryParse(_start.Text, out var tc, rate))
        {
            _genError.Text = $"\"{_start.Text}\" is not a valid HH:MM:SS:FF time at {rate.DisplayName()}.";
            return;
        }
        _engine.StartTime = tc;
        Settings.StartTime = tc.ToString();
        _start.Text = tc.ToString();
        if (locate) _engine.Locate(tc);
    }

    private void ApplyDevice()
    {
        _genError.Text = "";
        if (MtcEngine.TryParseDevice(_device.Text, out var id))
        {
            _engine.Transmitter.DeviceId = id;
            Settings.DeviceId = id.ToString("X2", CultureInfo.InvariantCulture);
            _device.Text = Settings.DeviceId;
        }
        else _genError.Text = "Device ID must be hex 00-7F (7F = all devices).";
    }

    private void ApplyReaderDevice()
    {
        _rxError.Text = "";
        var text = (_rxDevice.Text ?? "").Trim();
        if (text.Length == 0)
        {
            _engine.Receiver.DeviceId = null;
            Settings.ReaderDeviceId = "";
        }
        else if (MtcEngine.TryParseDevice(text, out var id))
        {
            _engine.Receiver.DeviceId = id;
            Settings.ReaderDeviceId = id.ToString("X2", CultureInfo.InvariantCulture);
            _rxDevice.Text = Settings.ReaderDeviceId;
        }
        else _rxError.Text = "Device ID filter must be hex 00-7F, or blank for all.";
    }

    private void ApplyDropout()
    {
        _rxError.Text = "";
        if (int.TryParse(_dropout.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms is >= 20 and <= 5000)
            _engine.SetDropout(TimeSpan.FromMilliseconds(ms));
        else _rxError.Text = "Dropout must be 20-5000 ms.";
    }

    private void SendUserBits()
    {
        _genError.Text = "";
        var text = _userBits.Text ?? "";
        UserBits bits;
        if (UserBits.TryParseDigits(text, out var digits)) bits = new UserBits(digits.Packed, _flagI.IsChecked, _flagJ.IsChecked);
        else if (text.Length <= 4) bits = UserBits.FromAscii(text, _flagI.IsChecked, _flagJ.IsChecked);
        else
        {
            _genError.Text = "User bits: 8 hex digits (e.g. 12:34:56:78) or up to 4 characters.";
            return;
        }
        Settings.UserBits = text;
        _engine.Transmitter.SendUserBits(bits);
    }

    // ---------------------------------------------------------------- display

    private void Refresh()
    {
        var tx = _engine.Transmitter;
        var pos = tx.Position;
        var mode = tx.Mode;
        var dir = tx.Direction;
        _genTime.Text = pos.ToString();
        _genTime.TextColor = mode switch
        {
            MtcTransportMode.Play => Ui.Accent,
            MtcTransportMode.Shuttle => Ui.Info,
            _ => Ui.Foreground,
        };
        _genMode.Text = $"{mode.DisplayName()} · {dir.DisplayName()} · {tx.Speed:0.00}× · {pos.Rate.DisplayName()}";
        _speedText.Text = string.Create(CultureInfo.InvariantCulture, $"{tx.Speed:0.00}×");
        _forward.BackgroundColor = dir == MtcDirection.Forward ? Ui.Accent : Ui.Border;
        _forward.TextColor = dir == MtcDirection.Forward ? Colors.Black : Ui.Foreground;
        _reverse.BackgroundColor = dir == MtcDirection.Reverse ? Ui.Accent : Ui.Border;
        _reverse.TextColor = dir == MtcDirection.Reverse ? Colors.Black : Ui.Foreground;
        var shuttle = Math.Sign(_engine.ShuttleFramesPerSecond);
        _shuttleBack.BackgroundColor = shuttle < 0 ? Ui.Info : Ui.Border;
        _shuttleFwd.BackgroundColor = shuttle > 0 ? Ui.Info : Ui.Border;
        if (_rate.SelectedIndex != (int)pos.Rate) _rate.SelectedIndex = (int)pos.Rate;

        _genCounters.Text = $"Sent {_engine.OutMessages:N0} messages ({_engine.OutQuarterFrames:N0} quarter frames)"
            + (_engine.OutErrors > 0 ? $" · {_engine.OutErrors:N0} send errors — last: {_engine.LastSendError}" : "");
        _genCounters.TextColor = _engine.OutErrors > 0 ? Ui.Bad : Ui.Muted;

        var st = _engine.Receiver.Status;
        _rxTime.Text = st.Timecode?.ToString() ?? "--:--:--:--";
        _rxTime.TextColor = st.State switch
        {
            MtcReceiverState.Locked => Ui.Good,
            MtcReceiverState.Syncing => Ui.Warn,
            MtcReceiverState.Located => Ui.Info,
            _ => st.Timecode is null ? Ui.Muted : Ui.Foreground,
        };
        _rxState.Text = $"{st.State.DisplayName()} · {st.Direction.DisplayName()} · {st.Rate?.DisplayName() ?? "rate unknown"}";

        var delta = st.Timecode is { } rt && rt.Rate == pos.Rate && _engine.InputName == MtcEngine.LoopbackSource
            ? $"{rt.TotalFrames - pos.TotalFrames:+#;-#;0} frames"
            : "—";
        _rxDetails.Text = string.Join('\n',
            $"Last full sequence   {st.LastAssembled?.ToString() ?? "—"}   (as sent; display adds +2 forward, −1 reverse)",
            $"User bits            {st.UserBits?.ToString() ?? "—"}",
            $"Quarter frames       {st.QuarterFrames:N0}",
            $"Complete sequences   {st.Sequences:N0}",
            $"Discontinuities      {st.Discontinuities:N0}",
            $"Invalid sequences    {st.InvalidSequences:N0}",
            $"Full Messages        {st.FullMessages:N0}",
            $"Parser errors        {_engine.Receiver.ParserErrors:N0}",
            $"MIDI in messages     {_engine.InMessages:N0} ({_engine.InOther:N0} other than MTC) · driver errors {_engine.InDriverErrors:N0}",
            $"Reader − generator   {delta}");
    }
}
