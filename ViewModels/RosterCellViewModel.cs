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
        OnPropertyChanged(nameof(CellBorderBrush));
        OnPropertyChanged(nameof(TooltipText));
    }

    public string DisplayText => Model.DisplayText;
    public string TooltipText => Model.TooltipText;

    /// <summary>
    /// Цветовая гамма для темной темы WinUI 3
    /// </summary>
    public string CellBackground
    {
        get
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
    }

    public string CellBorderBrush
    {
        get
        {
            if (IsSelected) return "#2899F5"; // Выделенная ячейка — яркий синий WinUI акцент
            if (IsHoliday) return "#A80000";  // Праздник — красная рамка
            if (Model.IsSubstitute && Model.TotalHours > 0) return "#D89B00"; // Подмена — золотистая рамка
            return "#383838";
        }
    }
}
