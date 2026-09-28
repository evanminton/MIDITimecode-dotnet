using MtcStudio.Services;

namespace MtcStudio;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.Services.AddSingleton<MtcEngine>();
        return builder.Build();
    }
}
