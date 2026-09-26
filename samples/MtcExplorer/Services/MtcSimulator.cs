using MidiTimecode;
using MidiTimecode.Cueing;
using MidiTimecode.Describe;
using MidiTimecode.Messages;
using MidiTimecode.Sync;

namespace MtcExplorer.Services;

/// <summary>
/// A transmitter wired to a receiver through a simulated MIDI cable that can lose messages.
/// Everything that crosses the cable is logged in plain English.
/// </summary>
public sealed class MtcSimulator : IDisposable
{
    private const int MaxLog = 400;
    private readonly object _gate = new();
    private readonly LinkedList<string> _log = new();
    private long _sent, _lost;

    public MtcSimulator()
    {
        Receiver = new MtcReceiver();
        Transmitter = new MtcTransmitter(new DelegateMidiOutput(OnWire), new Timecode(1, 0, 0, 0, MtcFrameRate.Fps30));
        Receiver.StateChanged += (_, s) => Log($"RX  state → {s.DisplayName()}");
        Receiver.DirectionChanged += (_, d) => Log($"RX  direction → {d.DisplayName()}");
        Receiver.UserBitsReceived += (_, m) => Log($"RX  user bits {m.Bits}");
        Receiver.CueingReceived += (_, m) => Log($"RX  {m}");
        Receiver.FullMessageReceived += (_, m) => Log($"RX  located at {m.Timecode.ToLongString()}");
        Transmitter.StartClock();
    }

    public MtcTransmitter Transmitter { get; }
    public MtcReceiver Receiver { get; }

    /// <summary>Probability (0-1) that a message is lost on the cable.</summary>
    public double LossProbability { get; set; }

    /// <summary>Log every quarter frame (120 lines a second at 30 fps) or only the other traffic.</summary>
    public bool LogQuarterFrames { get; set; } = true;

    public long Sent => Interlocked.Read(ref _sent);
    public long Lost => Interlocked.Read(ref _lost);

    public void ResetCounters()
    {
        Interlocked.Exchange(ref _sent, 0);
        Interlocked.Exchange(ref _lost, 0);
        Receiver.Reset();
        ClearLog();
    }

    public IReadOnlyList<string> GetLog(int max)
    {
        lock (_gate) return _log.Take(max).ToArray();
    }

    public void ClearLog()
    {
        lock (_gate) _log.Clear();
    }

    public void Log(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss.fff}  {line}";
        lock (_gate)
        {
            _log.AddFirst(stamped);
            while (_log.Count > MaxLog) _log.RemoveLast();
        }
    }

    private void OnWire(byte[] bytes)
    {
        Interlocked.Increment(ref _sent);
        var lost = LossProbability > 0 && Random.Shared.NextDouble() < LossProbability;
        if (lost) Interlocked.Increment(ref _lost);
        else Receiver.Feed(bytes);

        var isQuarterFrame = bytes.Length == 2 && bytes[0] == MtcConstants.QuarterFrameStatus;
        if (isQuarterFrame && !LogQuarterFrames && !lost) return;

        string meaning;
        if (isQuarterFrame)
        {
            var qf = QuarterFrameMessage.FromDataByte(bytes[1]);
            meaning = $"QF{(int)qf.Piece} {qf.Piece.DisplayName()} = {qf.Value:X}";
        }
        else meaning = MtcDescriber.Describe(bytes).Title;
        Log($"TX  {MtcHex.Format(bytes),-14} {meaning}{(lost ? "   ✗ LOST" : "")}");
    }

    public void Dispose() => Transmitter.Dispose();
}
