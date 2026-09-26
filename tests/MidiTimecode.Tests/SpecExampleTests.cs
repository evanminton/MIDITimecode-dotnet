namespace MidiTimecode.Tests;

/// <summary>Examples worked in the specification text.</summary>
public class SpecExampleTests
{
    [Fact]
    public void QuarterFrames_For_01_37_52_16_Match_Spec()
    {
        var tc = new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30);
        var bytes = QuarterFrameMessage.CreateSequence(tc).SelectMany(m => m.ToBytes()).ToArray();
        Assert.Equal(Bytes.Hex("F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76"), bytes);
    }

    [Fact]
    public void Piece7_Carries_Type_In_Bits_5_And_6()
    {
        var qf = QuarterFrameMessage.Create(new Timecode(1, 0, 0, 0, MtcFrameRate.Fps30), QuarterFramePiece.HoursHighAndRate);
        Assert.Equal(6, qf.Value); // "the value transmitted is 6 because the SMPTE Type (11 binary) is encoded in bits 5 and 6"
    }

    [Fact]
    public void Assemble_Spec_Example()
    {
        byte[] nibbles = [0x0, 0x1, 0x4, 0x3, 0x5, 0x2, 0x1, 0x6];
        Assert.True(QuarterFrameMessage.TryAssemble(nibbles, out var tc));
        Assert.Equal(new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30), tc);
    }

    [Fact]
    public void Receiver_Adds_Two_Frames_To_Spec_Example()
    {
        var rx = new MtcReceiver();
        rx.Feed(Bytes.Hex("F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76"));
        Assert.Equal(MtcReceiverState.Locked, rx.State);
        Assert.Equal(MtcDirection.Forward, rx.Direction);
        Assert.Equal(new Timecode(1, 37, 52, 18, MtcFrameRate.Fps30), rx.Timecode);
        Assert.Equal(new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30), rx.Status.LastAssembled);
    }

    [Fact]
    public void Nibblize_Note_On_Example()
    {
        Assert.Equal(Bytes.Hex("01 09 06 04 0F 07"), Nibblizer.Nibblize(Bytes.Hex("91 46 7F")));
        Assert.Equal(Bytes.Hex("91 46 7F"), Nibblizer.Denibblize(Bytes.Hex("01 09 06 04 0F 07")));
    }

    [Fact]
    public void Full_Message_Layout()
    {
        var full = new FullTimecodeMessage(new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30));
        Assert.Equal(Bytes.Hex("F0 7F 7F 01 01 61 25 34 10 F7"), full.ToBytes());
    }

    [Fact]
    public void Quarter_Frame_Interval_At_30fps_Is_8_333ms()
    {
        Assert.Equal(120, MtcFrameRate.Fps30.QuarterFramesPerSecond(), 6);
        Assert.Equal(8.333, MtcFrameRate.Fps30.QuarterFrameDuration().TotalMilliseconds, 3);
    }

    [Fact]
    public void Sequence_Frame_Numbers_Are_Even_Except_25fps()
    {
        foreach (var rate in new[] { MtcFrameRate.Fps24, MtcFrameRate.Fps30Drop, MtcFrameRate.Fps30 })
        {
            var gen = new QuarterFrameGenerator(new Timecode(0, 9, 58, 0, rate));
            for (var i = 0; i < 4 * 30 * 10; i++)
            {
                var m = gen.Next();
                if (m.Piece == QuarterFramePiece.FramesLow) Assert.Equal(0, m.Value & 1);
            }
        }

        var odd = false;
        var g25 = new QuarterFrameGenerator(new Timecode(0, 0, 0, 0, MtcFrameRate.Fps25));
        for (var i = 0; i < 4 * 25 * 3; i++)
        {
            var m = g25.Next();
            if (m.Piece == QuarterFramePiece.FramesLow && (m.Value & 1) == 1) odd = true;
        }
        Assert.True(odd);
    }

    [Fact]
    public void User_Bits_Characters_In_hg_fe_dc_ba_Order()
    {
        var ub = new UserBits(0x12345678);
        Assert.Equal(8, ub[1]);
        Assert.Equal(1, ub[8]);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x56, 0x78 }, ub.ToCharacterBytes());
        Assert.Equal("12:34:56:78", ub.ToDigitString());
        var msg = new UserBitsMessage(ub);
        Assert.Equal(Bytes.Hex("F0 7F 7F 01 02 08 07 06 05 04 03 02 01 00 F7"), msg.ToBytes());
    }
}
