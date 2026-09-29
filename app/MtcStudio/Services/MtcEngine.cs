using System.Collections.Concurrent;
using System.Globalization;
using MidiTimecode;
using MidiTimecode.Describe;
using MidiTimecode.Messages;
using MidiTimecode.Sync;
using MtcStudio.Midi;

namespace MtcStudio.Services;

public enum LogDirection { In, Out }

/// <summary>One line of the traffic monitor.</summary>
public sealed record LogEntry(DateTime Time, LogDirection Direction, byte[] Bytes, string Summary)
{
    public string Hex => MtcHex.Format(Bytes);

    public string Line =>
        string.Create(CultureInfo.InvariantCulture, $"{Time:HH:mm:ss.fff}  {(Direction == LogDirection.In ? "IN " : "OUT")}  {Clip(Hex, 44),-44}  {Summary}");

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>
/// The studio: an <see cref="MtcTransmitter"/> (generator) wired to a MIDI output, an
/// <see cref="MtcReceiver"/> (reader) fed from a MIDI input or from the generator itself, and a
/// traffic log. All MIDI goes through WinMM, so any port Windows lists works (hardware, USB,
/// loopMIDI and other virtual ports).
/// </summary>
public sealed class MtcEngine : IDisposable
{
    public const string NoPort = "(none)";
    public const string LoopbackSource = "Generator (internal loopback)";
    public const int MaxLogEntries = 5000;

    /// <summary>Shuttle (fast wind) speed as a multiple of play speed; Full Messages only, as the spec asks.</summary>
    public const double ShuttleMultiple = 10;

    private readonly object _outGate = new();
    private readonly object _inGate = new();
    private readonly ConcurrentQueue<LogEntry> _log = new();
    private readonly System.Threading.Timer _housekeeping;
    private readonly object _shuttleGate = new();
    private MidiOutPort? _out;
    private MidiInPort? _in;
    private volatile bool _readFromGenerator;

    // Shuttle state (housekeeping timer).
    private double _shuttleFramesPerSecond;
    private double _shuttleCarry;
    private long _shuttleLastTicks;
    private long _shuttleLastSendTicks;

    private long _outMessages, _outQuarterFrames, _outErrors, _inMessages, _inOther;

    public MtcEngine()
    {
        var rate = Settings.Rate;
        var start = Timecode.TryParse(Settings.StartTime, out var tc, rate) ? tc : new Timecode(1, 0, 0, 0, rate);
        StartTime = start;

        Transmitter = new MtcTransmitter(new DelegateOutput(Route), start);
        Transmitter.MessageSent += OnMessageSent;
        if (TryParseDevice(Settings.DeviceId, out var dev)) Transmitter.DeviceId = dev;
        var speed = Settings.Speed;
        if (speed is >= 0.01 and <= 16) Transmitter.Speed = speed;
        Transmitter.StartClock();

        Receiver = new MtcReceiver { DropoutTimeout = TimeSpan.FromMilliseconds(Math.Clamp(Settings.DropoutMs, 20, 5000)) };
        if (TryParseDevice(Settings.ReaderDeviceId, out var rdev)) Receiver.DeviceId = rdev;

        FullBeforePlay = Settings.FullBeforePlay;
        LogQuarterFrames = Settings.LogQuarterFrames;

        _housekeeping = new System.Threading.Timer(_ => Housekeeping(), null, 20, 20);

        // Reopen the ports used last time (silently skipped if they are gone).
        if (Settings.OutputPort != NoPort) SelectOutput(Settings.OutputPort, remember: false);
        if (Settings.InputPort != NoPort) SelectInput(Settings.InputPort, remember: false);
    }

    public MtcTransmitter Transmitter { get; }
    public MtcReceiver Receiver { get; }

    /// <summary>Where "Go to start" locates to.</summary>
    public Timecode StartTime { get; set; }

    /// <summary>Send a Full Message for the current position before quarter frames start, so readers jump straight there.</summary>
    public bool FullBeforePlay { get; set; }

    /// <summary>Log every quarter frame (120 per second at 30 fps); off logs only SysEx and other messages.</summary>
    public bool LogQuarterFrames { get; set; }

    public string OutputName { get; private set; } = NoPort;
    public string InputName { get; private set; } = NoPort;
    public string? OutputError { get; private set; }
    public string? InputError { get; private set; }
    public string? LastSendError { get; private set; }

    public long OutMessages => Interlocked.Read(ref _outMessages);
    public long OutQuarterFrames => Interlocked.Read(ref _outQuarterFrames);
    public long OutErrors => Interlocked.Read(ref _outErrors);
    public long InMessages => Interlocked.Read(ref _inMessages);
    public long InOther => Interlocked.Read(ref _inOther);
    public long InDriverErrors => _in?.Errors ?? 0;

    public bool IsShuttling => Volatile.Read(ref _shuttleFramesPerSecond) != 0;
    public double ShuttleFramesPerSecond => Volatile.Read(ref _shuttleFramesPerSecond);

    /// <summary>Raised (on a worker thread) when a port is opened or closed.</summary>
    public event Action? PortsChanged;

    // ---------------------------------------------------------------- ports

    public static IReadOnlyList<string> OutputChoices() => [NoPort, .. MidiDevices.Outputs().Select(p => p.Name)];

    public static IReadOnlyList<string> InputChoices() => [NoPort, LoopbackSource, .. MidiDevices.Inputs().Select(p => p.Name)];

    /// <summary>Opens a MIDI output by name (or closes it for <see cref="NoPort"/>). Returns an error message, or null.</summary>
    public string? SelectOutput(string name, bool remember = true)
    {
        MidiOutPort? old;
        lock (_outGate) { old = _out; _out = null; }
        old?.Dispose();
        OutputError = null;
        OutputName = NoPort;
        if (remember) Settings.OutputPort = name;
        if (name == NoPort) { PortsChanged?.Invoke(); return null; }

        var port = MidiDevices.Outputs().FirstOrDefault(p => p.Name == name);
        if (port is null)
        {
            OutputError = $"MIDI output \"{name}\" is not connected.";
        }
        else
        {
            try
            {
                var opened = MidiOutPort.Open(port);
                lock (_outGate) _out = opened;
                OutputName = name;
            }
            catch (MidiException ex) { OutputError = ex.Message; }
        }
        PortsChanged?.Invoke();
        return OutputError;
    }

    /// <summary>Sets the reader's source: a MIDI input, the generator (loopback), or nothing. Returns an error message, or null.</summary>
    public string? SelectInput(string name, bool remember = true)
    {
        MidiInPort? old;
        lock (_inGate) { old = _in; _in = null; _readFromGenerator = false; }
        if (old is not null)
        {
            old.Received -= OnInput;
            old.Dispose();
        }
        InputError = null;
        InputName = NoPort;
        if (remember) Settings.InputPort = name;
        ResetReader();

        if (name == LoopbackSource)
        {
            _readFromGenerator = true;
            InputName = name;
        }
        else if (name != NoPort)
        {
            var port = MidiDevices.Inputs().FirstOrDefault(p => p.Name == name);
            if (port is null)
            {
                InputError = $"MIDI input \"{name}\" is not connected.";
            }
            else
            {
                try
                {
                    var opened = MidiInPort.Open(port);
                    opened.Received += OnInput;
                    lock (_inGate) _in = opened;
                    InputName = name;
                }
                catch (MidiException ex) { InputError = ex.Message; }
            }
        }
        PortsChanged?.Invoke();
        return InputError;
    }

    // ---------------------------------------------------------------- transport

    public void Play()
    {
        StopShuttle();
        if (Transmitter.IsPlaying) return;
        if (FullBeforePlay) Transmitter.Locate(Transmitter.Position);
        Transmitter.Play();
    }

    public void Stop(bool sendNak = false)
    {
        StopShuttle();
        Transmitter.Stop(sendNak);
    }

    /// <summary>Jumps to <paramref name="timecode"/> with a Full Message; keeps playing if it was.</summary>
    public void Locate(Timecode timecode)
    {
        var wasPlaying = Transmitter.IsPlaying;
        StopShuttle();
        Transmitter.Locate(timecode);
        if (wasPlaying) Transmitter.Play();
    }

    public void Nudge(long frames) => Locate(Transmitter.Position.AddFrames(frames));

    public void GoToStart() => Locate(StartTime.Rate == Transmitter.Rate ? StartTime : StartTime.ConvertTo(Transmitter.Rate));

    /// <summary>Changes the SMPTE type, keeping the same frame label where it exists at the new rate.</summary>
    public void SetRate(MtcFrameRate rate)
    {
        Settings.Rate = rate;
        var p = Transmitter.Position;
        if (p.Rate == rate) return;
        var moved = SameLabel(p, rate);
        StartTime = SameLabel(StartTime, rate);
        Settings.StartTime = StartTime.ToString();   // saved at the new rate so the next launch can read it back
        Locate(moved);
    }

    /// <summary>
    /// The same HH:MM:SS:FF at another rate: frames above the new rate's last frame are clamped, and a
    /// label drop-frame skips (;00 or ;01 at the start of most minutes) moves on to ;02.
    /// </summary>
    private static Timecode SameLabel(Timecode t, MtcFrameRate rate)
    {
        var frames = Math.Min(t.Frames, rate.FramesPerSecond() - 1);
        if (rate.IsDropFrame() && t.Seconds == 0 && frames < 2 && t.Minutes % 10 != 0) frames = 2;
        return new Timecode(t.Hours, t.Minutes, t.Seconds, frames, rate);
    }

    public void SetDirection(MtcDirection direction) => Transmitter.Direction = direction;

    public void SetSpeed(double speed)
    {
        speed = Math.Clamp(speed, 0.01, 16);
        Transmitter.Speed = speed;
        Settings.Speed = speed;
    }

    /// <summary>Fast wind (<paramref name="direction"/> +1) or rewind (−1): Full Messages a few times a second, no quarter frames.</summary>
    public void Shuttle(int direction)
    {
        var fps = Transmitter.Rate.ActualFramesPerSecond() * ShuttleMultiple * Math.Sign(direction);
        if (fps == 0) { Stop(); return; }
        lock (_shuttleGate)
        {
            _shuttleCarry = 0;
            _shuttleLastTicks = Environment.TickCount64;
            _shuttleLastSendTicks = 0;
            Transmitter.Locate(Transmitter.Position, shuttle: true);
            Volatile.Write(ref _shuttleFramesPerSecond, fps);
        }
    }

    /// <summary>Ends shuttling; waits for an in-flight shuttle Full Message so it cannot land after the caller's transport change.</summary>
    private void StopShuttle()
    {
        lock (_shuttleGate) Volatile.Write(ref _shuttleFramesPerSecond, 0);
    }

    // ---------------------------------------------------------------- sending

    /// <summary>Sends any MTC message through the generator's output (serialised with its clock).</summary>
    public void Send(IMtcMessage message) => Transmitter.Send(message);

    /// <summary>Sends raw MIDI bytes (one or more complete messages) to the output.</summary>
    public void SendRaw(ReadOnlySpan<byte> data)
    {
        foreach (var (start, length) in MtcDescriber.Segment(data))
        {
            var msg = data.Slice(start, length);
            if (msg.IsEmpty) continue;
            Route(msg);
            Interlocked.Increment(ref _outMessages);
            if (msg[0] == MtcConstants.QuarterFrameStatus) Interlocked.Increment(ref _outQuarterFrames);
            if (msg[0] != MtcConstants.QuarterFrameStatus || LogQuarterFrames) AddLog(LogDirection.Out, msg.ToArray());
        }
    }

    // ---------------------------------------------------------------- reader

    public void ResetReader()
    {
        lock (_inGate) Receiver.Reset();
    }

    public void SetDropout(TimeSpan timeout)
    {
        Receiver.DropoutTimeout = timeout;
        Settings.DropoutMs = (int)timeout.TotalMilliseconds;
    }

    // ---------------------------------------------------------------- log

    /// <summary>Moves pending log entries into <paramref name="into"/> (single consumer: the Monitor page).</summary>
    public int DrainLog(List<LogEntry> into, int max = 1000)
    {
        var n = 0;
        while (n < max && _log.TryDequeue(out var e)) { into.Add(e); n++; }
        return n;
    }

    public void ClearLog() => _log.Clear();

    // ---------------------------------------------------------------- plumbing

    private void Route(ReadOnlySpan<byte> message)
    {
        lock (_outGate)
        {
            if (_out is { } port)
            {
                try { port.Send(message); }
                catch (Exception ex) when (ex is MidiException or ObjectDisposedException)
                {
                    Interlocked.Increment(ref _outErrors);
                    LastSendError = ex.Message;
                }
            }
        }
        if (_readFromGenerator)
        {
            lock (_inGate) Receiver.Feed(message);
        }
    }

    private void OnMessageSent(IMtcMessage message)
    {
        Interlocked.Increment(ref _outMessages);
        if (message.Kind == MtcMessageKind.QuarterFrame)
        {
            Interlocked.Increment(ref _outQuarterFrames);
            if (!LogQuarterFrames) return;
        }
        AddLog(LogDirection.Out, message.ToBytes());
    }

    private void OnInput(byte[] bytes)
    {
        Interlocked.Increment(ref _inMessages);
        lock (_inGate)
        {
            if (_readFromGenerator || _in is null) return;
            Receiver.Feed(bytes);
        }
        var status = bytes[0];
        if (status is 0xF8 or 0xFE) { Interlocked.Increment(ref _inOther); return; }   // clock / active sensing: counted, not logged
        if (status == MtcConstants.QuarterFrameStatus && !LogQuarterFrames) return;
        if (status != MtcConstants.QuarterFrameStatus && status != MtcConstants.SysExStart) Interlocked.Increment(ref _inOther);
        AddLog(LogDirection.In, bytes);
    }

    private void AddLog(LogDirection direction, byte[] bytes)
    {
        string summary;
        try { summary = MtcDescriber.Describe(bytes).Title; }
        catch (Exception ex) { summary = $"(could not describe: {ex.Message})"; }
        _log.Enqueue(new LogEntry(DateTime.Now, direction, bytes, summary));
        while (_log.Count > MaxLogEntries && _log.TryDequeue(out _)) { }
    }

    private void Housekeeping()
    {
        try
        {
            Receiver.CheckTimeout();

            if (Volatile.Read(ref _shuttleFramesPerSecond) == 0) return;
            lock (_shuttleGate)
            {
                var fps = _shuttleFramesPerSecond;
                if (fps == 0) return;
                var now = Environment.TickCount64;
                _shuttleCarry += (now - _shuttleLastTicks) / 1000.0 * fps;
                _shuttleLastTicks = now;
                if (now - _shuttleLastSendTicks < 100) return;   // "every so often": ~10 Full Messages a second
                var whole = (long)Math.Truncate(_shuttleCarry);
                _shuttleCarry -= whole;
                _shuttleLastSendTicks = now;
                Transmitter.Locate(Transmitter.Position.AddFrames(whole), shuttle: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Housekeeping failed: {ex}");
        }
    }

    public static bool TryParseDevice(string? text, out byte device)
    {
        device = MtcConstants.AllDevices;
        var t = (text ?? "").Trim();
        if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t[2..];
        if (t.EndsWith('h') || t.EndsWith('H')) t = t[..^1];
        return byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out device) && device <= 0x7F;
    }

    public void Dispose()
    {
        StopShuttle();
        _housekeeping.Dispose();
        Transmitter.StopClock();
        Transmitter.Dispose();
        SelectOutput(NoPort, remember: false);
        SelectInput(NoPort, remember: false);
    }

    /// <summary><see cref="IMidiOutput"/> over a span callback (no per-message allocation).</summary>
    private sealed class DelegateOutput(SpanAction send) : IMidiOutput
    {
        public void Send(ReadOnlySpan<byte> message) => send(message);
    }

    private delegate void SpanAction(ReadOnlySpan<byte> message);
}
