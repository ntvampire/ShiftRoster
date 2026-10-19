namespace ShiftRoster.Models;

/// <summary>
/// Сводные расчетные показатели сотрудника за месяц.
/// Предназначены для листа «Сводка для оператора 1С» и итоговых колонок табеля.
/// Табельный номер исключен согласно ТЗ.
/// </summary>
public class EmployeeMonthSummary
{
    public int OrderNumber { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool IsSubstitute { get; set; }
    public string? SubstitutedForName { get; set; }
    
    public int DaysWorked { get; set; }
    public double TotalHours { get; set; }
    public double DayHours { get; set; }
    public double NightHours { get; set; }
    public double HolidayHours { get; set; } // Праздничные часы по производственному календарю РФ (двойная оплата)
    public int VacationDays { get; set; }
    public int SickDays { get; set; }

    public string StatusText => IsSubstitute 
        ? (string.IsNullOrWhiteSpace(SubstitutedForName) ? "Подменный" : $"Подменный (за {SubstitutedForName})") 
        : "Основной";
}
