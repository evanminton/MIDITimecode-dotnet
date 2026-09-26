using Microsoft.Maui.Dispatching;
using MidiTimecode;
using MidiTimecode.Messages;
using MidiTimecode.Sync;
using MtcExplorer.Services;

namespace MtcExplorer.Pages;

/// <summary>
/// Transmitter → cable → receiver. Play forward / reverse, rock in Cue mode, locate, shuttle,
/// vari-speed, lose messages, stop with a NAK, send user bits; watch the receiver lock.
/// </summary>
public sealed class SimulatorPage : ContentPage
{
    private readonly MtcSimulator _sim;
    private readonly IDispatcherTimer _timer;

    private readonly Label _txTime = Ui.MonoText("--:--:--:--", 40, Ui.Accent);
    private readonly Label _txInfo = Ui.Caption("");
    private readonly Label _rxTime = Ui.MonoText("--:--:--:--", 40, Ui.Good);
    private readonly Label _rxInfo = Ui.Caption("");
    private readonly Label _stats = Ui.MonoText("", 12, Ui.Muted);
    private readonly Label _log = Ui.MonoText("", 12);
    private readonly Label _speedLabel = Ui.Caption("Speed 1.00×");
    private readonly Label _lossLabel = Ui.Caption("Cable loss 0 %");
    private readonly Entry _time = Ui.Entry("01:00:00:00", "HH:MM:SS:FF", 150);
    private readonly Picker _rate;
    private readonly Entry _userBits = Ui.Entry("12:34:56:78", "8 hex digits or 4 chars", 170);
    private readonly Switch _reverse = new() { OnColor = Ui.Accent };
    private readonly Switch _logQuarterFrames = new() { OnColor = Ui.Accent, IsToggled = true };
    private readonly Label _error = Ui.Text("", 13, color: Ui.Bad);
    private DateTime _lastShuttle;
    private bool _shuttling;

    public SimulatorPage(MtcSimulator simulator)
    {
        _sim = simulator;
        Title = "Simulator";
        BackgroundColor = Ui.Background;

        _rate = Ui.Picker("Frame rate", MtcFrameRateExtensions.All.Select(r => r.DisplayName()).ToList(), (int)MtcFrameRate.Fps30);

        var speed = new Slider(0.1, 4.0, 1.0) { WidthRequest = 220, MinimumTrackColor = Ui.Accent };
        speed.ValueChanged += (_, e) =>
        {
            _sim.Transmitter.Speed = Math.Round(e.NewValue, 2);
            _speedLabel.Text = $"Speed {_sim.Transmitter.Speed:0.00}× (vari-speed)";
        };
        var loss = new Slider(0, 0.2, 0) { WidthRequest = 220, MinimumTrackColor = Ui.Bad };
        loss.ValueChanged += (_, e) =>
        {
            _sim.LossProbability = e.NewValue;
            _lossLabel.Text = $"Cable loss {e.NewValue * 100:0.#} % of messages";
        };
        _reverse.Toggled += (_, e) => _sim.Transmitter.Direction = e.Value ? MtcDirection.Reverse : MtcDirection.Forward;
        _logQuarterFrames.Toggled += (_, e) => _sim.LogQuarterFrames = e.Value;

        Content = new ScrollView
        {
            Content = Ui.Column(
                Ui.Section("Transmitter (generator)",
                    _txTime, _txInfo,
                    Ui.Wrap(Ui.Labeled("Start / locate time", _time), Ui.Labeled("SMPTE type", _rate)),
                    Ui.Wrap(
                        Ui.Button("Locate (Full Message)", Locate),
                        Ui.Button("▶ Play", Play, Ui.Accent),
                        Ui.Button("■ Stop", () => Stop(false)),
                        Ui.Button("Stop + NAK", () => Stop(true)),
                        Ui.Button("⇄ Rock (flip direction)", Rock),
                        Ui.Button("⏩ Shuttle to time", Shuttle)),
                    Ui.Wrap(
                        Ui.Row(_reverse, Ui.Text("Reverse")),
                        Ui.Labeled("", new VerticalStackLayout { Children = { _speedLabel, speed } }),
                        Ui.Labeled("", new VerticalStackLayout { Children = { _lossLabel, loss } })),
                    Ui.Wrap(Ui.Labeled("User bits", _userBits), Ui.Button("Send User Bits", SendUserBits)),
                    _error),
                Ui.Section("Receiver (reader)", _rxTime, _rxInfo, _stats),
                Ui.Section("Cable log (newest first)",
                    Ui.Wrap(Ui.Row(_logQuarterFrames, Ui.Text("Log quarter frames")),
                        Ui.Button("Clear log", _sim.ClearLog),
                        Ui.Button("Reset receiver", _sim.ResetCounters)),
                    _log)),
        };

        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33);
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _logQuarterFrames.IsToggled = _sim.LogQuarterFrames;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    private MtcFrameRate SelectedRate => _rate.SelectedIndex < 0 ? MtcFrameRate.Fps30 : (MtcFrameRate)_rate.SelectedIndex;

    private bool TryReadTime(out Timecode tc)
    {
        if (Timecode.TryParse(_time.Text, out tc, SelectedRate))
        {
            _error.Text = "";
            return true;
        }
        var parts = (_time.Text ?? "").Split(':', ';', '.', ',');
        var reason = parts.Length == 4 && parts.All(p => int.TryParse(p, out _))
            ? Timecode.Validate(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]), SelectedRate)
            : null;
        _error.Text = reason ?? "Enter a time as HH:MM:SS:FF.";
        return false;
    }

    private void Locate()
    {
        if (!TryReadTime(out var tc)) return;
        _shuttling = false;
        _sim.Transmitter.Locate(tc);
    }

    private void Play()
    {
        _shuttling = false;
        if (_sim.Transmitter.Mode == MtcTransportMode.Stopped && _sim.Transmitter.QuarterFramesSent == 0 && _sim.Receiver.State == MtcReceiverState.Stopped)
        {
            if (!TryReadTime(out var tc)) return;
            _sim.Transmitter.Locate(tc);
        }
        _sim.Transmitter.Play();
    }

    private void Stop(bool nak)
    {
        _shuttling = false;
        _sim.Transmitter.Stop(nak);
    }

    private void Rock()
    {
        _reverse.IsToggled = !_reverse.IsToggled; // Toggled handler flips the transmitter
    }

    /// <summary>Winds towards the entered time sending only Full Messages every so often, then stops there.</summary>
    private void Shuttle()
    {
        if (!TryReadTime(out _)) return;
        _shuttling = true;
        _lastShuttle = DateTime.MinValue;
    }

    private void StepShuttle()
    {
        if (!_shuttling || !TryReadTime(out var target)) return;
        if ((DateTime.UtcNow - _lastShuttle).TotalMilliseconds < 100) return;
        _lastShuttle = DateTime.UtcNow;

        var here = _sim.Transmitter.Position.ConvertTo(target.Rate);
        var distance = target.TotalFrames - here.TotalFrames;
        var perDay = target.Rate.FramesPerDay();
        if (distance > perDay / 2) distance -= perDay;
        if (distance < -perDay / 2) distance += perDay;
        var jump = Math.Clamp(distance, -target.Rate.FramesPerSecond() * 20L, target.Rate.FramesPerSecond() * 20L);
        var next = here.AddFrames(jump);
        var arrived = next == target;
        _sim.Transmitter.Locate(next, shuttle: !arrived);
        if (arrived) _shuttling = false;
    }

    private void SendUserBits()
    {
        var text = _userBits.Text ?? "";
        UserBits bits;
        if (UserBits.TryParseDigits(text, out var parsed)) bits = parsed;
        else if (text.Length <= 4) bits = UserBits.FromAscii(text);
        else { _error.Text = "User bits: 8 hex digits (group 8 → 1) or up to 4 characters."; return; }
        _error.Text = "";
        _sim.Transmitter.SendUserBits(bits);
    }

    private void Refresh()
    {
        StepShuttle();
        _sim.Receiver.CheckTimeout();

        var tx = _sim.Transmitter;
        _txTime.Text = tx.Position.ToString();
        _txInfo.Text = $"{tx.Mode.DisplayName()} · {tx.Direction.DisplayName()} · {tx.Rate.DisplayName()} · {tx.Speed:0.00}× · " +
                       $"{tx.QuarterFramesSent:N0} quarter frames sent{(_shuttling ? " · shuttling…" : "")}";

        var s = _sim.Receiver.Status;
        _rxTime.Text = s.Timecode?.ToString() ?? "--:--:--:--";
        _rxTime.TextColor = s.State switch
        {
            MtcReceiverState.Locked => Ui.Good,
            MtcReceiverState.Syncing or MtcReceiverState.Located => Ui.Warn,
            _ => Ui.Muted,
        };
        _rxInfo.Text = $"{s.State.DisplayName()} · {s.Direction.DisplayName()} · {(s.Rate is { } r ? r.DisplayName() : "type unknown")}";
        _stats.Text =
            $"Last complete sequence : {s.LastAssembled?.ToLongString() ?? "--"}  (display adds +2 frames forward, −1 reverse)\n" +
            $"Quarter frames received: {s.QuarterFrames:N0}   sequences: {s.Sequences:N0}   full messages: {s.FullMessages:N0}\n" +
            $"Discontinuities        : {s.Discontinuities:N0}   invalid sequences: {s.InvalidSequences:N0}\n" +
            $"Cable                  : {_sim.Sent:N0} sent, {_sim.Lost:N0} lost\n" +
            $"User bits              : {(s.UserBits is { } ub ? ub.ToString() : "--")}";

        _log.Text = string.Join('\n', _sim.GetLog(40));
    }
}
