using MtcExplorer.Services;

namespace MtcExplorer;

public sealed class App : Application
{
    private readonly MtcSimulator _simulator;

    public App(MtcSimulator simulator)
    {
        _simulator = simulator;
        UserAppTheme = AppTheme.Dark;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new AppShell(_simulator)) { Title = "MTC Explorer" };
}
