namespace ShiftRoster.Models;

public enum DayBalanceStatus
{
    Exact24Hours, // Ровно 24 часа в день — норма
    Under24Hours, // Меньше 24 часов — недобор
    Over24Hours,  // Больше 24 часов — перебор
    ZeroHours     // 0 часов (пост не выставлен)
}

/// <summary>
/// Результат проверки суточного баланса круглосуточного поста (строго 24 часа).
/// </summary>
public class DayValidationResult
{
    public int DayOfMonth { get; set; }
    public double TotalHours { get; set; }
    public DayBalanceStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;

    public bool IsValid => Status == DayBalanceStatus.Exact24Hours;
}
