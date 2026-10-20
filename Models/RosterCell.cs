using CommunityToolkit.Mvvm.ComponentModel;

namespace ShiftRoster.Models;

/// <summary>
/// Ячейка табеля на определенный день месяца для определенного сотрудника.
/// </summary>
public partial class RosterCell : ObservableObject
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int DayOfMonth { get; set; }

    [ObservableProperty]
    private string _shiftCode = "В"; // "С24", "Д12", "Н12", "В", "О", "Б", "8", "РВ"

    [ObservableProperty]
    private double _dayHours = 0;

    [ObservableProperty]
    private double _nightHours = 0;

    [ObservableProperty]
    private double _totalHours = 0;

    [ObservableProperty]
    private bool _isSubstitute = false;

    [ObservableProperty]
    private string? _substitutedForName;

    [ObservableProperty]
    private string? _notes;

    /// <summary>
    /// Текст для отображения в ячейке сетки графика
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (ShiftCode == "О") return "О";
            if (ShiftCode == "Б") return "Б";
            if (ShiftCode == "В") return "В";
            if (TotalHours > 0) return TotalHours.ToString("0.#");
            return ShiftCode;
        }
    }

    /// <summary>
    /// Вспомогательное свойство для всплывающей подсказки
    /// </summary>
    public string TooltipText
    {
        get
        {
            if (IsSubstitute)
            {
                return $"{EmployeeName} (Подмена за {SubstitutedForName}): {TotalHours} ч (День: {DayHours}, Ночь: {NightHours})";
            }
            if (ShiftCode == "О") return $"{EmployeeName}: Отпуск";
            if (ShiftCode == "Б") return $"{EmployeeName}: Больничный";
            if (ShiftCode == "В") return $"{EmployeeName}: Выходной";
            if (TotalHours == 16) return $"{EmployeeName}: Заступление на смену 16 ч (День: {DayHours}, Ночь: {NightHours})";
            if (TotalHours == 8) return $"{EmployeeName}: Сдача смены 8 ч (Ночь: {NightHours}, День: {DayHours})";
            return $"{EmployeeName}: {TotalHours} ч (День: {DayHours}, Ночь: {NightHours})";
        }
    }
}
