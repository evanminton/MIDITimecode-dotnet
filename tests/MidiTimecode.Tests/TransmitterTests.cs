namespace MidiTimecode.Tests;

public class TransmitterTests
{
    private sealed class Capture : IMidiOutput
    {
        public List<byte[]> Messages { get; } = [];
        public void Send(ReadOnlySpan<byte> message) => Messages.Add(message.ToArray());
    }

    private static void Run(MtcTransmitter tx, ManualTimeProvider clock, TimeSpan duration, TimeSpan step)
    {
        for (var t = TimeSpan.Zero; t < duration; t += step)
        {
            clock.Advance(step);
            tx.Pump();
        }
    }

    [Fact]
    public void Locate_Sends_Full_Message_And_Stops()
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, Timecode.Zero(), clock);
        tx.Locate(new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30));
        Assert.Equal(Bytes.Hex("F0 7F 7F 01 01 61 25 34 10 F7"), cap.Messages.Single());
        Assert.Equal(MtcTransportMode.Stopped, tx.Mode);
        Assert.Equal(0, tx.Pump());
    }

    [Fact]
    public void Play_Sends_Spec_Sequence_At_Quarter_Frame_Rate()
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, new Timecode(1, 37, 52, 16, MtcFrameRate.Fps30), clock);
        tx.Play();
        Assert.Equal(1, tx.Pump()); // first quarter frame immediately, on the frame boundary
        Run(tx, clock, TimeSpan.FromMilliseconds(7 * 8.3334), TimeSpan.FromMilliseconds(0.5));
        var bytes = cap.Messages.SelectMany(m => m).ToArray();
        Assert.Equal(Bytes.Hex("F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76"), bytes);
    }

    [Theory]
    [InlineData(MtcFrameRate.Fps24, 96)]
    [InlineData(MtcFrameRate.Fps25, 100)]
    [InlineData(MtcFrameRate.Fps30Drop, 119)]
    [InlineData(MtcFrameRate.Fps30, 120)]
    public void One_Second_Of_Play(MtcFrameRate rate, int perSecond)
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, Timecode.Zero(rate), clock);
        tx.Play();
        Run(tx, clock, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Assert.InRange(tx.QuarterFramesSent, perSecond, perSecond + 1);
    }

    [Fact]
    public void Speed_Scales_Rate()
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, Timecode.Zero(MtcFrameRate.Fps25), clock) { Speed = 2.0 };
        tx.Play();
        Run(tx, clock, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Assert.InRange(tx.QuarterFramesSent, 200, 201);
    }

    [Fact]
    public void Stall_Is_Capped_To_MaxBurst()
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, Timecode.Zero(), clock) { MaxBurst = 8 };
        tx.Play();
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(8, tx.Pump());
    }

    [Fact]
    public void Stop_With_Nak()
    {
        var clock = new ManualTimeProvider();
        var cap = new Capture();
        using var tx = new MtcTransmitter(cap, Timecode.Zero(), clock);
        tx.Play();
        tx.Pump();
        tx.Stop(sendNak: true);
        Assert.Equal(Bytes.Hex("F0 7E 7F 7E 00 F7"), cap.Messages[^1]);
        Assert.False(tx.IsPlaying);
    }

    [Fact]
    public void Transmitter_To_Receiver_Loopback_Locks_Forward_And_Reverse()
    {
        var clock = new ManualTimeProvider();
        var rx = new MtcReceiver(clock);
        using var tx = new MtcTransmitter(new DelegateMidiOutput(b => rx.Feed(b)), Timecode.Zero(), clock);
        var start = new Timecode(10, 0, 0, 0, MtcFrameRate.Fps30Drop);

        tx.Locate(start);
        Assert.Equal(MtcReceiverState.Located, rx.State);
        Assert.Equal(start, rx.Timecode);

        tx.Play();
        Run(tx, clock, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(1));
        Assert.Equal(MtcReceiverState.Locked, rx.State);
        var diff = rx.Timecode!.Value.TotalFrames - tx.Position.TotalFrames;
        Assert.InRange(diff, 0, 1);

        tx.Direction = MtcDirection.Reverse;
        Run(tx, clock, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1));
        Assert.Equal(MtcReceiverState.Locked, rx.State);
        Assert.Equal(MtcDirection.Reverse, rx.Direction);
        Assert.Equal(tx.Position, rx.Timecode);

        tx.Stop(sendNak: true);
        Assert.Equal(MtcReceiverState.Stopped, rx.State);
    }

    [Fact]
    public void Background_Clock_Sends_In_Real_Time()
    {
        var count = 0;
        using var tx = new MtcTransmitter(new DelegateMidiOutput(_ => Interlocked.Increment(ref count)), Timecode.Zero(MtcFrameRate.Fps30));
        tx.StartClock();
        tx.Play();
        Thread.Sleep(500);
        tx.Stop();
        tx.StopClock();
        Assert.InRange(count, 40, 80); // ~60 expected; generous for CI jitter
    }
}
