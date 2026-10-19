namespace ShiftRoster.Models;

/// <summary>
/// Информация о дне по производственному календарю РФ.
/// Для сменных работников часы не урезаются, но праздники подсвечиваются и учитываются при расчете двойной оплаты.
/// </summary>
public class RussianCalendarDay
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Day { get; set; }
    public bool IsHoliday { get; set; }
    public bool IsPreHoliday { get; set; }
    public bool IsWeekend { get; set; }
    public string Description { get; set; } = string.Empty;
}
