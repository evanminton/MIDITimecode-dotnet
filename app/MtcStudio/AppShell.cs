using MtcStudio.Pages;
using MtcStudio.Services;

namespace MtcStudio;

public sealed class AppShell : Shell
{
    public AppShell(MtcEngine engine)
    {
        Shell.SetBackgroundColor(this, Ui.Surface);
        Shell.SetForegroundColor(this, Ui.Foreground);
        Shell.SetTitleColor(this, Ui.Foreground);
        Shell.SetTabBarBackgroundColor(this, Ui.Surface);
        Shell.SetTabBarForegroundColor(this, Ui.Accent);
        Shell.SetTabBarTitleColor(this, Ui.Accent);
        Shell.SetTabBarUnselectedColor(this, Ui.Muted);

        var tabs = new TabBar();
        tabs.Items.Add(MakeTab("Studio", () => new StudioPage(engine)));
        tabs.Items.Add(MakeTab("Monitor", () => new MonitorPage(engine)));
        tabs.Items.Add(MakeTab("Send", () => new SendPage(engine)));
        tabs.Items.Add(MakeTab("Decode", () => new DecodePage()));
        tabs.Items.Add(MakeTab("Reference", () => new ReferencePage()));
        Items.Add(tabs);
    }

    private static Tab MakeTab(string title, Func<Page> create) => new()
    {
        Title = title,
        Items = { new ShellContent { Title = title, ContentTemplate = new DataTemplate(create) } },
    };
}
