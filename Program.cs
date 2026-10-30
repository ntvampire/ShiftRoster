using Avalonia;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace ShiftRoster;

sealed class Program
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
        {
            HandleFatalException(eventArgs.ExceptionObject as Exception);
        };

        try
        {
            if (args.Contains("--test"))
            {
                VerificationTests.RunAllTests();
                Environment.Exit(0);
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            if (args.Contains("--test"))
            {
                Console.Error.WriteLine($"\n[ТЕСТ ПРОВАЛЕН]: {ex.Message}\n{ex.StackTrace}");
                Environment.Exit(1);
            }
            HandleFatalException(ex);
        }
    }

    private static void HandleFatalException(Exception? ex)
    {
        if (ex == null) return;

        string errorTitle = "Ошибка запуска ShiftRoster";
        string logPath = "";

        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShiftRoster");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, "crash.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] КРИТИЧЕСКИЙ СБОЙ ПРИЛОЖЕНИЯ:\n{ex}\n");
        }
        catch
        {
            // Ошибку записи лога не маскируем
        }

        string userMessage = $"Произошла критическая ошибка при запуске программы:\n\n" +
                             $"{ex.GetType().Name}: {ex.Message}\n\n" +
                             (string.IsNullOrEmpty(logPath) ? "" : $"Подробный лог сохранен в:\n{logPath}\n\n") +
                             $"Рекомендация: убедитесь, что все библиотеки программы установлены корректно.";

        Console.Error.WriteLine(userMessage);

        if (OperatingSystem.IsWindows())
        {
            MessageBox(IntPtr.Zero, userMessage, errorTitle, 0x10 /* MB_ICONERROR */);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
