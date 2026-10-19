using Avalonia;
using System;
using System.Linq;

namespace ShiftRoster;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--test"))
        {
            VerificationTests.RunAllTests();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
