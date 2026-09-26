namespace MidiTimecode.Tests;

public class ReceiverTests
{
    private static void Send(MtcReceiver rx, QuarterFrameMessage qf) => rx.Process(qf);

    [Theory]
    [InlineData(MtcFrameRate.Fps24)]
    [InlineData(MtcFrameRate.Fps25)]
    [InlineData(MtcFrameRate.Fps30Drop)]
    [InlineData(MtcFrameRate.Fps30)]
    public void Forward_Display_Tracks_Generator(MtcFrameRate rate)
    {
        var gen = new QuarterFrameGenerator(new Timecode(0, 0, 58, 0, rate));
        var rx = new MtcReceiver();
        var locked = false;
        for (var i = 0; i < 4 * 30 * 8; i++)
        {
            var qf = gen.Next();
            Send(rx, qf);
            if (rx.State != MtcReceiverState.Locked) { Assert.False(locked); continue; }
            locked = true;
            var expected = qf.Piece == QuarterFramePiece.HoursHighAndRate ? gen.CurrentFrame.AddFrames(1) : gen.CurrentFrame;
            Assert.Equal(expected, rx.Timecode);
            Assert.Equal(MtcDirection.Forward, rx.Direction);
        }
        Assert.True(locked);
        Assert.Equal(0, rx.Status.Discontinuities);
    }

    [Theory]
    [InlineData(MtcFrameRate.Fps24)]
    [InlineData(MtcFrameRate.Fps25)]
    [InlineData(MtcFrameRate.Fps30Drop)]
    [InlineData(MtcFrameRate.Fps30)]
    public void Reverse_Display_Tracks_Generator(MtcFrameRate rate)
    {
        var gen = new QuarterFrameGenerator(new Timecode(0, 10, 1, 0, rate), MtcDirection.Reverse);
        var rx = new MtcReceiver();
        var locked = false;
        for (var i = 0; i < 4 * 30 * 8; i++)
        {
            Send(rx, gen.Next());
            if (rx.State != MtcReceiverState.Locked) continue;
            locked = true;
            Assert.Equal(gen.CurrentFrame, rx.Timecode);
            Assert.Equal(MtcDirection.Reverse, rx.Direction);
        }
        Assert.True(locked);
        Assert.Equal(0, rx.Status.Discontinuities);
    }

    [Fact]
    public void Lock_Takes_One_Full_Sequence()
    {
        // Coming on line at piece 0: lock on the 8th message.
        var rx = new MtcReceiver();
        var gen = new QuarterFrameGenerator(new Timecode(1, 0, 0, 0));
        for (var i = 0; i < 7; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Syncing, rx.State);
        Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);

        // Coming on line at piece 1: must wait for the next piece 0, i.e. 7 + 8 messages.
        rx = new MtcReceiver();
        gen = new QuarterFrameGenerator(new Timecode(1, 0, 0, 0));
        gen.Next();
        for (var i = 0; i < 14; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Syncing, rx.State);
        Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);
    }

    [Fact]
    public void Missing_Quarter_Frame_Drops_Lock_Then_Relocks()
    {
        var rx = new MtcReceiver();
        var gen = new QuarterFrameGenerator(new Timecode(2, 0, 0, 0));
        for (var i = 0; i < 16; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);

        gen.Next(); // lost on the wire
        Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Syncing, rx.State);
        Assert.Equal(1, rx.Status.Discontinuities);

        for (var i = 0; i < 16; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);
    }

    [Fact]
    public void Direction_Change_Relocks_In_Reverse()
    {
        var rx = new MtcReceiver();
        var gen = new QuarterFrameGenerator(new Timecode(3, 0, 0, 0));
        var directions = new List<MtcDirection>();
        rx.DirectionChanged += (_, d) => directions.Add(d);
        for (var i = 0; i < 21; i++) Send(rx, gen.Next());
        gen.Direction = MtcDirection.Reverse;
        for (var i = 0; i < 24; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);
        Assert.Equal(MtcDirection.Reverse, rx.Direction);
        Assert.Equal(gen.CurrentFrame, rx.Timecode);
        Assert.Equal(new[] { MtcDirection.Forward, MtcDirection.Reverse }, directions);
    }

    [Fact]
    public void Full_Message_Locates_And_Quarter_Frame_Runs()
    {
        var rx = new MtcReceiver();
        var located = new Timecode(0, 30, 0, 0, MtcFrameRate.Fps25);
        rx.Process(new FullTimecodeMessage(located));
        Assert.Equal(MtcReceiverState.Located, rx.State);
        Assert.Equal(located, rx.Timecode);
        Assert.Equal(MtcFrameRate.Fps25, rx.Status.Rate);

        var gen = new QuarterFrameGenerator(located);
        Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Syncing, rx.State);
        for (var i = 0; i < 7; i++) Send(rx, gen.Next());
        Assert.Equal(MtcReceiverState.Locked, rx.State);
        Assert.Equal(located.AddFrames(2), rx.Timecode);
    }

    [Fact]
    public void Nak_Stops()
    {
        var rx = new MtcReceiver();
        var gen = new QuarterFrameGenerator(new Timecode(0, 0, 0, 0));
        for (var i = 0; i < 8; i++) Send(rx, gen.Next());
        rx.Feed(new NakMessage().ToBytes());
        Assert.Equal(MtcReceiverState.Stopped, rx.State);
    }

    [Fact]
    public void Silence_Times_Out()
    {
        var clock = new ManualTimeProvider();
        var rx = new MtcReceiver(clock) { DropoutTimeout = TimeSpan.FromMilliseconds(100) };
        var gen = new QuarterFrameGenerator(new Timecode(0, 0, 0, 0));
        for (var i = 0; i < 8; i++) { Send(rx, gen.Next()); clock.Advance(TimeSpan.FromMilliseconds(8)); }
        Assert.False(rx.CheckTimeout());
        clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(rx.CheckTimeout());
        Assert.Equal(MtcReceiverState.Stopped, rx.State);
    }

    [Fact]
    public void Device_Filter_And_User_Bits()
    {
        var rx = new MtcReceiver { DeviceId = 0x10 };
        rx.Process(new FullTimecodeMessage(new Timecode(1, 0, 0, 0), 0x11));
        Assert.Equal(MtcReceiverState.Stopped, rx.State);
        rx.Process(new FullTimecodeMessage(new Timecode(1, 0, 0, 0), 0x10));
        Assert.Equal(MtcReceiverState.Located, rx.State);
        rx.Process(new UserBitsMessage(new UserBits(0xCAFEF00D)));
        Assert.Equal(new UserBits(0xCAFEF00D), rx.Status.UserBits);
    }

    [Fact]
    public void Invalid_Assembled_Time_Is_Counted()
    {
        var rx = new MtcReceiver();
        // Frames 31 (invalid at any rate).
        rx.Feed(Bytes.Hex("F1 0F F1 11 F1 20 F1 30 F1 40 F1 50 F1 60 F1 76"));
        Assert.Equal(1, rx.Status.InvalidSequences);
        Assert.NotEqual(MtcReceiverState.Locked, rx.State);
    }
}
