using System;
using System.Collections.Generic;
using System.Linq;
using ShiftRoster.Models;

namespace ShiftRoster.Services;

/// <summary>
/// Сервис расчетов графика, автозаполнения и контроля баланса 24 часов на пост.
/// </summary>
public class RosterCalculationService
{
    private static RosterCalculationService? _instance;
    public static RosterCalculationService Instance => _instance ??= new RosterCalculationService();

    /// <summary>
    /// Проверка баланса круглосуточного поста: строго 24 часа в сутки (не больше и не меньше).
    /// </summary>
    public List<DayValidationResult> ValidatePostDailyBalance(IEnumerable<RosterCell> cells, int daysInMonth)
    {
        var results = new List<DayValidationResult>();
        var dayGroups = cells.GroupBy(c => c.DayOfMonth).ToDictionary(g => g.Key, g => g.ToList());

        for (int day = 1; day <= daysInMonth; day++)
        {
            double totalHours = 0;
            if (dayGroups.TryGetValue(day, out var dayCells))
            {
                totalHours = dayCells.Sum(c => c.TotalHours);
            }

            var result = new DayValidationResult
            {
                DayOfMonth = day,
                TotalHours = totalHours
            };

            if (Math.Abs(totalHours - 24.0) < 0.01)
            {
                result.Status = DayBalanceStatus.Exact24Hours;
                result.Message = $"День {day}: Баланс 24 ч соблюден";
            }
            else if (totalHours == 0)
            {
                result.Status = DayBalanceStatus.ZeroHours;
                result.Message = $"День {day}: Дежурство не выставлено (0 ч)";
            }
            else if (totalHours < 24.0)
            {
                result.Status = DayBalanceStatus.Under24Hours;
                result.Message = $"День {day}: Недобор! Выставлено {totalHours:0.#} ч вместо 24 ч (не хватает {24.0 - totalHours:0.#} ч)";
            }
            else
            {
                result.Status = DayBalanceStatus.Over24Hours;
                result.Message = $"День {day}: Перебор! Выставлено {totalHours:0.#} ч вместо 24 ч (лишних {totalHours - 24.0:0.#} ч)";
            }

            results.Add(result);
        }

        return results;
    }

    /// <summary>
    /// Автозаполнение сетки для схемы «Сутки через трое (1/3)»
    /// Смена 24 часа (16 ч день + 8 ч ночь), затем 3 дня выходных.
    /// Итого в любой день месяца дежурит ровно 1 сотрудник = строго 24 часа на пост!
    /// </summary>
    public List<RosterCell> AutoFillOneThree(int scheduleId, List<Employee> employees, int year, int month, int startShiftEmployeeIndex = 0)
    {
        var cells = new List<RosterCell>();
        if (employees.Count == 0) return cells;

        int daysInMonth = DateTime.DaysInMonth(year, month);
        int empCount = employees.Count;

        for (int day = 1; day <= daysInMonth; day++)
        {
            // Определяем, чей сегодня черед дежурить (цикл из empCount сотрудников, обычно 4)
            int activeIndex = (startShiftEmployeeIndex + (day - 1)) % empCount;

            for (int i = 0; i < empCount; i++)
            {
                var emp = employees[i];
                bool isDutyDay = (i == activeIndex);

                cells.Add(new RosterCell
                {
                    ScheduleId = scheduleId,
                    EmployeeId = emp.Id,
                    EmployeeName = emp.FullName,
                    Position = emp.Position,
                    DayOfMonth = day,
                    ShiftCode = isDutyDay ? "С24" : "В",
                    DayHours = isDutyDay ? 16.0 : 0,
                    NightHours = isDutyDay ? 8.0 : 0,
                    TotalHours = isDutyDay ? 24.0 : 0,
                    IsSubstitute = false
                });
            }
        }

        return cells;
    }

    /// <summary>
    /// Автозаполнение сетки для схемы «2 через 2» (по 12 часов: День / Ночь).
    /// В каждые сутки задействовано 2 сотрудника (12ч день + 12ч ночь) = строго 24 часа на пост!
    /// </summary>
    public List<RosterCell> AutoFillTwoTwo(int scheduleId, List<Employee> employees, int year, int month)
    {
        var cells = new List<RosterCell>();
        if (employees.Count < 4) return cells;

        int daysInMonth = DateTime.DaysInMonth(year, month);

        // 4 смены/бригады:
        // Смена 1: Д, Д, В, В
        // Смена 2: В, В, Д, Д
        // Смена 3: Н, Н, В, В
        // Смена 4: В, В, Н, Н
        for (int day = 1; day <= daysInMonth; day++)
        {
            int cycleDay = (day - 1) % 4; // 0, 1, 2, 3

            for (int i = 0; i < employees.Count; i++)
            {
                var emp = employees[i];
                string code = "В";
                double dh = 0, nh = 0, th = 0;

                if (i == 0) // Дневная смена А
                {
                    if (cycleDay is 0 or 1) { code = "Д12"; dh = 12; th = 12; }
                }
                else if (i == 1) // Дневная смена Б
                {
                    if (cycleDay is 2 or 3) { code = "Д12"; dh = 12; th = 12; }
                }
                else if (i == 2) // Ночная смена А
                {
                    if (cycleDay is 0 or 1) { code = "Н12"; nh = 12; th = 12; }
                }
                else if (i == 3) // Ночная смена Б
                {
                    if (cycleDay is 2 or 3) { code = "Н12"; nh = 12; th = 12; }
                }

                cells.Add(new RosterCell
                {
                    ScheduleId = scheduleId,
                    EmployeeId = emp.Id,
                    EmployeeName = emp.FullName,
                    Position = emp.Position,
                    DayOfMonth = day,
                    ShiftCode = code,
                    DayHours = dh,
                    NightHours = nh,
                    TotalHours = th,
                    IsSubstitute = false
                });
            }
        }

        return cells;
    }

    /// <summary>
    /// Назначение подменного сотрудника на период отпуска или болезни.
    /// Позволяет вписать произвольное ФИО подменного без создания карточки!
    /// </summary>
    public void ApplySubstitute(
        List<RosterCell> cells,
        int scheduleId,
        string mainEmployeeName,
        string substituteFullName,
        int startDay,
        int endDay,
        string absenceCode, // "О" (Отпуск) или "Б" (Больничный)
        string substitutePosition = "Оператор (подмена)")
    {
        if (string.IsNullOrWhiteSpace(substituteFullName))
            return;

        substituteFullName = substituteFullName.Trim();

        // Фиксируем имя подменного в истории для подсказок при следующем вводе
        DatabaseService.Instance.RecordSubstituteName(substituteFullName);

        // Находим все ячейки основного сотрудника за указанный период
        var targetCells = cells
            .Where(c => c.EmployeeName.Equals(mainEmployeeName, StringComparison.OrdinalIgnoreCase)
                     && c.DayOfMonth >= startDay && c.DayOfMonth <= endDay)
            .ToList();

        // Проверяем, есть ли уже в списке ячейки для этого подменного сотрудника
        var existingSubCells = cells
            .Where(c => c.EmployeeName.Equals(substituteFullName, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.DayOfMonth);

        int daysInMonth = cells.Max(c => c.DayOfMonth);

        // Если подменного сотрудника еще не было в таблице за этот месяц — инициализируем для него дни
        if (existingSubCells.Count == 0)
        {
            for (int d = 1; d <= daysInMonth; d++)
            {
                var newSubCell = new RosterCell
                {
                    ScheduleId = scheduleId,
                    EmployeeName = substituteFullName,
                    Position = substitutePosition,
                    DayOfMonth = d,
                    ShiftCode = "В",
                    DayHours = 0,
                    NightHours = 0,
                    TotalHours = 0,
                    IsSubstitute = true,
                    SubstitutedForName = mainEmployeeName
                };
                cells.Add(newSubCell);
                existingSubCells[d] = newSubCell;
            }
        }

        foreach (var mainCell in targetCells)
        {
            // Сохраняем часы, которые должен был отработать основной сотрудник
            double dutyDayHours = mainCell.DayHours;
            double dutyNightHours = mainCell.NightHours;
            double dutyTotalHours = mainCell.TotalHours;
            string originalShiftCode = mainCell.ShiftCode;

            // Основному сотруднику ставим статус отсутствия (О или Б) и обнуляем рабочие часы
            mainCell.ShiftCode = absenceCode;
            mainCell.DayHours = 0;
            mainCell.NightHours = 0;
            mainCell.TotalHours = 0;
            mainCell.Notes = $"Замещен: {substituteFullName}";

            // Если в этот день у основного сотрудника была рабочая смена — передаем ее подменному!
            if (dutyTotalHours > 0)
            {
                if (existingSubCells.TryGetValue(mainCell.DayOfMonth, out var subCell))
                {
                    subCell.ShiftCode = originalShiftCode;
                    subCell.DayHours = dutyDayHours;
                    subCell.NightHours = dutyNightHours;
                    subCell.TotalHours = dutyTotalHours;
                    subCell.IsSubstitute = true;
                    subCell.SubstitutedForName = mainEmployeeName;
                    subCell.Notes = $"Подмена за {mainEmployeeName}";
                }
            }
        }
    }

    /// <summary>
    /// Расчет итоговых показателей за месяц по каждому сотруднику.
    /// Включает подсчет праздничных часов по производственному календарю РФ (двойная оплата).
    /// </summary>
    public List<EmployeeMonthSummary> CalculateSummaries(int year, int month, IEnumerable<RosterCell> cells)
    {
        var result = new List<EmployeeMonthSummary>();
        var empGroups = cells.GroupBy(c => c.EmployeeName);
        int order = 1;

        foreach (var group in empGroups.OrderBy(g => g.First().IsSubstitute).ThenBy(g => g.Key))
        {
            var firstCell = group.First();
            var summary = new EmployeeMonthSummary
            {
                OrderNumber = order++,
                EmployeeName = group.Key,
                Position = firstCell.Position,
                IsSubstitute = group.Any(c => c.IsSubstitute),
                SubstitutedForName = group.FirstOrDefault(c => !string.IsNullOrEmpty(c.SubstitutedForName))?.SubstitutedForName
            };

            foreach (var cell in group)
            {
                if (cell.TotalHours > 0)
                {
                    summary.DaysWorked++;
                    summary.TotalHours += cell.TotalHours;
                    summary.DayHours += cell.DayHours;
                    summary.NightHours += cell.NightHours;

                    // Если смена выпала на официальный праздник РФ — учитываем праздничные часы
                    if (ProductionCalendarService.Instance.IsHoliday(year, month, cell.DayOfMonth))
                    {
                        summary.HolidayHours += cell.TotalHours;
                    }
                }

                if (cell.ShiftCode == "О")
                {
                    summary.VacationDays++;
                }
                else if (cell.ShiftCode == "Б")
                {
                    summary.SickDays++;
                }
            }

            result.Add(summary);
        }

        return result;
    }
}
