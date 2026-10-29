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
    /// Автозаполнение сетки для схемы «Сутки через трое (1/3)» с разбивкой 16 часов / 8 часов следующего дня.
    /// Заступление: 16 часов (14ч день + 2ч ночь с 22:00 до 24:00).
    /// Сдача смены на следующий день: 8 часов (2ч день + 6ч ночь с 00:00 до 06:00).
    /// Затем 2 дня отдыха (отсыпной и выходной).
    /// Итого в каждые сутки на посту дежурит 1 сдающий (8ч) + 1 заступающий (16ч) = ровно 24 часа!
    /// Поддерживает сквозную непрерывность графика между месяцами относительно базового месяца (baseYear, baseMonth).
    /// </summary>
    public List<RosterCell> AutoFillOneThree(
        int scheduleId, 
        List<Employee> employees, 
        int year, 
        int month, 
        int? baseYear = null, 
        int? baseMonth = null, 
        int startShiftEmployeeIndex = 0)
    {
        var cells = new List<RosterCell>();
        if (employees.Count == 0) return cells;

        int bYear = baseYear ?? year;
        int bMonth = baseMonth ?? month;
        int daysInBaseMonth = DateTime.DaysInMonth(bYear, bMonth);

        int daysInMonth = DateTime.DaysInMonth(year, month);
        bool hasCustomStartDays = employees.Select(e => e.InitialShiftDay).Distinct().Count() == employees.Count;

        for (int day = 1; day <= daysInMonth; day++)
        {
            var currentDate = new DateTime(year, month, day);

            for (int i = 0; i < employees.Count; i++)
            {
                var emp = employees[i];
                int startDay = hasCustomStartDays ? emp.InitialShiftDay : (startShiftEmployeeIndex + i + 1);
                startDay = Math.Clamp(startDay, 1, daysInBaseMonth);

                var empStartDate = new DateTime(bYear, bMonth, startDay);
                int totalDaysOffset = (currentDate - empStartDate).Days;

                // 0: заступление на смену (16ч)
                // 1: сдача смены утром (8ч)
                // 2, 3: выходные дни
                int cycleDay = ((totalDaysOffset % 4) + 4) % 4;

                string code = "В";
                double dh = 0, nh = 0, th = 0;

                if (cycleDay == 0)
                {
                    code = "16";
                    dh = 14.0;
                    nh = 2.0;
                    th = 16.0;
                }
                else if (cycleDay == 1)
                {
                    code = "8";
                    dh = 2.0;
                    nh = 6.0;
                    th = 8.0;
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
    /// Автозаполнение сетки для схемы «Сутки через двое (1/2)» с учетом стартового дня и непрерывности.
    /// Цикл 3 дня, 3 сотрудника.
    /// В каждые сутки задействовано 2 сотрудника (16ч заступление + 8ч сдача) = строго 24.0 часа на пост!
    /// </summary>
    public List<RosterCell> AutoFillOneTwo(
        int scheduleId,
        List<Employee> employees,
        int year,
        int month,
        int? baseYear = null,
        int? baseMonth = null,
        int startShiftEmployeeIndex = 0)
    {
        var cells = new List<RosterCell>();
        if (employees.Count == 0) return cells;

        int bYear = baseYear ?? year;
        int bMonth = baseMonth ?? month;
        int daysInBaseMonth = DateTime.DaysInMonth(bYear, bMonth);

        int daysInMonth = DateTime.DaysInMonth(year, month);
        bool hasCustomStartDays = employees.Select(e => e.InitialShiftDay).Distinct().Count() == employees.Count;

        for (int day = 1; day <= daysInMonth; day++)
        {
            var currentDate = new DateTime(year, month, day);

            for (int i = 0; i < employees.Count; i++)
            {
                var emp = employees[i];
                int startDay = hasCustomStartDays ? emp.InitialShiftDay : (startShiftEmployeeIndex + i + 1);
                startDay = Math.Clamp(startDay, 1, daysInBaseMonth);

                var empStartDate = new DateTime(bYear, bMonth, startDay);
                int totalDaysOffset = (currentDate - empStartDate).Days;

                // 0: заступление на смену (16ч)
                // 1: сдача смены утром (8ч)
                // 2: выходной день
                int cycleDay = ((totalDaysOffset % 3) + 3) % 3;

                string code = "В";
                double dh = 0, nh = 0, th = 0;

                if (cycleDay == 0)
                {
                    code = "16";
                    dh = 14.0;
                    nh = 2.0;
                    th = 16.0;
                }
                else if (cycleDay == 1)
                {
                    code = "8";
                    dh = 2.0;
                    nh = 6.0;
                    th = 8.0;
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
    /// Автозаполнение сетки для схемы «2 через 2» (по 12 часов: День / Ночь) с учетом стартового дня и непрерывности.
    /// В каждые сутки задействовано 2 сотрудника (12ч день + 12ч ночь) = строго 24 часа на пост!
    /// </summary>
    public List<RosterCell> AutoFillTwoTwo(
        int scheduleId, 
        List<Employee> employees, 
        int year, 
        int month, 
        int? baseYear = null, 
        int? baseMonth = null)
    {
        var cells = new List<RosterCell>();
        if (employees.Count < 4) return cells;

        int bYear = baseYear ?? year;
        int bMonth = baseMonth ?? month;
        int daysInBaseMonth = DateTime.DaysInMonth(bYear, bMonth);

        int daysInMonth = DateTime.DaysInMonth(year, month);

        for (int day = 1; day <= daysInMonth; day++)
        {
            var currentDate = new DateTime(year, month, day);

            for (int i = 0; i < employees.Count; i++)
            {
                var emp = employees[i];
                int startDay = emp.InitialShiftDay > 0 ? emp.InitialShiftDay : ((i % 2 == 0) ? 1 : 3);
                startDay = Math.Clamp(startDay, 1, daysInBaseMonth);

                var empStartDate = new DateTime(bYear, bMonth, startDay);
                int totalDaysOffset = (currentDate - empStartDate).Days;

                string pattern = emp.ShiftPattern ?? "2/2";

                string code = "В";
                double dh = 0, nh = 0, th = 0;

                int cycleDay = ((totalDaysOffset % 4) + 4) % 4;
                if (cycleDay is 0 or 1)
                {
                    if (pattern.Contains("Ночь", StringComparison.OrdinalIgnoreCase) || i >= 2)
                    {
                        code = "Н12";
                        nh = 12.0;
                        th = 12.0;
                    }
                    else
                    {
                        code = "Д12";
                        dh = 12.0;
                        th = 12.0;
                    }
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
    /// Определение плановой смены сотрудника на конкретную календарную дату
    /// согласно схеме графика (1/3 или 2/2) и стартовому дню.
    /// Возвращает код смены, дневные, ночные и общие часы.
    /// </summary>
    public (string ShiftCode, double DayHours, double NightHours, double TotalHours) GetPlannedShiftForDay(
        Employee emp, 
        string pattern, 
        int year, 
        int month, 
        int day, 
        int baseYear, 
        int baseMonth)
    {
        int daysInBase = DateTime.DaysInMonth(baseYear, baseMonth);
        int startDay = Math.Clamp(emp.InitialShiftDay, 1, daysInBase);
        var baseDate = new DateTime(baseYear, baseMonth, startDay);
        var targetDate = new DateTime(year, month, day);

        int totalDaysOffset = (targetDate - baseDate).Days;
        int cycleDay = ((totalDaysOffset % 4) + 4) % 4;

        if (pattern == "1/2")
        {
            int cycleDay12 = ((totalDaysOffset % 3) + 3) % 3;
            if (cycleDay12 == 0)
            {
                return ("16", 14.0, 2.0, 16.0);
            }
            else if (cycleDay12 == 1)
            {
                return ("8", 2.0, 6.0, 8.0);
            }
            return ("В", 0.0, 0.0, 0.0);
        }
        else if (pattern == "2/2")
        {
            if (cycleDay is 0 or 1)
            {
                if (emp.ShiftPattern.Contains("Ночь", StringComparison.OrdinalIgnoreCase) || emp.SortOrder > 2)
                {
                    return ("Н12", 0.0, 12.0, 12.0);
                }
                return ("Д12", 12.0, 0.0, 12.0);
            }
            return ("В", 0.0, 0.0, 0.0);
        }
        else // 1/3 (сутки через трое)
        {
            if (cycleDay == 0)
            {
                return ("16", 14.0, 2.0, 16.0);
            }
            else if (cycleDay == 1)
            {
                return ("8", 2.0, 6.0, 8.0);
            }
            return ("В", 0.0, 0.0, 0.0);
        }
    }

    /// <summary>
    /// Назначение подменного сотрудника на период отпуска или болезни.
    /// Позволяет вписать произвольное ФИО подменного без создания карточки!
    /// Корректно восстанавливает плановые дежурства, даже если ячейки уже были помечены как 'О' или 'Б'!
    /// </summary>
    public void ApplySubstitute(
        List<RosterCell> cells,
        int scheduleId,
        string mainEmployeeName,
        string substituteFullName,
        int startDay,
        int endDay,
        string absenceCode, // "О" (Отпуск) или "Б" (Больничный)
        List<Employee>? postEmployees = null,
        string postPattern = "1/3",
        int year = 0,
        int month = 0,
        int baseYear = 0,
        int baseMonth = 0,
        string substitutePosition = "Оператор (подмена)")
    {
        if (string.IsNullOrWhiteSpace(substituteFullName))
            return;

        substituteFullName = substituteFullName.Trim();
        if (string.IsNullOrWhiteSpace(substitutePosition))
        {
            substitutePosition = "Оператор (подмена)";
        }
        substitutePosition = substitutePosition.Trim();

        // Сохраняем подменного в базе данных с должностью и привязкой к основному сотруднику
        DatabaseService.Instance.SaveSubstitute(substituteFullName, substitutePosition, mainEmployeeName);

        // Находим сотрудника для планового восстановления смен
        var mainEmp = postEmployees?.FirstOrDefault(e => e.FullName.Equals(mainEmployeeName, StringComparison.OrdinalIgnoreCase));

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
        else
        {
            // Обновляем должность для существующих ячеек подменного, если указана
            foreach (var sc in existingSubCells.Values)
            {
                sc.Position = substitutePosition;
            }
        }

        foreach (var mainCell in targetCells)
        {
            // Определяем часы, которые должен был отработать основной сотрудник
            double dutyDayHours = mainCell.DayHours;
            double dutyNightHours = mainCell.NightHours;
            double dutyTotalHours = mainCell.TotalHours;
            string transferShiftCode = mainCell.ShiftCode;

            // Если у основного сотрудника уже стоял отпуск/больничный или 0ч,
            // восстанавливаем его плановое дежурство по схеме подразделения!
            if (dutyTotalHours <= 0 && mainEmp != null && year > 0 && month > 0)
            {
                int bYear = baseYear > 0 ? baseYear : year;
                int bMonth = baseMonth > 0 ? baseMonth : month;
                var planned = GetPlannedShiftForDay(mainEmp, postPattern, year, month, mainCell.DayOfMonth, bYear, bMonth);
                dutyDayHours = planned.DayHours;
                dutyNightHours = planned.NightHours;
                dutyTotalHours = planned.TotalHours;
                transferShiftCode = planned.ShiftCode;
            }

            // Основному сотруднику ставим статус отсутствия (О или Б) и обнуляем рабочие часы
            mainCell.ShiftCode = absenceCode;
            mainCell.DayHours = 0;
            mainCell.NightHours = 0;
            mainCell.TotalHours = 0;
            mainCell.Notes = $"Замещен: {substituteFullName}";

            // Если в этот день была рабочая смена — передаем ее подменному!
            if (dutyTotalHours > 0)
            {
                if (existingSubCells.TryGetValue(mainCell.DayOfMonth, out var subCell))
                {
                    subCell.ShiftCode = transferShiftCode;
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
    /// <summary>
    /// Удаление подменного сотрудника из набора ячеек месяца со снятием отметок отсутствия
    /// и возвратом плановых рабочих смен основному сотруднику.
    /// </summary>
    public void RemoveSubstitute(
        List<RosterCell> cells,
        string substituteFullName,
        bool restoreShiftsToMainEmployee = true,
        List<Employee>? postEmployees = null,
        string postPattern = "1/3",
        int year = 0,
        int month = 0,
        int baseYear = 0,
        int baseMonth = 0)
    {
        if (string.IsNullOrWhiteSpace(substituteFullName)) return;

        string cleanSubName = substituteFullName.Trim();

        // 1. Находим все ячейки этого подменного сотрудника
        var subCells = cells
            .Where(c => c.IsSubstitute && c.EmployeeName.Equals(cleanSubName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (subCells.Count == 0) return;

        // Определяем имя основного сотрудника, за которого выходил подменный
        string? mainEmpName = subCells.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.SubstitutedForName))?.SubstitutedForName;

        if (restoreShiftsToMainEmployee)
        {
            var mainEmp = postEmployees?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(mainEmpName) 
                && e.FullName.Equals(mainEmpName, StringComparison.OrdinalIgnoreCase));

            // Собираем все ячейки основного сотрудника
            var mainCells = !string.IsNullOrWhiteSpace(mainEmpName)
                ? cells.Where(c => !c.IsSubstitute && c.EmployeeName.Equals(mainEmpName, StringComparison.OrdinalIgnoreCase)).ToList()
                : new List<RosterCell>();

            var mainCellsByDay = mainCells.ToDictionary(c => c.DayOfMonth);

            foreach (var subCell in subCells)
            {
                if (mainCellsByDay.TryGetValue(subCell.DayOfMonth, out var mainCell))
                {
                    // Если у подменного в этот день стояла рабочая смена — возвращаем ее основному
                    if (subCell.TotalHours > 0)
                    {
                        mainCell.ShiftCode = subCell.ShiftCode;
                        mainCell.DayHours = subCell.DayHours;
                        mainCell.NightHours = subCell.NightHours;
                        mainCell.TotalHours = subCell.TotalHours;
                        mainCell.Notes = string.Empty;
                    }
                    else if (mainCell.Notes != null && mainCell.Notes.Contains(cleanSubName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Если в этот день смена не передавалась (выходной день цикла),
                        // но основной сотрудник был помечен как 'Б' или 'О' из-за этой подмены — восстанавливаем плановый статус дня
                        if (mainEmp != null && year > 0 && month > 0)
                        {
                            int bYear = baseYear > 0 ? baseYear : year;
                            int bMonth = baseMonth > 0 ? baseMonth : month;
                            var planned = GetPlannedShiftForDay(mainEmp, postPattern, year, month, mainCell.DayOfMonth, bYear, bMonth);
                            mainCell.ShiftCode = planned.ShiftCode;
                            mainCell.DayHours = planned.DayHours;
                            mainCell.NightHours = planned.NightHours;
                            mainCell.TotalHours = planned.TotalHours;
                        }
                        else
                        {
                            mainCell.ShiftCode = "В";
                            mainCell.DayHours = 0;
                            mainCell.NightHours = 0;
                            mainCell.TotalHours = 0;
                        }
                        mainCell.Notes = string.Empty;
                    }
                }
            }
        }

        // 2. Удаляем все ячейки подменного из общего списка
        cells.RemoveAll(c => c.IsSubstitute && c.EmployeeName.Equals(cleanSubName, StringComparison.OrdinalIgnoreCase));
    }

    public List<EmployeeMonthSummary> CalculateSummaries(int year, int month, IEnumerable<RosterCell> cells)
    {
        var result = new List<EmployeeMonthSummary>();
        var cellsList = cells.ToList();

        // Фиксируем порядок первого появления сотрудника в переданном наборе ячеек (без алфавитной сортировки!)
        var firstIndexMap = new Dictionary<string, int>();
        for (int i = 0; i < cellsList.Count; i++)
        {
            string name = cellsList[i].EmployeeName;
            if (!firstIndexMap.ContainsKey(name))
            {
                firstIndexMap[name] = i;
            }
        }

        var empGroups = cellsList.GroupBy(c => c.EmployeeName)
            .OrderBy(g => g.First().IsSubstitute ? 1 : 0)
            .ThenBy(g => firstIndexMap[g.Key]);

        int order = 1;

        foreach (var group in empGroups)
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

    /// <summary>
    /// Подбор штатных сменщиков поста для оперативной подмены коллеги в свой выходной день.
    /// Анализирует плановые графики и оценивает безопасность дежурства.
    /// </summary>
    public List<CoverageCandidate> FindCoverageCandidates(
        List<Employee> postEmployees,
        string postPattern,
        int year,
        int month,
        int targetDay,
        string absentEmployeeName,
        int baseYear,
        int baseMonth)
    {
        var result = new List<CoverageCandidate>();
        int daysInMonth = DateTime.DaysInMonth(year, month);

        foreach (var emp in postEmployees)
        {
            if (emp.FullName.Equals(absentEmployeeName, StringComparison.OrdinalIgnoreCase))
                continue;

            var targetDate = new DateTime(year, month, targetDay);
            var yesterday = targetDate.AddDays(-1);
            var tomorrow = targetDate.AddDays(1);

            var plannedToday = GetPlannedShiftForDay(emp, postPattern, year, month, targetDay, baseYear, baseMonth);
            bool isOffDuty = plannedToday.TotalHours == 0;

            var plannedYesterday = GetPlannedShiftForDay(emp, postPattern, yesterday.Year, yesterday.Month, yesterday.Day, baseYear, baseMonth);
            var plannedTomorrow = GetPlannedShiftForDay(emp, postPattern, tomorrow.Year, tomorrow.Month, tomorrow.Day, baseYear, baseMonth);

            bool workedYesterday;
            bool worksTomorrow;

            if (postPattern == "2/2")
            {
                workedYesterday = plannedYesterday.TotalHours > 0;
                worksTomorrow = plannedTomorrow.TotalHours > 0;
            }
            else // 1/3 или 1/2
            {
                // Для суточных смен критично заступление на 16ч накануне (сдача только сегодня утром)
                // или заступление на 16ч завтра (две смены подряд без отдыха)
                workedYesterday = plannedYesterday.ShiftCode == "16";
                worksTomorrow = plannedTomorrow.ShiftCode == "16";
            }

            string? warning = null;
            if (!isOffDuty)
            {
                warning = "Уже задействован на посту в этот день";
            }
            else if (workedYesterday && worksTomorrow)
            {
                warning = "Заступал вчера и заступает завтра (высокая нагрузка!)";
            }
            else if (workedYesterday)
            {
                warning = "Вчера заступал на смену (сдает смену сегодня утром)";
            }
            else if (worksTomorrow)
            {
                warning = "Заступает на смену завтра (два дежурства подряд)";
            }

            result.Add(new CoverageCandidate
            {
                Employee = emp,
                IsOffDutyOnDay = isOffDuty,
                WorkedYesterday = workedYesterday,
                WorksTomorrow = worksTomorrow,
                WarningText = warning
            });
        }

        return result
            .OrderByDescending(c => c.IsRecommended)
            .ThenBy(c => c.WorkedYesterday || c.WorksTomorrow ? 1 : 0)
            .ThenBy(c => c.FullName)
            .ToList();
    }

    /// <summary>
    /// Неразрушающая сборка табеля месяца путем наложения слоев:
    /// 1. Базовый непрерывный план по схеме (1/3, 2/2 или 1/2)
    /// 2. Наложение кадровых событий (Отпуска и Больничные)
    /// 3. Наложение оперативных замещений (внешние резервисты и коллеги в выходной)
    /// 4. Наложение взаимных обменов сменами
    /// </summary>
    public List<RosterCell> BuildMaterializedSchedule(
        int scheduleId,
        List<Employee> postEmployees,
        string postPattern,
        int year,
        int month,
        int baseYear,
        int baseMonth,
        List<AbsenceEvent> absences,
        List<ShiftSubstitution> substitutions,
        List<ShiftSwap> swaps)
    {
        // Слой 1: Базовый график
        List<RosterCell> cells;
        if (postPattern == "1/2")
        {
            cells = AutoFillOneTwo(scheduleId, postEmployees, year, month, baseYear, baseMonth);
        }
        else if (postPattern == "2/2")
        {
            cells = AutoFillTwoTwo(scheduleId, postEmployees, year, month, baseYear, baseMonth);
        }
        else
        {
            cells = AutoFillOneThree(scheduleId, postEmployees, year, month, baseYear, baseMonth);
        }

        int daysInMonth = DateTime.DaysInMonth(year, month);

        // Слой 2: Наложение кадровых событий (Отпуска и Больничные)
        foreach (var abs in absences)
        {
            for (int d = abs.StartDay; d <= Math.Min(abs.EndDay, daysInMonth); d++)
            {
                var cell = cells.FirstOrDefault(c => !c.IsSubstitute 
                    && c.EmployeeName.Equals(abs.EmployeeName, StringComparison.OrdinalIgnoreCase) 
                    && c.DayOfMonth == d);

                if (cell != null)
                {
                    cell.ShiftCode = abs.AbsenceType; // "О" или "Б"
                    cell.DayHours = 0;
                    cell.NightHours = 0;
                    cell.TotalHours = 0;
                    cell.Notes = abs.TypeLabel;
                }
            }
        }

        // Слой 3: Наложение оперативных замещений (внешние и внутренние)
        var externalSubs = substitutions.Where(s => s.IsExternalSubstitute).ToList();
        var externalSubGroups = externalSubs.GroupBy(s => s.ActualEmployeeName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in externalSubGroups)
        {
            string subName = group.Key;
            string subPos = "Оператор (подмена)";

            // Проверяем, есть ли уже ячейки для этого внешнего подменного
            bool exists = cells.Any(c => c.IsSubstitute && c.EmployeeName.Equals(subName, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                for (int d = 1; d <= daysInMonth; d++)
                {
                    cells.Add(new RosterCell
                    {
                        ScheduleId = scheduleId,
                        EmployeeName = subName,
                        Position = subPos,
                        DayOfMonth = d,
                        ShiftCode = "В",
                        DayHours = 0,
                        NightHours = 0,
                        TotalHours = 0,
                        IsSubstitute = true
                    });
                }
            }

            foreach (var sub in group)
            {
                var subCell = cells.FirstOrDefault(c => c.IsSubstitute 
                    && c.EmployeeName.Equals(subName, StringComparison.OrdinalIgnoreCase) 
                    && c.DayOfMonth == sub.DayOfMonth);

                if (subCell != null)
                {
                    subCell.ShiftCode = sub.ShiftCode;
                    subCell.DayHours = sub.DayHours;
                    subCell.NightHours = sub.NightHours;
                    subCell.TotalHours = sub.TotalHours;
                    subCell.SubstitutedForName = sub.ScheduledEmployeeName;
                    subCell.Notes = $"Подмена за {sub.ScheduledEmployeeName} ({sub.ReasonCode})";
                }
            }
        }

        // Внутренние замещения (коллега поста в свой выходной)
        var internalSubs = substitutions.Where(s => !s.IsExternalSubstitute).ToList();
        foreach (var sub in internalSubs)
        {
            var colleagueCell = cells.FirstOrDefault(c => !c.IsSubstitute 
                && c.EmployeeName.Equals(sub.ActualEmployeeName, StringComparison.OrdinalIgnoreCase) 
                && c.DayOfMonth == sub.DayOfMonth);

            if (colleagueCell != null)
            {
                colleagueCell.ShiftCode = sub.ShiftCode;
                colleagueCell.DayHours = sub.DayHours;
                colleagueCell.NightHours = sub.NightHours;
                colleagueCell.TotalHours = sub.TotalHours;
                colleagueCell.Notes = $"Подмена за {sub.ScheduledEmployeeName} ({sub.ReasonCode})";
            }
        }

        // Слой 4: Наложение взаимных обменов сменами (Shift Swaps)
        foreach (var swap in swaps)
        {
            var cell1_d1 = cells.FirstOrDefault(c => c.EmployeeName.Equals(swap.Employee1Name, StringComparison.OrdinalIgnoreCase) && c.DayOfMonth == swap.Day1);
            var cell2_d1 = cells.FirstOrDefault(c => c.EmployeeName.Equals(swap.Employee2Name, StringComparison.OrdinalIgnoreCase) && c.DayOfMonth == swap.Day1);

            var cell1_d2 = cells.FirstOrDefault(c => c.EmployeeName.Equals(swap.Employee1Name, StringComparison.OrdinalIgnoreCase) && c.DayOfMonth == swap.Day2);
            var cell2_d2 = cells.FirstOrDefault(c => c.EmployeeName.Equals(swap.Employee2Name, StringComparison.OrdinalIgnoreCase) && c.DayOfMonth == swap.Day2);

            // На Day1: меняем местами смены Employee1 и Employee2
            if (cell1_d1 != null && cell2_d1 != null)
            {
                SwapCellValues(cell1_d1, cell2_d1);
                cell1_d1.Notes = $"Обмен с {swap.Employee2Name}";
                cell2_d1.Notes = $"Обмен с {swap.Employee1Name}";
            }

            // На Day2: меняем местами смены Employee1 и Employee2
            if (cell1_d2 != null && cell2_d2 != null)
            {
                SwapCellValues(cell1_d2, cell2_d2);
                cell1_d2.Notes = $"Обмен с {swap.Employee2Name}";
                cell2_d2.Notes = $"Обмен с {swap.Employee1Name}";
            }
        }

        return cells;
    }

    private static void SwapCellValues(RosterCell a, RosterCell b)
    {
        string tmpCode = a.ShiftCode;
        double tmpDh = a.DayHours;
        double tmpNh = a.NightHours;
        double tmpTh = a.TotalHours;

        a.ShiftCode = b.ShiftCode;
        a.DayHours = b.DayHours;
        a.NightHours = b.NightHours;
        a.TotalHours = b.TotalHours;

        b.ShiftCode = tmpCode;
        b.DayHours = tmpDh;
        b.NightHours = tmpNh;
        b.TotalHours = tmpTh;
    }
}
