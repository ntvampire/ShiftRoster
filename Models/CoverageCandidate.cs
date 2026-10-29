namespace ShiftRoster.Models;

/// <summary>
/// Кандидат из числа штатных сотрудников подразделения для оперативной подмены коллеги в свой выходной.
/// Содержит оценку безопасности дежурства (чтобы не было работы двое суток подряд).
/// </summary>
public class CoverageCandidate
{
    public Employee Employee { get; set; } = null!;
    public string FullName => Employee.FullName;
    public string Position => Employee.Position;

    /// <summary>
    /// Имеет ли плановый выходной в этот день
    /// </summary>
    public bool IsOffDutyOnDay { get; set; } = true;

    /// <summary>
    /// Дежурил ли накануне (заступал на 16ч или 12ч)
    /// </summary>
    public bool WorkedYesterday { get; set; } = false;

    /// <summary>
    /// Заступает ли на дежурство на следующий день
    /// </summary>
    public bool WorksTomorrow { get; set; } = false;

    /// <summary>
    /// Текстовое предупреждение о нарушении режима отдыха
    /// </summary>
    public string? WarningText { get; set; }

    public bool IsRecommended => IsOffDutyOnDay && !WorkedYesterday && !WorksTomorrow;

    public string SafetyBadge => IsRecommended 
        ? "✅ Безопасно (выходной)" 
        : (WorkedYesterday ? "⚠️ Сдает смену утром" : (WorksTomorrow ? "⚠️ Заступает завтра" : "❌ Занят"));

    public string DisplayText => $"{FullName} ({Position}) — {SafetyBadge}";
}
