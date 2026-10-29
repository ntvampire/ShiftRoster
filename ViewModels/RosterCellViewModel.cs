using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.ViewModels;

/// <summary>
/// Представление ячейки табеля в интерфейсе с поддержкой подсветки праздников и стилей WinUI 3
/// </summary>
public partial class RosterCellViewModel : ObservableObject
{
    public RosterCell Model { get; }
    public string EmployeeName => Model.EmployeeName;
    public int DayOfMonth => Model.DayOfMonth;
    public string DayOfWeekName { get; set; } = string.Empty;
    public bool IsWeekend { get; set; }
    public bool IsHoliday { get; set; }
    public string HolidayTooltip { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CellBorderBrush));
        OnPropertyChanged(nameof(CellBackground));
        OnPropertyChanged(nameof(CellForeground));
    }

    public RosterCellViewModel(RosterCell model, int year, int month)
    {
        Model = model;
        var dt = new DateTime(year, month, model.DayOfMonth);
        DayOfWeekName = dt.ToString("ddd");
        IsWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        IsHoliday = ProductionCalendarService.Instance.IsHoliday(year, month, model.DayOfMonth);
        if (IsHoliday)
        {
            HolidayTooltip = ProductionCalendarService.Instance.GetHolidayDescription(year, month, model.DayOfMonth);
        }
    }

    public string ShiftCode
    {
        get => Model.ShiftCode;
        set
        {
            if (Model.ShiftCode != value)
            {
                Model.ShiftCode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(TotalHours));
                OnPropertyChanged(nameof(CellBackground));
                OnPropertyChanged(nameof(CellForeground));
                OnPropertyChanged(nameof(CellBorderBrush));
                OnPropertyChanged(nameof(TooltipText));
            }
        }
    }

    public double TotalHours => Model.TotalHours;
    public double DayHours => Model.DayHours;
    public double NightHours => Model.NightHours;

    public void SetShift(string code, double dayHours, double nightHours)
    {
        Model.ShiftCode = code;
        Model.DayHours = dayHours;
        Model.NightHours = nightHours;
        Model.TotalHours = dayHours + nightHours;

        OnPropertyChanged(nameof(ShiftCode));
        OnPropertyChanged(nameof(TotalHours));
        OnPropertyChanged(nameof(DayHours));
        OnPropertyChanged(nameof(NightHours));
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(CellBackground));
        OnPropertyChanged(nameof(CellForeground));
        OnPropertyChanged(nameof(CellBorderBrush));
        OnPropertyChanged(nameof(TooltipText));
    }

    public string DisplayText => Model.DisplayText;
    public string TooltipText => Model.TooltipText;

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(CellBackground));
        OnPropertyChanged(nameof(CellForeground));
        OnPropertyChanged(nameof(CellBorderBrush));
    }

    /// <summary>
    /// Цветовая гамма фона ячейки с учетом активной темы (Тёмная / Светлая)
    /// </summary>
    public string CellBackground
    {
        get
        {
            bool isDark = ThemeService.Instance.IsDarkMode;
            if (isDark)
            {
                if (IsSelected) return "#004B87"; // Подсветка выделенных ячеек (WinUI Accent dark blue)
                if (Model.ShiftCode == "О") return "#1B4D2E"; // Отпуск — приглушенный зеленый
                if (Model.ShiftCode == "Б") return "#5A3D1A"; // Больничный — приглушенный оранжево-коричневый
                if (Model.TotalHours == 16) return "#1F3B5C"; // Заступление 16ч — глубокий темно-синий
                if (Model.TotalHours == 8 && Model.NightHours > 0) return "#2D2750"; // Сдача смены 8ч (ночные) — фиолетовый
                if (Model.TotalHours == 24) return "#1F3B5C"; // Сутки 24ч
                if (Model.ShiftCode == "Н12") return "#2D2750"; // Ночная смена — темно-фиолетовый
                if (Model.TotalHours > 0) return "#2B4036"; // Дневная смена 12ч / 8ч — хвойно-серый

                // Для выходных
                if (IsHoliday) return "#3D1A1A"; // Праздничный день в календаре — бордовый оттенок
                if (IsWeekend) return "#1F1F1F"; // Суббота/Воскресенье — затемненный фон
                return "#282828"; // Обычный будний день
            }
            else
            {
                // Светлая тема: эргономичные пастельные тона (стандарт Excel/WinUI)
                if (IsSelected) return "#0078D4"; // WinUI яркий акцент
                if (Model.ShiftCode == "О") return "#D4EDDA"; // Отпуск — мягкий мятно-зеленый
                if (Model.ShiftCode == "Б") return "#FFE8D1"; // Больничный — нежный янтарный
                if (Model.TotalHours == 16) return "#E3F2FD"; // Заступление 16ч — пастельный небесно-голубой
                if (Model.TotalHours == 8 && Model.NightHours > 0) return "#EDE7F6"; // Сдача 8ч — нежный лавандовый
                if (Model.TotalHours == 24) return "#E3F2FD"; // Сутки 24ч — небесно-голубой
                if (Model.ShiftCode == "Н12") return "#EDE7F6"; // Ночь — нежный лавандовый
                if (Model.TotalHours > 0) return "#E8F5E9"; // День 12ч — мягкий шалфей

                // Для выходных
                if (IsHoliday) return "#FFEBEE"; // Праздник РФ — нежный коралловый
                if (IsWeekend) return "#F0F0F0"; // Суббота/Воскресенье — мягкий серый
                return "#FFFFFF"; // Обычный будний день
            }
        }
    }

    /// <summary>
    /// Высококонтрастный цвет шрифта для безупречной читаемости в любой теме
    /// </summary>
    public string CellForeground
    {
        get
        {
            bool isDark = ThemeService.Instance.IsDarkMode;
            if (isDark)
            {
                if (IsSelected) return "#FFFFFF";
                if (Model.TotalHours > 0 || Model.ShiftCode == "О" || Model.ShiftCode == "Б") return "#FFFFFF";
                if (IsHoliday) return "#FF9999";
                if (IsWeekend) return "#777777";
                return "#888888";
            }
            else
            {
                // Светлая тема: глубокий насыщенный цвет текста под каждый оттенок
                if (IsSelected) return "#FFFFFF";
                if (Model.ShiftCode == "О") return "#155724"; // Тёмно-зеленый
                if (Model.ShiftCode == "Б") return "#8F3900"; // Тёмно-коричневый
                if (Model.TotalHours == 16 || Model.TotalHours == 24) return "#0D47A1"; // Тёмно-синий
                if (Model.ShiftCode == "Н12" || (Model.TotalHours == 8 && Model.NightHours > 0)) return "#4A148C"; // Тёмно-фиолетовый
                if (Model.TotalHours > 0) return "#1B5E20"; // Тёмно-зеленый
                if (IsHoliday) return "#B71C1C"; // Тёмно-красный
                if (IsWeekend) return "#777777"; // Серый
                return "#555555"; // Нейтральный серый
            }
        }
    }

    /// <summary>
    /// Цвет границы ячейки
    /// </summary>
    public string CellBorderBrush
    {
        get
        {
            bool isDark = ThemeService.Instance.IsDarkMode;
            if (isDark)
            {
                if (IsSelected) return "#2899F5"; // Выделенная ячейка — яркий синий WinUI акцент
                if (IsHoliday) return "#A80000";  // Праздник — красная рамка
                if (Model.IsSubstitute && Model.TotalHours > 0) return "#D89B00"; // Подмена — золотистая рамка
                return "#383838";
            }
            else
            {
                if (IsSelected) return "#005A9E"; // Выделенная ячейка — темный акцентный синий
                if (IsHoliday) return "#E57373";  // Праздник — деликатная красная рамка
                if (Model.IsSubstitute && Model.TotalHours > 0) return "#C67D00"; // Подмена — золотистая рамка
                if (Model.ShiftCode == "О") return "#85D399";
                if (Model.ShiftCode == "Б") return "#FFB74D";
                if (Model.TotalHours == 16 || Model.TotalHours == 24) return "#90CAF9";
                if (Model.ShiftCode == "Н12" || (Model.TotalHours == 8 && Model.NightHours > 0)) return "#B39DDB";
                if (Model.TotalHours > 0) return "#81C784";
                return "#DCDCDC"; // Обычная светлая граница
            }
        }
    }
}
