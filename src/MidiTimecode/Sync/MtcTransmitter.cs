using System.Diagnostics;
using System.Runtime.InteropServices;
using MidiTimecode.Cueing;
using MidiTimecode.Messages;

namespace MidiTimecode.Sync;

/// <summary>Destination for encoded MIDI messages (a port, a loopback, a log…).</summary>
public interface IMidiOutput
{
    /// <summary>Sends one complete message. Called from the transmitter's clock thread while playing.</summary>
    void Send(ReadOnlySpan<byte> message);
}

/// <summary><see cref="IMidiOutput"/> that forwards to a delegate (the array is a fresh copy).</summary>
public sealed class DelegateMidiOutput(Action<byte[]> send) : IMidiOutput
{
    public void Send(ReadOnlySpan<byte> message) => send(message.ToArray());
}

/// <summary>
/// MIDI Time Code generator driven by a clock. In Play it sends Quarter Frame messages at
/// quarter-frame intervals (scaled by <see cref="Speed"/> for vari-speed), ascending or
/// descending. <see cref="Locate"/> stops quarter frames and sends a Full Message, as the
/// specification asks for fast-forward, rewind and shuttle.
/// </summary>
/// <remarks>
/// Timing is computed from elapsed clock time, so late wake-ups send the overdue quarter frames
/// in a short burst rather than drifting. Call <see cref="Pump"/> yourself (deterministic, e.g.
/// tests or an audio callback) or <see cref="StartClock"/> to run a background clock thread.
/// </remarks>
public sealed class MtcTransmitter : IDisposable
{
    private readonly object _gate = new();
    private readonly IMidiOutput _output;
    private readonly TimeProvider _time;
    private readonly QuarterFrameGenerator _generator;
    private readonly byte[] _buffer = new byte[32];

    private long _lastPumpTimestamp;
    private double _due;
    private double _speed = 1.0;
    private MtcTransportMode _mode = MtcTransportMode.Stopped;
    private Timecode _position;

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private readonly AutoResetEvent _wake = new(false);
    private bool _timerPeriodRaised;

    public MtcTransmitter(IMidiOutput output, Timecode start = default, TimeProvider? timeProvider = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _time = timeProvider ?? TimeProvider.System;
        _generator = new QuarterFrameGenerator(start);
        _position = start.WithSubFrames(0);
    }

    /// <summary>Device ID for Full, User Bits and NAK messages (default 7F, entire system).</summary>
    public byte DeviceId { get; set; } = MtcConstants.AllDevices;

    /// <summary>Vari-speed factor (1 = normal play). Must be positive; 0.01 - 16.</summary>
    public double Speed
    {
        get { lock (_gate) return _speed; }
        set
        {
            if (!(value >= 0.01 && value <= 16)) throw new ArgumentOutOfRangeException(nameof(value), value, "Speed must be 0.01-16.");
            lock (_gate) { Accumulate(); _speed = value; }
        }
    }

    /// <summary>Play direction; may be flipped while playing (Cue mode rocking).</summary>
    public MtcDirection Direction
    {
        get { lock (_gate) return _generator.Direction; }
        set
        {
            if (value == MtcDirection.Unknown) throw new ArgumentOutOfRangeException(nameof(value));
            lock (_gate) { Accumulate(); _generator.Direction = value; }
        }
    }

    /// <summary>Position: the frame being played (or the located frame while stopped).</summary>
    public Timecode Position { get { lock (_gate) return _position; } }

    public MtcFrameRate Rate { get { lock (_gate) return _generator.Rate; } }

    public MtcTransportMode Mode { get { lock (_gate) return _mode; } }

    public bool IsPlaying => Mode == MtcTransportMode.Play;

    /// <summary>Quarter frames the next <see cref="Pump"/> would send right now (fractional).</summary>
    public double PendingQuarterFrames { get { lock (_gate) { Accumulate(); return _due; } } }

    /// <summary>Most quarter frames sent in a single burst after a stall; beyond this time slips.</summary>
    public int MaxBurst { get; set; } = 8;

    public long QuarterFramesSent { get; private set; }

    /// <summary>Raised after each message is handed to the output (on the sending thread).</summary>
    public event Action<IMtcMessage>? MessageSent;

    /// <summary>
    /// Stops quarter frames (if playing) and sends a Full Message for <paramref name="timecode"/>.
    /// Resume with <see cref="Play"/>. Repeat while shuttling to update devices "every so often".
    /// </summary>
    public void Locate(Timecode timecode, bool shuttle = false)
    {
        lock (_gate)
        {
            _mode = shuttle ? MtcTransportMode.Shuttle : MtcTransportMode.Stopped;
            _due = 0;
            _generator.Locate(timecode);
            _position = _generator.CurrentFrame;
            SendLocked(new FullTimecodeMessage(_position, DeviceId));
        }
    }

    /// <summary>Starts sending quarter frames from the current position; the first goes out immediately.</summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_mode == MtcTransportMode.Play) return;
            if (_mode == MtcTransportMode.Shuttle) _generator.Locate(_position);
            _mode = MtcTransportMode.Play;
            _lastPumpTimestamp = _time.GetTimestamp();
            _due = 1.0;
        }
        _wake.Set();
    }

    /// <summary>Stops quarter frames. Optionally sends a NAK ("synchronization dropped").</summary>
    public void Stop(bool sendNak = false)
    {
        lock (_gate)
        {
            _mode = MtcTransportMode.Stopped;
            _due = 0;
            if (sendNak) SendLocked(new NakMessage(DeviceId));
        }
    }

    /// <summary>Sends a User Bits message (allowed at any time, in any mode).</summary>
    public void SendUserBits(UserBits bits)
    {
        lock (_gate) SendLocked(new UserBitsMessage(bits, DeviceId));
    }

    /// <summary>Sends any message (cueing, NAK…) through the same output, serialised with the clock.</summary>
    public void Send(IMtcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate) SendLocked(message);
    }

    /// <summary>Sends every quarter frame that is due. Returns how many were sent.</summary>
    public int Pump()
    {
        lock (_gate)
        {
            if (_mode != MtcTransportMode.Play) return 0;
            Accumulate();
            if (_due > MaxBurst) _due = MaxBurst;
            var sent = 0;
            while (_due >= 1.0)
            {
                _due -= 1.0;
                var qf = _generator.Next();
                _position = _generator.CurrentFrame;
                QuarterFramesSent++;
                SendLocked(qf);
                sent++;
            }
            return sent;
        }
    }

    /// <summary>Time until the next quarter frame is due at the current speed (zero if overdue, infinite if not playing).</summary>
    public TimeSpan TimeUntilNextQuarterFrame()
    {
        lock (_gate)
        {
            if (_mode != MtcTransportMode.Play) return Timeout.InfiniteTimeSpan;
            Accumulate();
            var remaining = 1.0 - _due;
            if (remaining <= 0) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(remaining / (_generator.Rate.QuarterFramesPerSecond() * _speed));
        }
    }

    /// <summary>Starts a background thread that calls <see cref="Pump"/> on time.</summary>
    public void StartClock()
    {
        lock (_gate)
        {
            if (_thread is not null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _thread = new Thread(() => ClockLoop(token)) { IsBackground = true, Name = "MTC clock", Priority = ThreadPriority.Highest };
            RaiseTimerResolution();
            _thread.Start();
        }
    }

    /// <summary>Stops the background clock thread (does not change the transport mode).</summary>
    public void StopClock()
    {
        Thread? thread;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            thread = _thread;
            if (thread is null) return;
            cts = _cts;
            cts!.Cancel();
            _thread = null;
            _cts = null;
        }
        _wake.Set();
        thread.Join(TimeSpan.FromSeconds(2));
        cts.Dispose();
        RestoreTimerResolution();
    }

    public void Dispose()
    {
        StopClock();
        _wake.Dispose();
    }

    private void ClockLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            Pump();
            var wait = TimeUntilNextQuarterFrame();
            if (wait == Timeout.InfiniteTimeSpan)
            {
                _wake.WaitOne(50);
                continue;
            }
            if (wait > TimeSpan.FromMilliseconds(2.5))
                _wake.WaitOne(wait - TimeSpan.FromMilliseconds(1.5));
            else if (wait > TimeSpan.Zero)
                Thread.Yield();
        }
    }

    private void Accumulate()
    {
        if (_mode != MtcTransportMode.Play) return;
        var now = _time.GetTimestamp();
        var elapsed = (now - _lastPumpTimestamp) / (double)_time.TimestampFrequency;
        _lastPumpTimestamp = now;
        if (elapsed > 0) _due += elapsed * _generator.Rate.QuarterFramesPerSecond() * _speed;
    }

    private void SendLocked(IMtcMessage message)
    {
        var length = message.Length;
        var buffer = length <= _buffer.Length ? _buffer.AsSpan(0, length) : new byte[length];
        message.WriteTo(buffer);
        _output.Send(buffer);
        MessageSent?.Invoke(message);
    }

    // Windows' default timer tick is ~15.6 ms, longer than a quarter frame (8.3 ms at 30 fps).
    private void RaiseTimerResolution()
    {
        if (!OperatingSystem.IsWindows() || _timerPeriodRaised) return;
        try { _timerPeriodRaised = TimeBeginPeriod(1) == 0; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
    }

    private void RestoreTimerResolution()
    {
        if (!_timerPeriodRaised) return;
        try { TimeEndPeriod(1); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        _timerPeriodRaised = false;
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}
