using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.ViewModels;

/// <summary>
/// Строка графика для одного сотрудника (штатного или подменного)
/// Табельный номер исключен согласно ТЗ.
/// </summary>
public partial class EmployeeRosterRowViewModel : ObservableObject
{
    public string EmployeeName { get; }
    public string Position { get; }
    public bool IsSubstitute { get; }
    public string? SubstitutedForName { get; }

    public ObservableCollection<RosterCellViewModel> DayCells { get; } = new();

    [ObservableProperty]
    private int _daysWorked;

    [ObservableProperty]
    private double _totalHours;

    [ObservableProperty]
    private double _dayHours;

    [ObservableProperty]
    private double _nightHours;

    [ObservableProperty]
    private double _holidayHours;

    [ObservableProperty]
    private int _vacationDays;

    [ObservableProperty]
    private int _sickDays;

    public string DisplayBadge => IsSubstitute ? $"🔄 Подмена (за {SubstitutedForName})" : "Штатный";
    public string BadgeColor => IsSubstitute ? "#D89B00" : "#505050";

    public EmployeeRosterRowViewModel(string employeeName, string position, bool isSubstitute, string? substitutedForName)
    {
        EmployeeName = employeeName;
        Position = position;
        IsSubstitute = isSubstitute;
        SubstitutedForName = substitutedForName;
    }

    public void RecalculateSummary(int year, int month)
    {
        int days = 0;
        double totalH = 0;
        double dayH = 0;
        double nightH = 0;
        double holH = 0;
        int vac = 0;
        int sick = 0;

        foreach (var cellVm in DayCells)
        {
            var cell = cellVm.Model;
            if (cell.TotalHours > 0)
            {
                days++;
                totalH += cell.TotalHours;
                dayH += cell.DayHours;
                nightH += cell.NightHours;

                if (ProductionCalendarService.Instance.IsHoliday(year, month, cell.DayOfMonth))
                {
                    holH += cell.TotalHours;
                }
            }

            if (cell.ShiftCode == "О") vac++;
            else if (cell.ShiftCode == "Б") sick++;
        }

        DaysWorked = days;
        TotalHours = totalH;
        DayHours = dayH;
        NightHours = nightH;
        HolidayHours = holH;
        VacationDays = vac;
        SickDays = sick;
    }
}
