using System;
using Avalonia;
using Avalonia.Styling;

namespace ShiftRoster.Services;

/// <summary>
/// Режим оформления приложения
/// </summary>
public enum AppThemeMode
{
    Auto,
    Dark,
    Light
}

/// <summary>
/// Сервис управления глобальной темой оформления (Авто / Тёмная / Светлая)
/// </summary>
public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    public event Action? ThemeChanged;

    private AppThemeMode _currentMode = AppThemeMode.Auto;
    private bool _isInitialized;

    public AppThemeMode CurrentMode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                ApplyTheme();
                DatabaseService.Instance.SetThemeMode(_currentMode.ToString());
            }
        }
    }

    /// <summary>
    /// Определяет, активна ли сейчас тёмная тема (напрямую или через системную настройку Windows)
    /// </summary>
    public bool IsDarkMode
    {
        get
        {
            if (Application.Current == null) return true;
            return Application.Current.ActualThemeVariant != ThemeVariant.Light;
        }
    }

    public void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        string saved = DatabaseService.Instance.GetThemeMode();
        if (Enum.TryParse<AppThemeMode>(saved, true, out var mode))
        {
            _currentMode = mode;
        }
        else
        {
            _currentMode = AppThemeMode.Auto;
        }

        if (Application.Current != null)
        {
            Application.Current.ActualThemeVariantChanged += (_, _) =>
            {
                ThemeChanged?.Invoke();
            };
        }

        ApplyTheme();
    }

    public void ApplyTheme()
    {
        if (Application.Current == null) return;

        Application.Current.RequestedThemeVariant = _currentMode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        ThemeChanged?.Invoke();
    }
}
