using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShiftRoster.Services;
using ShiftRoster.ViewModels;
using ShiftRoster.Views;

namespace ShiftRoster;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShiftRoster");
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "crash.log");
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled: {e.ExceptionObject}\n");
            }
            catch { }
        };

        ThemeService.Instance.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
