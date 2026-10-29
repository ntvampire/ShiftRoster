namespace ShiftRoster.Models;

/// <summary>
/// Запись взаимного обмена сменами между двумя штатными сотрудниками («смещение / замена смен»).
/// Например: Сотрудник А освобождает дату 1 и выходит за Сотрудника Б в дату 2.
/// </summary>
public class ShiftSwap
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public string PostName { get; set; } = string.Empty;

    public int Day1 { get; set; }
    public string Employee1Name { get; set; } = string.Empty;

    public int Day2 { get; set; }
    public string Employee2Name { get; set; } = string.Empty;

    public string? Reason { get; set; }
}
