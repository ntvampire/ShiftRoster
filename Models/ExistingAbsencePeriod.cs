namespace ShiftRoster.Models;

/// <summary>
/// Информация о периоде отсутствия сотрудника (отпуск или больничный) в текущем месяце
/// </summary>
public class ExistingAbsencePeriod
{
    public string EmployeeName { get; set; } = string.Empty;
    public string AbsenceCode { get; set; } = "О"; // "О" или "Б"
    public string AbsenceLabel => AbsenceCode == "О" ? "Отпуск" : "Больничный";
    public int StartDay { get; set; }
    public int EndDay { get; set; }
    public int DaysCount => EndDay - StartDay + 1;
    public int ScheduledShiftsCount { get; set; }
    public double ScheduledHours { get; set; }
    public string SubstituteAssignedName { get; set; } = string.Empty;
    public bool HasSubstitute => !string.IsNullOrWhiteSpace(SubstituteAssignedName);

    public string DisplayText =>
        $"{EmployeeName} — {AbsenceLabel} с {StartDay} по {EndDay} число ({DaysCount} дн., смен к замещению: {ScheduledShiftsCount})" 
        + (HasSubstitute ? $" [Подмена: {SubstituteAssignedName}]" : " [Подмена НЕ назначена]");
}
