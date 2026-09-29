namespace MidiTimecode.Tests;

public class MessageTests
{
    private static T RoundTrip<T>(IMtcMessage message) where T : IMtcMessage
    {
        var bytes = message.ToBytes();
        Assert.Equal(message.Length, bytes.Length);
        Assert.True(MtcMessage.TryParse(bytes, out var parsed));
        var typed = Assert.IsType<T>(parsed);
        Assert.Equal(bytes, typed.ToBytes());
        return typed;
    }

    [Fact]
    public void QuarterFrame_All_Data_Bytes_RoundTrip()
    {
        for (var d = 0; d < 0x80; d++)
        {
            var qf = QuarterFrameMessage.FromDataByte((byte)d);
            Assert.Equal(d, qf.DataByte);
            RoundTrip<QuarterFrameMessage>(qf);
        }
        Assert.False(MtcMessage.TryParse(Bytes.Hex("F1 80"), out _));
        Assert.False(MtcMessage.TryParse(Bytes.Hex("F1"), out _));
    }

    [Fact]
    public void Full_RoundTrip_All_Rates()
    {
        foreach (var rate in MtcFrameRateExtensions.All)
        {
            var tc = Timecode.FromTotalFrames(123_456, rate);
            var m = RoundTrip<FullTimecodeMessage>(new FullTimecodeMessage(tc, 0x10));
            Assert.Equal(tc, m.Timecode);
            Assert.Equal(0x10, m.DeviceId);
        }
    }

    [Fact]
    public void Full_Rejects_Bad_Time_And_Length()
    {
        Assert.False(FullTimecodeMessage.TryParse(Bytes.Hex("F0 7F 7F 01 01 78 00 00 00 F7"), out _)); // hours 24
        Assert.False(FullTimecodeMessage.TryParse(Bytes.Hex("F0 7F 7F 01 01 41 01 00 00 F7"), out _)); // 01:01:00;00 is a dropped label
        Assert.True(FullTimecodeMessage.TryParse(Bytes.Hex("F0 7F 7F 01 01 41 0A 00 00 F7"), out _));  // 01:10:00;00 exists
        Assert.False(FullTimecodeMessage.TryParse(Bytes.Hex("F0 7F 7F 01 01 00 00 00 F7"), out _));
    }

    [Fact]
    public void UserBits_RoundTrip_With_Flags()
    {
        var bits = UserBits.FromAscii("REEL", flagBit43: true, flagBit59: false);
        var m = RoundTrip<UserBitsMessage>(new UserBitsMessage(bits));
        Assert.Equal("REEL", m.Bits.ToAscii());
        Assert.True(m.Bits.FlagBit43);
        Assert.False(m.Bits.FlagBit59);
        Assert.Equal(1, m.ToBytes()[13]);
        Assert.Equal(bits.WithGroup(1, 9)[1], 9);
        Assert.True(UserBits.TryParseDigits("01:02:03:04", out var bcd));
        Assert.True(bcd.IsBcd);
        Assert.Equal(4, bcd[1]);
    }

    [Fact]
    public void Nak_RoundTrip()
    {
        var m = RoundTrip<NakMessage>(new NakMessage(0x7F, 3));
        Assert.Equal(Bytes.Hex("F0 7E 7F 7E 03 F7"), m.ToBytes());
    }

    [Fact]
    public void NonRealTime_Cueing_Every_Type_RoundTrips()
    {
        var time = new Timecode(1, 2, 3, 4, MtcFrameRate.Fps25, 56);
        foreach (var type in CueingSetupTypeExtensions.All)
        {
            byte[] info = type.HasAdditionalInfo() ? [0x91, 0x46, 0x7F] : [];
            var m = RoundTrip<NonRealTimeCueingMessage>(new NonRealTimeCueingMessage(0x05, type, time, 0x1234, info));
            Assert.Equal(type, m.SetupType);
            Assert.Equal(time, m.EventTime);
            Assert.Equal(0x1234, m.EventNumber);
            Assert.Equal(info, m.AdditionalInfo.ToArray());
        }
    }

    [Fact]
    public void NonRealTime_Cueing_Layout()
    {
        var m = new NonRealTimeCueingMessage(0x01, CueingSetupType.CuePointWithInfo, new Timecode(0, 0, 10, 0, MtcFrameRate.Fps30, 50), 129, [0x91, 0x46, 0x7F]);
        Assert.Equal(Bytes.Hex("F0 7E 01 04 0C 60 00 0A 00 32 01 01 01 09 06 04 0F 07 F7"), m.ToBytes());
    }

    [Fact]
    public void Special_Types_Use_Event_Number_Field()
    {
        var m = NonRealTimeCueingMessage.Special(0x7F, CueingSpecialType.SystemStop, new Timecode(0, 59, 0, 0));
        var bytes = m.ToBytes();
        Assert.Equal(0x04, bytes[10]); // "04 00"
        Assert.Equal(0x00, bytes[11]);
        Assert.Equal(CueingSpecialType.SystemStop, RoundTrip<NonRealTimeCueingMessage>(m).SpecialType);
        Assert.True(CueingSpecialType.SystemStop.IgnoresEventTime());
        Assert.False(CueingSpecialType.EventListRequest.IgnoresEventTime());
    }

    [Theory]
    [InlineData("01")] // Enable Event List
    [InlineData("02")] // Disable Event List
    [InlineData("03")] // Clear Event List
    [InlineData("04")] // System Stop
    public void Specials_That_Ignore_Time_Accept_Any_Time_Bytes(string special)
    {
        // Hours 31, minutes 99: not a valid address, but the field is ignored for these specials.
        Assert.True(NonRealTimeCueingMessage.TryParse(Bytes.Hex($"F0 7E 01 04 00 7F 63 00 00 00 {special} 00 F7"), out var m));
        Assert.Equal((CueingSpecialType)Convert.ToInt32(special, 16), m!.SpecialType);
    }

    [Theory]
    [InlineData("F0 7E 01 04 00 7F 63 00 00 00 00 00 F7")] // Time Code Offset uses the time
    [InlineData("F0 7E 01 04 00 7F 63 00 00 00 05 00 F7")] // Event List Request uses the time
    [InlineData("F0 7E 01 04 0B 7F 63 00 00 00 04 00 F7")] // Cue point 4, not a special
    public void Invalid_Time_Still_Rejected_Where_Time_Is_Used(string hex) =>
        Assert.False(NonRealTimeCueingMessage.TryParse(Bytes.Hex(hex), out _));

    [Fact]
    public void Event_Name_Is_Nibblized_Ascii()
    {
        var m = NonRealTimeCueingMessage.EventName(0x02, new Timecode(0, 0, 0, 0), 7, "Hit\r\n");
        Assert.Equal("Hit\r\n", RoundTrip<NonRealTimeCueingMessage>(m).EventNameText);
        Assert.Equal(Bytes.Hex("08 04"), m.ToBytes()[12..14]); // 'H' = 48
    }

    [Fact]
    public void RealTime_Cueing()
    {
        var stop = RealTimeCueingMessage.SystemStop(0x7F);
        Assert.Equal(Bytes.Hex("F0 7F 7F 05 00 04 00 F7"), stop.ToBytes());
        Assert.True(stop.IsDefinedByStandard);

        foreach (var type in CueingSetupTypeExtensions.All)
        {
            byte[] info = type.HasAdditionalInfo() ? [0x01] : [];
            var m = RoundTrip<RealTimeCueingMessage>(new RealTimeCueingMessage(0x03, type, 99, info));
            Assert.Equal(type.IsDefinedForRealTime() && type != CueingSetupType.Special, m.IsDefinedByStandard);
        }
    }

    [Fact]
    public void Cueing_Rejects_Bad_Nibbles()
    {
        Assert.False(MtcMessage.TryParse(Bytes.Hex("F0 7F 7F 05 07 01 00 1F 00 F7"), out _));
        Assert.False(MtcMessage.TryParse(Bytes.Hex("F0 7F 7F 05 07 01 00 01 F7"), out _));
    }

    [Fact]
    public void Constructors_Validate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuarterFrameMessage(QuarterFramePiece.FramesLow, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FullTimecodeMessage(default, 0x80));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RealTimeCueingMessage(0, CueingSetupType.CuePoint, 0x4000));
    }

    [Fact]
    public void Parser_Handles_Chunking_And_Interleaved_RealTime()
    {
        var stream = Bytes.Hex("90 40 7F F1 F8 00 F0 7F 7F F8 01 01 61 25 34 10 F7 F1 11 FE F0 43 12 00 F7 F1 24");
        var found = new List<IMtcMessage>();
        var other = 0;
        var parser = new MtcParser();
        parser.MessageParsed += found.Add;
        parser.OtherSysExReceived += _ => other++;
        foreach (var b in stream) parser.Feed(b);

        Assert.Equal(4, found.Count);
        Assert.Equal(QuarterFrameMessage.FromDataByte(0x00), found[0]);
        Assert.IsType<FullTimecodeMessage>(found[1]);
        Assert.Equal(QuarterFrameMessage.FromDataByte(0x11), found[2]);
        Assert.Equal(QuarterFrameMessage.FromDataByte(0x24), found[3]);
        Assert.Equal(1, other);
        Assert.Equal(4, MtcParser.ParseAll(stream).Count);
    }

    [Fact]
    public void Parser_Aborted_SysEx_Is_Counted()
    {
        var parser = new MtcParser();
        var found = new List<IMtcMessage>();
        parser.MessageParsed += found.Add;
        parser.Feed(Bytes.Hex("F0 7F 7F 01 F1 20"));
        Assert.Single(found);
        Assert.Equal(1, parser.ErrorCount);
    }

    [Fact]
    public void Describer_Covers_Every_Kind()
    {
        IMtcMessage[] all =
        [
            QuarterFrameMessage.Create(new Timecode(1, 37, 52, 16), QuarterFramePiece.HoursHighAndRate),
            new FullTimecodeMessage(new Timecode(1, 37, 52, 16)),
            new UserBitsMessage(new UserBits(0x01020304)),
            NonRealTimeCueingMessage.EventName(1, new Timecode(0, 0, 1, 0), 3, "Boom"),
            new RealTimeCueingMessage(1, CueingSetupType.EventStartWithInfo, 3, [0x91, 0x46, 0x7F]),
            new NakMessage(),
        ];
        foreach (var m in all)
        {
            var d = MtcDescriber.Describe(m);
            Assert.NotEmpty(d.Fields);
            Assert.DoesNotContain("Not MIDI Time Code", d.Title);
        }
        Assert.Contains("Note On", MtcDescriber.Describe(all[4]).ToString());
        Assert.Contains("Boom", MtcDescriber.Describe(all[3]).ToString());
        Assert.Contains("30 Frames/Second (Non-Drop)", MtcDescriber.Describe(all[0]).ToString());
    }

    [Fact]
    public void Describe_Stream_Reports_Lock()
    {
        var text = MtcDescriber.DescribeStreamText(Bytes.Hex("F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76"));
        Assert.Contains("Sequence complete", text);
        Assert.Contains("01:37:52:18", text);
        Assert.Contains("Locked", text);
    }

    [Fact]
    public void Hex_Parsing_Is_Forgiving()
    {
        Assert.Equal(new byte[] { 0xF1, 0x10, 0x7F }, MtcHex.Parse("0xF1, 10H 7f"));
        Assert.Equal(new byte[] { 0xF1, 0x7F }, MtcHex.Parse("F17F"));
        Assert.False(MtcHex.TryParse("zz", out _));
    }

    [Fact]
    public void Options_Catalog_Is_Complete()
    {
        Assert.Equal(4, MtcOptions.Find("rates")!.Options.Count);
        Assert.Equal(8, MtcOptions.Find("quarter")!.Options.Count);
        Assert.Equal(15, MtcOptions.Find("setup")!.Options.Count);
        Assert.Equal(6, MtcOptions.Find("special")!.Options.Count);
        Assert.Equal(6, MtcOptions.Find("messages")!.Options.Count);
        Assert.Contains("Event List Request", MtcOptions.ToText());
    }

    [Fact]
    public void MidiDataDescriber_Keeps_Running_Status_Across_Real_Time_Bytes()
    {
        var lines = MidiDataDescriber.Describe(Bytes.Hex("90 3C 40 F8 3E 40"));
        Assert.Equal(3, lines.Count);
        Assert.Contains("Timing Clock", lines[1]);
        Assert.Contains("Note On ch 1, note 62, velocity 64 (running status)", lines[2]);
    }

    [Fact]
    public void MidiDataDescriber_System_Common_Cancels_Running_Status()
    {
        var lines = MidiDataDescriber.Describe(Bytes.Hex("90 3C 40 F6 3E"));
        Assert.Contains("stray data byte", lines[^1]);
    }
}
