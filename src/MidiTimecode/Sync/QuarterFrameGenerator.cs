using MidiTimecode.Messages;

namespace MidiTimecode.Sync;

/// <summary>
/// Produces the Quarter Frame stream for a continuously moving position, forward or reverse.
/// Pure and clock-free: every call to <see cref="Next"/> is the next quarter-frame boundary.
/// </summary>
/// <remarks>
/// <para>Positions are counted in quarter-frame boundaries since 00:00:00:00. Boundary
/// <c>4·f + q</c> is the start of quarter <c>q</c> (0-3) of frame <c>f</c>. Sequences cover two
/// frames and start on even frame counts, so for 24, 30 drop and 30 non-drop the sequence frame
/// number is always even; at 25 fps it alternates between even and odd every second, exactly as
/// the specification describes.</para>
/// <para>Piece 0 (<c>F1 0X</c>) is sent on the boundary at the start of the frame it encodes and
/// piece 4 on the next frame boundary. In reverse the same boundaries are crossed in descending
/// order (7 → 0), so <c>F1 0X</c> still falls on the boundary of the frame it represents.</para>
/// </remarks>
public sealed class QuarterFrameGenerator
{
    private long _boundary;       // next boundary crossed when moving forward
    private long _boundariesPerDay;

    public QuarterFrameGenerator(Timecode start, MtcDirection direction = MtcDirection.Forward)
    {
        Direction = direction == MtcDirection.Reverse ? MtcDirection.Reverse : MtcDirection.Forward;
        Locate(start);
    }

    public MtcFrameRate Rate { get; private set; }

    /// <summary>
    /// Direction of travel; may be changed at any time (e.g. tape rocked in Cue mode). Changed before
    /// any message since <see cref="Locate"/>, the located frame is still the first one sent.
    /// </summary>
    public MtcDirection Direction
    {
        get => _direction;
        set
        {
            if (value == _direction) return;
            _direction = value;
            if (MessagesSent == 0 && _boundariesPerDay != 0) Locate(CurrentFrame);
        }
    }

    private MtcDirection _direction;

    /// <summary>Frame containing the current position (the quarter frame just entered).</summary>
    public Timecode CurrentFrame { get; private set; }

    /// <summary>Messages produced since the last <see cref="Locate"/>.</summary>
    public long MessagesSent { get; private set; }

    /// <summary>
    /// Moves to the start of <paramref name="timecode"/> (forward) or its end (reverse) so the next
    /// message is the first quarter frame of that frame in the current direction.
    /// </summary>
    public void Locate(Timecode timecode)
    {
        Rate = timecode.Rate;
        _boundariesPerDay = Rate.FramesPerDay() * MtcConstants.QuarterFramesPerFrame;
        var frame = timecode.TotalFrames;
        _boundary = Mod(frame * 4 + (Direction == MtcDirection.Reverse ? 4 : 0));
        CurrentFrame = timecode.WithSubFrames(0);
        MessagesSent = 0;
    }

    /// <summary>The piece that <see cref="Next"/> will produce.</summary>
    public QuarterFramePiece NextPiece => PieceAt(Direction == MtcDirection.Reverse ? Mod(_boundary - 1) : _boundary);

    /// <summary>Produces the message for the next boundary in <see cref="Direction"/>.</summary>
    public QuarterFrameMessage Next()
    {
        long crossed, inside;
        if (Direction == MtcDirection.Reverse)
        {
            _boundary = Mod(_boundary - 1);
            crossed = _boundary;
            inside = Mod(crossed - 1);
        }
        else
        {
            crossed = _boundary;
            inside = crossed;
            _boundary = Mod(_boundary + 1);
        }
        CurrentFrame = Timecode.FromTotalFrames(inside / 4, Rate);
        MessagesSent++;
        return MessageAt(crossed, Rate);
    }

    /// <summary>The Quarter Frame message sent on boundary <paramref name="boundary"/>.</summary>
    public static QuarterFrameMessage MessageAt(long boundary, MtcFrameRate rate)
    {
        var frame = boundary / 4;
        var sequenceFrame = frame - (frame & 1);
        return QuarterFrameMessage.Create(Timecode.FromTotalFrames(sequenceFrame, rate), PieceAt(boundary));
    }

    private static QuarterFramePiece PieceAt(long boundary) =>
        (QuarterFramePiece)((((boundary / 4) & 1) * 4) + (boundary & 3));

    private long Mod(long value) => ((value % _boundariesPerDay) + _boundariesPerDay) % _boundariesPerDay;
}
