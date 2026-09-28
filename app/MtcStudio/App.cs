using MtcStudio.Services;

namespace MtcStudio;

public sealed class App : Application
{
    private readonly MtcEngine _engine;

    public App(MtcEngine engine)
    {
        _engine = engine;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell(_engine))
        {
            Title = "MTC Studio",
            Width = 1280,
            Height = 860,
            MinimumWidth = 960,
            MinimumHeight = 640,
        };
        // Close the MIDI ports and stop the clock thread when the window goes away.
        window.Destroying += (_, _) => _engine.Dispose();
        return window;
    }
}
