using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls.Hosting;
namespace SkiaBgRepro;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Diagnostika: zapiši zadnjo neujeto izjemo, da jo vidiš po restartu
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Preferences.Set("last_exception", e.ExceptionObject?.ToString() ?? "?");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Preferences.Set("last_exception", "UNOBSERVED: " + e.Exception);

        return MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureLifecycleEvents(events =>
            {
#if IOS
                events.AddiOS(ios => ios
                    .OnResignActivation(_ => CameraSim.OnLifecycle("WillResignActive", false))
                    .DidEnterBackground(_ => CameraSim.OnLifecycle("DidEnterBackground", false))
                    .WillEnterForeground(_ => CameraSim.OnLifecycle("WillEnterForeground", false))
                    .OnActivated(_ => CameraSim.OnLifecycle("Activated", true)));
#endif
            })
            .Build();
    }
}

public class App : Application
{
    protected override Window CreateWindow(IActivationState? state) => new(new MainPage());
}
