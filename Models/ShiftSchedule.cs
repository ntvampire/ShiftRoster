using System;

namespace ShiftRoster.Models;

/// <summary>
/// График смен на конкретный месяц и год для определенного поста / подразделения
/// </summary>
public class ShiftSchedule
{
    public int Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PostName { get; set; } = "Пост №1";
    public string PatternType { get; set; } = "1/3"; // "1/3" (сутки/трое), "2/2", "Custom"
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Title => $"{PostName} — {GetMonthName(Month)} {Year}";

    public static string GetMonthName(int month)
    {
        return month switch
        {
            1 => "Январь",
            2 => "Февраль",
            3 => "Март",
            4 => "Апрель",
            5 => "Май",
            6 => "Июнь",
            7 => "Июль",
            8 => "Август",
            9 => "Сентябрь",
            10 => "Октябрь",
            11 => "Ноябрь",
            12 => "Декабрь",
            _ => "Месяц"
        };
    }
}
