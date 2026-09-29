using MidiTimecode.Cueing;
using MidiTimecode.Messages;

namespace MidiTimecode.Sync;

/// <summary>Snapshot of what an <see cref="MtcReceiver"/> knows.</summary>
public readonly record struct MtcReceiverStatus(
    MtcReceiverState State,
    MtcDirection Direction,
    Timecode? Timecode,
    Timecode? LastAssembled,
    MtcFrameRate? Rate,
    UserBits? UserBits,
    long QuarterFrames,
    long Sequences,
    long Discontinuities,
    long InvalidSequences,
    long FullMessages);

/// <summary>Raised when the displayed time changes.</summary>
public sealed class MtcTimecodeEventArgs(Timecode timecode, MtcDirection direction, MtcReceiverState state, bool sequenceComplete, bool fromFullMessage) : EventArgs
{
    /// <summary>Time to display (forward sequences already include the +2 frame offset).</summary>
    public Timecode Timecode { get; } = timecode;
    public MtcDirection Direction { get; } = direction;
    public MtcReceiverState State { get; } = state;

    /// <summary>True when this update came from assembling a complete 8-message sequence.</summary>
    public bool SequenceComplete { get; } = sequenceComplete;

    /// <summary>True when this update came from a Full Message.</summary>
    public bool FromFullMessage { get; } = fromFullMessage;
}

/// <summary>
/// MIDI Time Code reader. Feed it bytes (<see cref="Feed(ReadOnlySpan{byte})"/>) or parsed
/// messages (<see cref="Process"/>); it follows the reader rules in the specification:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Lock needs a complete sequence read first-to-last (0→7 forward, 7→0 reverse), which
/// takes 2-4 frames depending on when the reader comes on line.</item>
/// <item>Direction is detected from ascending / descending piece numbers.</item>
/// <item>A forward-assembled time is 2 frames old when piece 7 arrives, so +2 frames is added for
/// display. In reverse the time is complete on piece 0, which falls on the start boundary of the
/// frame it encodes; the position has just moved into the frame before it, so −1 is applied (derived
/// from that boundary rule; the specification states no reverse offset).</item>
/// <item>Between completions the position is followed quarter frame by quarter frame, so the display
/// changes on every frame boundary (pieces 0 and 4) and follows direction changes at once.</item>
/// <item>Every complete sequence is verified against the followed position; a missing quarter frame
/// drops back to <see cref="MtcReceiverState.Syncing"/> until the next complete sequence.</item>
/// <item>The same piece twice in a row is a direction reversal on that boundary (Cue mode).</item>
/// <item>A Full Message sets the position without running; time runs from the next quarter frame.</item>
/// <item>A NAK, or no quarter frame for <see cref="DropoutTimeout"/>, means "tape has stopped".</item>
/// </list>
/// Not thread-safe for concurrent writers; <see cref="Status"/> may be read from any thread.
/// </remarks>
public sealed class MtcReceiver
{
    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private readonly MtcParser _parser = new();
    private readonly byte[] _nibbles = new byte[8];

    private int _lastPiece = -1;
    private int _mask;
    private int _sequenceStart = -1;
    private long _lastQuarterFrameTimestamp;
    private Timecode? _lastAssembled;
    private Timecode? _display;
    private long? _crossed;   // quarter-frame boundary last crossed, once known

    private MtcReceiverState _state = MtcReceiverState.Stopped;
    private MtcDirection _direction = MtcDirection.Unknown;
    private MtcFrameRate? _rate;
    private UserBits? _userBits;
    private long _quarterFrames, _sequences, _discontinuities, _invalid, _fullMessages;

    public MtcReceiver(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _parser.MessageParsed += Process;
    }

    /// <summary>
    /// No quarter frame for this long while running means stopped. Default 250 ms
    /// (about 24 quarter frames at 24 fps).
    /// </summary>
    public TimeSpan DropoutTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Only messages for this device ID or <c>7F</c> (all) are acted on; null accepts all.</summary>
    public byte? DeviceId { get; set; }

    public event EventHandler<MtcTimecodeEventArgs>? TimecodeChanged;
    public event EventHandler<MtcReceiverState>? StateChanged;
    public event EventHandler<MtcDirection>? DirectionChanged;
    public event EventHandler<FullTimecodeMessage>? FullMessageReceived;
    public event EventHandler<UserBitsMessage>? UserBitsReceived;
    public event EventHandler<CueingMessageBase>? CueingReceived;

    /// <summary>Raised for every message acted on (after state is updated).</summary>
    public event EventHandler<IMtcMessage>? MessageReceived;

    public MtcReceiverState State { get { lock (_gate) return _state; } }
    public MtcDirection Direction { get { lock (_gate) return _direction; } }

    /// <summary>Current display time, or null before anything is known.</summary>
    public Timecode? Timecode { get { lock (_gate) return _display; } }

    public MtcReceiverStatus Status
    {
        get
        {
            lock (_gate)
                return new MtcReceiverStatus(_state, _direction, _display, _lastAssembled, _rate, _userBits,
                    _quarterFrames, _sequences, _discontinuities, _invalid, _fullMessages);
        }
    }

    /// <summary>Parser errors seen by <see cref="Feed(ReadOnlySpan{byte})"/>.</summary>
    public long ParserErrors => _parser.ErrorCount;

    public void Feed(ReadOnlySpan<byte> data) => _parser.Feed(data);

    public void Feed(byte data) => _parser.Feed(data);

    public void Reset()
    {
        lock (_gate)
        {
            _parser.Reset();
            ResetSequence();
            _lastAssembled = null;
            _display = null;
            _crossed = null;
            _direction = MtcDirection.Unknown;
            _rate = null;
            _userBits = null;
            _quarterFrames = _sequences = _discontinuities = _invalid = _fullMessages = 0;
            SetState(MtcReceiverState.Stopped);
        }
    }

    /// <summary>Call periodically; moves to Stopped when quarter frames stop arriving. Returns true if it stopped.</summary>
    public bool CheckTimeout()
    {
        lock (_gate)
        {
            if (_state is not (MtcReceiverState.Syncing or MtcReceiverState.Locked)) return false;
            if (_time.GetElapsedTime(_lastQuarterFrameTimestamp) <= DropoutTimeout) return false;
            ResetSequence();
            SetState(MtcReceiverState.Stopped);
            return true;
        }
    }

    public void Process(IMtcMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_gate)
        {
            switch (message)
            {
                case QuarterFrameMessage qf:
                    OnQuarterFrame(qf);
                    break;
                case FullTimecodeMessage full when Accepts(full.DeviceId):
                    OnFull(full);
                    break;
                case UserBitsMessage ub when Accepts(ub.DeviceId):
                    _userBits = ub.Bits;
                    UserBitsReceived?.Invoke(this, ub);
                    break;
                case NakMessage nak when Accepts(nak.DeviceId):
                    ResetSequence();
                    SetState(MtcReceiverState.Stopped);
                    break;
                case CueingMessageBase cue when Accepts(cue.DeviceId):
                    CueingReceived?.Invoke(this, cue);
                    break;
                default:
                    return;
            }
            MessageReceived?.Invoke(this, message);
        }
    }

    private bool Accepts(byte deviceId) =>
        DeviceId is not { } mine || deviceId == MtcConstants.AllDevices || deviceId == mine;

    private void OnFull(FullTimecodeMessage full)
    {
        _fullMessages++;
        ResetSequence();
        _lastAssembled = null;
        _rate = full.Timecode.Rate;
        _display = full.Timecode;
        SetState(MtcReceiverState.Located);
        FullMessageReceived?.Invoke(this, full);
        TimecodeChanged?.Invoke(this, new MtcTimecodeEventArgs(full.Timecode, _direction, _state, false, true));
    }

    private void OnQuarterFrame(QuarterFrameMessage qf)
    {
        _quarterFrames++;
        _lastQuarterFrameTimestamp = _time.GetTimestamp();
        var p = (int)qf.Piece;

        // Direction from the step between consecutive pieces. The same piece twice in a row means
        // the same boundary was crossed again the other way (tape rocked in Cue mode).
        var step = MtcDirection.Unknown;
        var reversal = false;
        if (_lastPiece >= 0)
        {
            if (p == ((_lastPiece + 1) & 7)) step = MtcDirection.Forward;
            else if (p == ((_lastPiece + 7) & 7)) step = MtcDirection.Reverse;
            else if (p == _lastPiece && _direction != MtcDirection.Unknown)
            {
                step = _direction == MtcDirection.Forward ? MtcDirection.Reverse : MtcDirection.Forward;
                reversal = true;
            }
        }
        var wasRunning = _state is MtcReceiverState.Syncing or MtcReceiverState.Locked;
        _lastPiece = p;

        if (_state is MtcReceiverState.Stopped or MtcReceiverState.Located) SetState(MtcReceiverState.Syncing);

        if (step == MtcDirection.Unknown)
        {
            // First quarter frame, or one went missing.
            if (wasRunning && _quarterFrames > 1)
            {
                _discontinuities++;
                if (_state == MtcReceiverState.Locked) SetState(MtcReceiverState.Syncing);
            }
            _crossed = null;
            StartSequence(p);
            _nibbles[p] = qf.Value;
            return;
        }

        var restarted = false;
        if (step != _direction)
        {
            var hadDirection = _direction != MtcDirection.Unknown;
            _direction = step;
            DirectionChanged?.Invoke(this, step);
            if (hadDirection)
            {
                // The partial sequence belongs to the old direction; start collecting afresh.
                StartSequence(p);
                restarted = true;
            }
        }

        var startPiece = _direction == MtcDirection.Forward ? 0 : 7;
        var endPiece = _direction == MtcDirection.Forward ? 7 : 0;
        if (!restarted)
        {
            if (p == startPiece) StartSequence(p);
            else _mask |= 1 << p;
        }
        _nibbles[p] = qf.Value;

        // Follow the position boundary by boundary (piece number == boundary index mod 8).
        if (_crossed is { } c && _rate is { } rate)
            _crossed = reversal ? c : Wrap(c + (step == MtcDirection.Forward ? 1 : -1), rate);

        if (!restarted && p == endPiece && _mask == 0xFF && _sequenceStart == startPiece)
        {
            CompleteSequence();
            return;
        }

        if (_state == MtcReceiverState.Locked && _crossed is { } boundary && _rate is { } r)
            SetDisplay(DisplayFor(boundary, r), sequenceComplete: false);
    }

    private void CompleteSequence()
    {
        _sequences++;
        if (!QuarterFrameMessage.TryAssemble(_nibbles, out var assembled))
        {
            _invalid++;
            _crossed = null;
            if (_state == MtcReceiverState.Locked) SetState(MtcReceiverState.Syncing);
            return;
        }

        // Boundary of the piece just received: 7 of the sequence (forward) or 0 (reverse).
        var s = assembled.TotalFrames;
        var boundary = _direction == MtcDirection.Forward ? 4 * s + 7 : 4 * s;
        if (_crossed is { } predicted && (predicted != boundary || _rate != assembled.Rate)) _discontinuities++;

        _crossed = boundary;
        _lastAssembled = assembled;
        _rate = assembled.Rate;
        SetState(MtcReceiverState.Locked);
        SetDisplay(DisplayFor(boundary, assembled.Rate), sequenceComplete: true);
    }

    /// <summary>
    /// Frame to display after crossing <paramref name="boundary"/>. Forward: the frame entered,
    /// except on piece 7 where the specification's +2 (from the sequence frame) anticipates the next
    /// frame by one quarter. Reverse: the frame entered below the boundary.
    /// </summary>
    private Timecode DisplayFor(long boundary, MtcFrameRate rate)
    {
        long quarter = _direction == MtcDirection.Reverse
            ? Wrap(boundary - 1, rate)
            : ((boundary & 7) == 7 ? Wrap(boundary + 1, rate) : boundary);
        return MidiTimecode.Timecode.FromTotalFrames(quarter / 4, rate);
    }

    private static long Wrap(long boundary, MtcFrameRate rate)
    {
        var perDay = rate.FramesPerDay() * MtcConstants.QuarterFramesPerFrame;
        return ((boundary % perDay) + perDay) % perDay;
    }

    private void SetDisplay(Timecode value, bool sequenceComplete)
    {
        var changed = _display != value;
        _display = value;
        if (changed || sequenceComplete)
            TimecodeChanged?.Invoke(this, new MtcTimecodeEventArgs(value, _direction, _state, sequenceComplete, false));
    }

    private void StartSequence(int piece)
    {
        _sequenceStart = piece;
        _mask = 1 << piece;
    }

    private void ResetSequence()
    {
        _crossed = null;
        _lastPiece = -1;
        _mask = 0;
        _sequenceStart = -1;
    }

    private void SetState(MtcReceiverState state)
    {
        if (_state == state) return;
        _state = state;
        StateChanged?.Invoke(this, state);
    }
}
