namespace ShiftRoster.Models;

/// <summary>
/// Кадровое событие отсутствия сотрудника (отпуск или больничный лист).
/// Хранится отдельно от базового расписания, обеспечивая неразрушающее наложение.
/// </summary>
public class AbsenceEvent
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string PostName { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int StartDay { get; set; }
    public int EndDay { get; set; }
    public string AbsenceType { get; set; } = "Б"; // "Б" (Больничный) или "О" (Отпуск)
    public string? Notes { get; set; }

    public string TypeLabel => AbsenceType == "О" ? "Отпуск" : "Больничный";
    public int DaysCount => EndDay >= StartDay ? (EndDay - StartDay + 1) : 0;
}
