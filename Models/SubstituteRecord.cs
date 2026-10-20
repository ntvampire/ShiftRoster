using System;

namespace ShiftRoster.Models;

/// <summary>
/// Информация о сохраненном подменном сотруднике в базе данных.
/// Хранит ФИО, должность, сотрудника, которого обычно подменяет, и дату последнего использования.
/// </summary>
public class SubstituteRecord
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Position { get; set; } = "Оператор (подмена)";
    public string? DefaultReplacedEmployee { get; set; }
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
}
