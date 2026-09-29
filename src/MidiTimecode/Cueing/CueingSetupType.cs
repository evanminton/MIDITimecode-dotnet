namespace MidiTimecode.Cueing;

/// <summary>MTC Cueing Set-Up Type (sub-ID #2 of the cueing messages).</summary>
public enum CueingSetupType : byte
{
    Special = 0x00,
    PunchIn = 0x01,
    PunchOut = 0x02,
    DeletePunchIn = 0x03,
    DeletePunchOut = 0x04,
    EventStart = 0x05,
    EventStop = 0x06,
    EventStartWithInfo = 0x07,
    EventStopWithInfo = 0x08,
    DeleteEventStart = 0x09,
    DeleteEventStop = 0x0A,
    CuePoint = 0x0B,
    CuePointWithInfo = 0x0C,
    DeleteCuePoint = 0x0D,
    EventName = 0x0E,
}

/// <summary>
/// Special set-up types. With <see cref="CueingSetupType.Special"/> the special type takes the
/// place of the event number (<c>sl sm</c>, LSB first — "01 00" is value 1).
/// </summary>
public enum CueingSpecialType : ushort
{
    TimeCodeOffset = 0x0000,
    EnableEventList = 0x0001,
    DisableEventList = 0x0002,
    ClearEventList = 0x0003,
    SystemStop = 0x0004,
    EventListRequest = 0x0005,
}

public static class CueingSetupTypeExtensions
{
    /// <summary>All defined set-up types in code order.</summary>
    public static IReadOnlyList<CueingSetupType> All { get; } =
        Enumerable.Range(0, 0x0F).Select(i => (CueingSetupType)i).ToArray();

    /// <summary>All defined special types in code order.</summary>
    public static IReadOnlyList<CueingSpecialType> AllSpecial { get; } =
        Enumerable.Range(0, 6).Select(i => (CueingSpecialType)i).ToArray();

    public static bool IsDefined(this CueingSetupType type) => (byte)type <= 0x0E;

    public static bool IsDefined(this CueingSpecialType type) => (ushort)type <= 0x0005;

    /// <summary>Types that carry additional information between the event number and EOX.</summary>
    public static bool HasAdditionalInfo(this CueingSetupType type) => type is
        CueingSetupType.EventStartWithInfo or CueingSetupType.EventStopWithInfo or
        CueingSetupType.CuePointWithInfo or CueingSetupType.EventName;

    /// <summary>True for Event Name, whose additional information is nibblized ASCII rather than MIDI.</summary>
    public static bool AdditionalInfoIsText(this CueingSetupType type) => type == CueingSetupType.EventName;

    /// <summary>Delete types (non-real-time only).</summary>
    public static bool IsDelete(this CueingSetupType type) => type is
        CueingSetupType.DeletePunchIn or CueingSetupType.DeletePunchOut or
        CueingSetupType.DeleteEventStart or CueingSetupType.DeleteEventStop or CueingSetupType.DeleteCuePoint;

    /// <summary>
    /// Types defined for Real Time cueing (<c>F0 7F … 05</c>): everything except the Delete types
    /// (03, 04, 09, 0A, 0D are reserved there). For Special only System Stop is defined.
    /// </summary>
    public static bool IsDefinedForRealTime(this CueingSetupType type) => type.IsDefined() && !type.IsDelete();

    /// <summary>Special types defined for Real Time cueing (only System Stop).</summary>
    public static bool IsDefinedForRealTime(this CueingSpecialType type) => type == CueingSpecialType.SystemStop;

    /// <summary>
    /// Specials 01 00 - 04 00 ignore the event time field ("Note that types 01 00 through 04 00 ignore
    /// the event time field", MTC Cueing, Special). This includes System Stop, even though its own
    /// description speaks of "a time when the unit may shut down".
    /// </summary>
    public static bool IgnoresEventTime(this CueingSpecialType type) => type is
        CueingSpecialType.EnableEventList or CueingSpecialType.DisableEventList or
        CueingSpecialType.ClearEventList or CueingSpecialType.SystemStop;

    /// <summary>Name as worded in the specification.</summary>
    public static string DisplayName(this CueingSetupType type) => type switch
    {
        CueingSetupType.Special => "Special",
        CueingSetupType.PunchIn => "Punch In point",
        CueingSetupType.PunchOut => "Punch Out point",
        CueingSetupType.DeletePunchIn => "Delete Punch In point",
        CueingSetupType.DeletePunchOut => "Delete Punch Out point",
        CueingSetupType.EventStart => "Event Start point",
        CueingSetupType.EventStop => "Event Stop point",
        CueingSetupType.EventStartWithInfo => "Event Start point with additional info",
        CueingSetupType.EventStopWithInfo => "Event Stop point with additional info",
        CueingSetupType.DeleteEventStart => "Delete Event Start point",
        CueingSetupType.DeleteEventStop => "Delete Event Stop point",
        CueingSetupType.CuePoint => "Cue point",
        CueingSetupType.CuePointWithInfo => "Cue point with additional info",
        CueingSetupType.DeleteCuePoint => "Delete Cue point",
        CueingSetupType.EventName => "Event Name in additional info",
        _ => $"Undefined set-up type {(byte)type:X2}",
    };

    /// <summary>What the set-up type means, paraphrasing the specification.</summary>
    public static string Description(this CueingSetupType type) => type switch
    {
        CueingSetupType.Special => "Set-up affecting the unit globally; the special type replaces the event number.",
        CueingSetupType.PunchIn => "Enable record mode; the event number is the track to record.",
        CueingSetupType.PunchOut => "Disable record mode; the event number is the track.",
        CueingSetupType.DeletePunchIn => "Delete the matching Punch In point (time and event number) from the cue list.",
        CueingSetupType.DeletePunchOut => "Delete the matching Punch Out point (time and event number) from the cue list.",
        CueingSetupType.EventStart => "Start running / playback of an event (a sequence or continuous event).",
        CueingSetupType.EventStop => "Stop running / playback of an event.",
        CueingSetupType.EventStartWithInfo => "Event Start with additional parameters (nibblized MIDI) before EOX.",
        CueingSetupType.EventStopWithInfo => "Event Stop with additional parameters (nibblized MIDI) before EOX.",
        CueingSetupType.DeleteEventStart => "Delete the matching Event Start (with or without info) from the cue list.",
        CueingSetupType.DeleteEventStop => "Delete the matching Event Stop (with or without info) from the cue list.",
        CueingSetupType.CuePoint => "Individual event occurrence, e.g. a sound-effect hit point or an edit reference.",
        CueingSetupType.CuePointWithInfo => "Cue point with additional parameters (nibblized MIDI) before EOX.",
        CueingSetupType.DeleteCuePoint => "Delete the matching Cue point (with or without info) from the cue list.",
        CueingSetupType.EventName => "Assign a name (nibblized ASCII) to an event number, for human logging.",
        _ => "Not defined by the specification.",
    };

    /// <summary>Name as worded in the specification.</summary>
    public static string DisplayName(this CueingSpecialType type) => type switch
    {
        CueingSpecialType.TimeCodeOffset => "Time Code Offset",
        CueingSpecialType.EnableEventList => "Enable Event List",
        CueingSpecialType.DisableEventList => "Disable Event List",
        CueingSpecialType.ClearEventList => "Clear Event List",
        CueingSpecialType.SystemStop => "System Stop",
        CueingSpecialType.EventListRequest => "Event List Request",
        _ => $"Undefined special {(ushort)type & 0x7F:X2} {((ushort)type >> 7) & 0x7F:X2}",
    };

    /// <summary>What the special type means, paraphrasing the specification.</summary>
    public static string Description(this CueingSpecialType type) => type switch
    {
        CueingSpecialType.TimeCodeOffset => "Relative time code offset for this unit (one offset per unit).",
        CueingSpecialType.EnableEventList => "Enable execution of the unit's event list when the matching time occurs. Time field ignored.",
        CueingSpecialType.DisableEventList => "Disable execution of the event list without erasing it. Time field ignored.",
        CueingSpecialType.ClearEventList => "Erase the unit's entire event list. Time field ignored.",
        CueingSpecialType.SystemStop => "Time at which the unit may shut down (guards against missing stops / tape run-out). Time field ignored.",
        CueingSpecialType.EventListRequest => "Master asks the peripheral to transmit its cue list as Set-Up messages, starting from the given time.",
        _ => "Not defined by the specification.",
    };

    /// <summary>The two event-number bytes as written in the specification, e.g. "04 00".</summary>
    public static string SpecBytes(this CueingSpecialType type) =>
        $"{(ushort)type & 0x7F:X2} {((ushort)type >> 7) & 0x7F:X2}";
}
