using MtcExplorer.Pages;
using MtcExplorer.Services;

namespace MtcExplorer;

public sealed class AppShell : Shell
{
    public AppShell(MtcSimulator simulator)
    {
        Shell.SetBackgroundColor(this, Ui.Surface);
        Shell.SetForegroundColor(this, Ui.Foreground);
        Shell.SetTitleColor(this, Ui.Foreground);
        Shell.SetTabBarBackgroundColor(this, Ui.Surface);
        Shell.SetTabBarForegroundColor(this, Ui.Accent);
        Shell.SetTabBarTitleColor(this, Ui.Accent);
        Shell.SetTabBarUnselectedColor(this, Ui.Muted);

        var tabs = new TabBar();
        tabs.Items.Add(MakeTab("Simulator", () => new SimulatorPage(simulator)));
        tabs.Items.Add(MakeTab("Encode", () => new EncodePage(simulator)));
        tabs.Items.Add(MakeTab("Decode", () => new DecodePage()));
        tabs.Items.Add(MakeTab("Reference", () => new OptionsPage()));
        Items.Add(tabs);
    }

    private static Tab MakeTab(string title, Func<Page> create) => new()
    {
        Title = title,
        Items = { new ShellContent { Title = title, ContentTemplate = new DataTemplate(create) } },
    };
}
