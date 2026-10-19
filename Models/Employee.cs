namespace ShiftRoster.Models;

/// <summary>
/// Сотрудник предприятия (постоянный или подменный)
/// Обратите внимание: табельный номер исключен согласно ТЗ.
/// </summary>
public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = "Оператор";
    public string PostName { get; set; } = "Пост №1";
    public bool IsPermanent { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public string DisplayTitle => $"{FullName} ({Position})";
}
