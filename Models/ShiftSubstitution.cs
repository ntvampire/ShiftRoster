namespace ShiftRoster.Models;

/// <summary>
/// Запись оперативного замещения смены на определенную дату.
/// Поддерживает как привлечение внешнего подменного, так и выход штатного коллеги в свой выходной.
/// </summary>
public class ShiftSubstitution
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string PostName { get; set; } = string.Empty;
    public int DayOfMonth { get; set; }

    /// <summary>
    /// Штатный сотрудник, за которого осуществляется выход
    /// </summary>
    public string ScheduledEmployeeName { get; set; } = string.Empty;

    /// <summary>
    /// Фактически вышедший сотрудник (внешний подменный или штатный коллега)
    /// </summary>
    public string ActualEmployeeName { get; set; } = string.Empty;

    /// <summary>
    /// true — привлечен человек из внешнего резерва; false — штатный сотрудник поста в свой выходной
    /// </summary>
    public bool IsExternalSubstitute { get; set; } = true;

    /// <summary>
    /// Код смены ("16", "8", "Д12", "Н12")
    /// </summary>
    public string ShiftCode { get; set; } = "16";

    public double DayHours { get; set; }
    public double NightHours { get; set; }
    public double TotalHours { get; set; }

    /// <summary>
    /// Причина замещения ("Б" — болезнь, "О" — отпуск, "Подмена")
    /// </summary>
    public string ReasonCode { get; set; } = "Б";

    public string? Notes { get; set; }
}
