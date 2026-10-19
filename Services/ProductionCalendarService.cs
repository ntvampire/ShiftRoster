using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using ShiftRoster.Models;

namespace ShiftRoster.Services;

/// <summary>
/// Сервис производственного календаря РФ.
/// Содержит встроенную базу праздников (офлайн-режим) и механизм автозагрузки из открытых источников.
/// Правило для сменных работников: праздничные дни подсвечиваются и учитываются в двойной оплате,
/// но смена в предпраздничные дни не сокращается.
/// </summary>
public class ProductionCalendarService
{
    private static ProductionCalendarService? _instance;
    public static ProductionCalendarService Instance => _instance ??= new ProductionCalendarService();

    private readonly Dictionary<string, RussianCalendarDay> _cache = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    public ProductionCalendarService()
    {
        // Загружаем праздники для текущего и соседних годов
        int currentYear = DateTime.Now.Year;
        LoadYear(currentYear - 1);
        LoadYear(currentYear);
        LoadYear(currentYear + 1);
        LoadYear(currentYear + 2);
    }

    public void LoadYear(int year)
    {
        // 1. Проверяем БД
        var fromDb = DatabaseService.Instance.GetHolidays(year);
        if (fromDb.Count > 0)
        {
            foreach (var day in fromDb)
            {
                string key = $"{day.Year}-{day.Month:D2}-{day.Day:D2}";
                _cache[key] = day;
            }
            return;
        }

        // 2. Если в БД нет — берем встроенный офлайн-календарь и сохраняем в БД
        var embedded = GenerateEmbeddedHolidays(year);
        DatabaseService.Instance.SaveHolidays(year, embedded);
        foreach (var day in embedded)
        {
            string key = $"{day.Year}-{day.Month:D2}-{day.Day:D2}";
            _cache[key] = day;
        }
    }

    public bool IsHoliday(int year, int month, int day)
    {
        string key = $"{year}-{month:D2}-{day:D2}";
        if (_cache.TryGetValue(key, out var calDay))
        {
            return calDay.IsHoliday;
        }
        return false;
    }

    public bool IsPreHoliday(int year, int month, int day)
    {
        string key = $"{year}-{month:D2}-{day:D2}";
        if (_cache.TryGetValue(key, out var calDay))
        {
            return calDay.IsPreHoliday;
        }
        return false;
    }

    public bool IsWeekend(int year, int month, int day)
    {
        var dt = new DateTime(year, month, day);
        return dt.DayOfWeek == DayOfWeek.Saturday || dt.DayOfWeek == DayOfWeek.Sunday;
    }

    public string GetHolidayDescription(int year, int month, int day)
    {
        string key = $"{year}-{month:D2}-{day:D2}";
        if (_cache.TryGetValue(key, out var calDay))
        {
            return calDay.Description;
        }
        return string.Empty;
    }

    /// <summary>
    /// Автозагрузка / синхронизация календаря из интернета (xmlcalendar.ru)
    /// При отсутствии сети метод завершается без ошибок, оставаясь на встроенном кэше.
    /// </summary>
    public async Task<bool> SyncCalendarFromInternetAsync(int year)
    {
        try
        {
            string url = $"https://xmlcalendar.ru/data/ru/{year}/calendar.json";
            var response = await _httpClient.GetStringAsync(url);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            if (root.TryGetProperty("months", out var monthsElement))
            {
                var fetchedDays = new List<RussianCalendarDay>();

                foreach (var monthObj in monthsElement.EnumerateArray())
                {
                    int month = monthObj.GetProperty("month").GetInt32();
                    string daysStr = monthObj.GetProperty("days").GetString() ?? "";

                    var tokens = daysStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in tokens)
                    {
                        string trimmed = token.Trim();
                        bool isPreHoliday = trimmed.EndsWith("*");
                        bool isHoliday = !isPreHoliday; // в xmlcalendar дни без звездочки в списке days — это праздники/выходные переносы

                        string numStr = trimmed.TrimEnd('*', '+');
                        if (int.TryParse(numStr, out int day))
                        {
                            // Фильтруем: нас интересуют именно нерабочие праздничные дни
                            bool isRealHoliday = IsOfficialRussianHoliday(month, day) || isHoliday;
                            string desc = GetDefaultHolidayName(month, day);

                            fetchedDays.Add(new RussianCalendarDay
                            {
                                Year = year,
                                Month = month,
                                Day = day,
                                IsHoliday = isRealHoliday,
                                IsPreHoliday = isPreHoliday,
                                IsWeekend = new DateTime(year, month, day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                                Description = desc
                            });
                        }
                    }
                }

                if (fetchedDays.Count > 0)
                {
                    DatabaseService.Instance.SaveHolidays(year, fetchedDays);
                    foreach (var day in fetchedDays)
                    {
                        string key = $"{day.Year}-{day.Month:D2}-{day.Day:D2}";
                        _cache[key] = day;
                    }
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Производственный календарь: автономный режим (ошибка сети: {ex.Message})");
        }

        return false;
    }

    /// <summary>
    /// Встроенный офлайн-календарь официальных нерабочих праздничных дней РФ по ст. 112 ТК РФ.
    /// </summary>
    private List<RussianCalendarDay> GenerateEmbeddedHolidays(int year)
    {
        var list = new List<RussianCalendarDay>();

        // 1-8 января — Новогодние каникулы и Рождество Христово
        for (int d = 1; d <= 8; d++)
        {
            list.Add(new RussianCalendarDay
            {
                Year = year,
                Month = 1,
                Day = d,
                IsHoliday = true,
                Description = d == 7 ? "Рождество Христово" : "Новогодние каникулы"
            });
        }

        // 23 февраля — День защитника Отечества
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 2,
            Day = 23,
            IsHoliday = true,
            Description = "День защитника Отечества"
        });

        // 8 марта — Международный женский день
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 3,
            Day = 8,
            IsHoliday = true,
            Description = "Международный женский день"
        });

        // 1 мая — Праздник Весны и Труда
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 5,
            Day = 1,
            IsHoliday = true,
            Description = "Праздник Весны и Труда"
        });

        // 9 мая — День Победы
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 5,
            Day = 9,
            IsHoliday = true,
            Description = "День Победы"
        });

        // 12 июня — День России
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 6,
            Day = 12,
            IsHoliday = true,
            Description = "День России"
        });

        // 4 ноября — День народного единства
        list.Add(new RussianCalendarDay
        {
            Year = year,
            Month = 11,
            Day = 4,
            IsHoliday = true,
            Description = "День народного единства"
        });

        // Предпраздничные дни (сокращенные для 5-дневки, для информации)
        // 22 февраля, 7 марта, 30 апреля, 8 мая, 11 июня, 3 ноября, 31 декабря
        int[] preHolidaysMonth = [2, 3, 4, 5, 6, 11, 12];
        int[] preHolidaysDay = [22, 7, 30, 8, 11, 3, 31];

        for (int i = 0; i < preHolidaysMonth.Length; i++)
        {
            list.Add(new RussianCalendarDay
            {
                Year = year,
                Month = preHolidaysMonth[i],
                Day = preHolidaysDay[i],
                IsPreHoliday = true,
                Description = "Предпраздничный день (-1 ч для 5-дневки)"
            });
        }

        return list;
    }

    private static bool IsOfficialRussianHoliday(int month, int day)
    {
        if (month == 1 && day is >= 1 and <= 8) return true;
        if (month == 2 && day == 23) return true;
        if (month == 3 && day == 8) return true;
        if (month == 5 && day is 1 or 9) return true;
        if (month == 6 && day == 12) return true;
        if (month == 11 && day == 4) return true;
        return false;
    }

    private static string GetDefaultHolidayName(int month, int day)
    {
        if (month == 1 && day == 7) return "Рождество Христово";
        if (month == 1 && day is >= 1 and <= 8) return "Новогодние каникулы";
        if (month == 2 && day == 23) return "День защитника Отечества";
        if (month == 3 && day == 8) return "Международный женский день";
        if (month == 5 && day == 1) return "Праздник Весны и Труда";
        if (month == 5 && day == 9) return "День Победы";
        if (month == 6 && day == 12) return "День России";
        if (month == 11 && day == 4) return "День народного единства";
        return "Праздничный / перенесенный выходной день";
    }
}
