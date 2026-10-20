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

    /// <summary>
    /// День первой смены в месяце (1..31).
    /// Например: для схемы "сутки через трое" работник №1 начинает 1-го числа, 
    /// работник №2 — 2-го числа, работник №3 — 3-го числа, работник №4 — 4-го числа.
    /// </summary>
    public int InitialShiftDay { get; set; } = 1;

    /// <summary>
    /// Тип графика для сотрудника ("1/3", "2/2-День", "2/2-Ночь")
    /// </summary>
    public string ShiftPattern { get; set; } = "1/3";

    public string DisplayTitle => $"{FullName} ({Position}) — старт {InitialShiftDay}-го числа";
}
