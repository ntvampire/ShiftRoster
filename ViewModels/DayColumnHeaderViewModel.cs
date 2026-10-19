using System;
using CommunityToolkit.Mvvm.ComponentModel;
using ShiftRoster.Services;

namespace ShiftRoster.ViewModels;

public class DayColumnHeaderViewModel
{
    public int DayOfMonth { get; }
    public string DayOfWeekName { get; }
    public bool IsWeekend { get; }
    public bool IsHoliday { get; }
    public bool IsPreHoliday { get; }
    public string Tooltip { get; }

    public string HeaderBackground
    {
        get
        {
            if (IsHoliday) return "#5A1E1E";
            if (IsWeekend) return "#1F1F1F";
            return "#2C2C2C";
        }
    }

    public string HeaderBorderBrush => IsHoliday ? "#A80000" : "#3D3D3D";

    public DayColumnHeaderViewModel(int year, int month, int day)
    {
        DayOfMonth = day;
        var dt = new DateTime(year, month, day);
        DayOfWeekName = dt.ToString("ddd");
        IsWeekend = dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        IsHoliday = ProductionCalendarService.Instance.IsHoliday(year, month, day);
        IsPreHoliday = ProductionCalendarService.Instance.IsPreHoliday(year, month, day);

        if (IsHoliday)
        {
            Tooltip = $"{day:D2}.{month:D2}.{year} — {ProductionCalendarService.Instance.GetHolidayDescription(year, month, day)} (Праздник РФ)";
        }
        else if (IsPreHoliday)
        {
            Tooltip = $"{day:D2}.{month:D2}.{year} — Предпраздничный день (для сменных часов не сокращается)";
        }
        else
        {
            Tooltip = $"{day:D2}.{month:D2}.{year} ({DayOfWeekName})";
        }
    }
}

public partial class DayBalanceViewModel : ObservableObject
{
    public int DayOfMonth { get; }

    [ObservableProperty]
    private double _totalHours;

    public DayBalanceViewModel(int day)
    {
        DayOfMonth = day;
    }

    public bool IsExact24 => Math.Abs(TotalHours - 24.0) < 0.01;
    public bool IsZero => TotalHours == 0;
    public bool HasError => !IsExact24 && !IsZero;

    public string DisplayText => TotalHours > 0 ? TotalHours.ToString("0.#") : "-";

    public string BackgroundColor
    {
        get
        {
            if (IsExact24) return "#107C41"; // Зеленый — 24ч закрыто
            if (HasError) return "#C42B1C";  // Красный — ошибка (недобор или перебор)
            return "#333333";
        }
    }

    public string TooltipText
    {
        get
        {
            if (IsExact24) return $"День {DayOfMonth}: Баланс 24 ч соблюден";
            if (TotalHours < 24) return $"День {DayOfMonth}: Недобор! {TotalHours:0.#} ч вместо 24 ч (не хватает {24.0 - TotalHours:0.#} ч)";
            return $"День {DayOfMonth}: Перебор! {TotalHours:0.#} ч вместо 24 ч (лишних {TotalHours - 24.0:0.#} ч)";
        }
    }

    public void UpdateHours(double hours)
    {
        TotalHours = hours;
        OnPropertyChanged(nameof(DisplayText));
        OnPropertyChanged(nameof(IsExact24));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(TooltipText));
    }
}
