using MidiTimecode.Messages;

namespace MidiTimecode.Sync;

/// <summary>
/// Streaming parser: feed raw MIDI bytes in any chunking and receive complete MTC messages.
/// Real-time bytes (<c>F8</c>-<c>FF</c>) may be interleaved anywhere, including inside a SysEx.
/// Channel messages and SysEx messages that are not MTC are skipped.
/// </summary>
public sealed class MtcParser
{
    private readonly byte[] _sysex;
    private int _sysexLength;
    private bool _inSysex;
    private bool _sysexOverflow;
    private bool _expectQuarterFrameData;

    public MtcParser(int maxSysExLength = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSysExLength, 16);
        _sysex = new byte[maxSysExLength];
    }

    /// <summary>Raised for every complete MTC message.</summary>
    public event Action<IMtcMessage>? MessageParsed;

    /// <summary>Raised for complete SysEx messages that are not MTC (including over-long ones, truncated).</summary>
    public event Action<byte[]>? OtherSysExReceived;

    /// <summary>Count of malformed MTC-looking input (e.g. <c>F1</c> not followed by a data byte).</summary>
    public long ErrorCount { get; private set; }

    public void Reset()
    {
        _inSysex = false;
        _sysexLength = 0;
        _sysexOverflow = false;
        _expectQuarterFrameData = false;
    }

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data) Feed(b);
    }

    public void Feed(byte b)
    {
        if (b >= 0xF8) return; // System Real Time: never interrupts parsing

        if (_inSysex)
        {
            if (b < 0x80)
            {
                if (_sysexLength < _sysex.Length) _sysex[_sysexLength++] = b;
                else _sysexOverflow = true;
                return;
            }
            if (b == MtcConstants.SysExEnd)
            {
                if (_sysexLength < _sysex.Length) _sysex[_sysexLength++] = b;
                else _sysexOverflow = true;
                _inSysex = false;
                CompleteSysEx();
                return;
            }
            // Any other status byte aborts the SysEx; fall through and process it.
            _inSysex = false;
            ErrorCount++;
        }

        if (_expectQuarterFrameData)
        {
            _expectQuarterFrameData = false;
            if (b < 0x80)
            {
                MessageParsed?.Invoke(QuarterFrameMessage.FromDataByte(b));
                return;
            }
            ErrorCount++;
        }

        switch (b)
        {
            case MtcConstants.SysExStart:
                _inSysex = true;
                _sysexOverflow = false;
                _sysexLength = 0;
                _sysex[_sysexLength++] = b;
                break;
            case MtcConstants.QuarterFrameStatus:
                _expectQuarterFrameData = true;
                break;
            default:
                break; // other status and data bytes are not MTC
        }
    }

    private void CompleteSysEx()
    {
        var span = _sysex.AsSpan(0, _sysexLength);
        if (!_sysexOverflow && MtcMessage.TryParse(span, out var message))
        {
            MessageParsed?.Invoke(message!);
            return;
        }
        if (!_sysexOverflow && IsMtcHeader(span)) ErrorCount++;
        OtherSysExReceived?.Invoke(span.ToArray());
    }

    private static bool IsMtcHeader(ReadOnlySpan<byte> s) =>
        s.Length >= 4 &&
        ((s[1] == MtcConstants.UniversalRealTime && s[3] is MtcConstants.SubIdMidiTimeCode or MtcConstants.SubIdRealTimeCueing) ||
         (s[1] == MtcConstants.UniversalNonRealTime && s[3] is MtcConstants.SubIdNonRealTimeCueing));

    /// <summary>Parses a whole buffer and returns the MTC messages found.</summary>
    public static List<IMtcMessage> ParseAll(ReadOnlySpan<byte> data)
    {
        var list = new List<IMtcMessage>();
        var parser = new MtcParser();
        parser.MessageParsed += list.Add;
        parser.Feed(data);
        return list;
    }
}
