namespace MidiTimecode.Sync;

/// <summary>Direction of time code, as signalled by the order of Quarter Frame pieces.</summary>
public enum MtcDirection
{
    /// <summary>Not yet known (fewer than two consecutive quarter frames).</summary>
    Unknown = 0,

    /// <summary>Pieces ascend 0 → 7: normal play or forward cueing.</summary>
    Forward = 1,

    /// <summary>Pieces descend 7 → 0: reverse play or reverse cueing.</summary>
    Reverse = 2,
}

/// <summary>State of an <see cref="MtcReceiver"/>.</summary>
public enum MtcReceiverState
{
    /// <summary>No time code: never started, NAK received, or quarter frames stopped arriving.</summary>
    Stopped = 0,

    /// <summary>A Full Message set the position; time runs from the next quarter frame.</summary>
    Located = 1,

    /// <summary>Quarter frames are arriving but a complete 8-message sequence has not been read yet.</summary>
    Syncing = 2,

    /// <summary>Complete sequences are arriving and the time is known.</summary>
    Locked = 3,
}

/// <summary>Operating modes from the "MTC Signal Path Summary".</summary>
public enum MtcTransportMode
{
    /// <summary>Nothing is being sent.</summary>
    Stopped = 0,

    /// <summary>Play (normal or vari-speed) or Cue mode: Quarter Frame messages, ascending or descending.</summary>
    Play = 1,

    /// <summary>Fast-forward / rewind / shuttle: no quarter frames, just an occasional Full Message.</summary>
    Shuttle = 2,
}

public static class MtcStateText
{
    public static string DisplayName(this MtcDirection d) => d switch
    {
        MtcDirection.Forward => "Forward",
        MtcDirection.Reverse => "Reverse",
        _ => "Unknown",
    };

    public static string DisplayName(this MtcReceiverState s) => s switch
    {
        MtcReceiverState.Stopped => "Stopped",
        MtcReceiverState.Located => "Located (waiting for quarter frames)",
        MtcReceiverState.Syncing => "Syncing (reading first sequence)",
        MtcReceiverState.Locked => "Locked",
        _ => s.ToString(),
    };

    public static string DisplayName(this MtcTransportMode m) => m switch
    {
        MtcTransportMode.Stopped => "Stopped",
        MtcTransportMode.Play => "Play (quarter frames)",
        MtcTransportMode.Shuttle => "Shuttle (full messages only)",
        _ => m.ToString(),
    };
}
