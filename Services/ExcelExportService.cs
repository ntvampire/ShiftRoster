using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ShiftRoster.Models;

namespace ShiftRoster.Services;

/// <summary>
/// Сервис экспорта графика и табеля в Excel (ClosedXML).
/// Формирует унифицированный бланк Т-13 и сводную ведомость для быстрого ручного ввода в 1С.
/// Поддерживает сводный отчет по нескольким сменам/постам сразу.
/// Табельные номера и колонка «Статус» исключены согласно требованиям ТЗ.
/// Все листы оптимизированы под печать на 1 листе А4 в альбомной ориентации.
/// </summary>
public class ExcelExportService
{
    private static ExcelExportService? _instance;
    public static ExcelExportService Instance => _instance ??= new ExcelExportService();

    /// <summary>
    /// Экспорт одной конкретной смены/поста.
    /// Возвращает фактический путь к сохраненному файлу (с защитой от блокировки файла Excel).
    /// </summary>
    public string ExportRosterToExcel(
        string filePath,
        ShiftSchedule schedule,
        List<RosterCell> cells,
        List<EmployeeMonthSummary> summaries)
    {
        string actualPath = GetAvailableFilePath(filePath);
        using var workbook = new XLWorkbook();

        // 1. Лист «Сводка для оператора 1С» (удобный для ручного переноса)
        Create1CSummarySheet(workbook, schedule, summaries, "Сводка для 1С");

        // 2. Лист «Бланк Т-13 (Печатный табель)» (официальная форма)
        CreateT13Sheet(workbook, schedule, cells, summaries, "Бланк Т-13");

        workbook.SaveAs(actualPath);
        return actualPath;
    }

    /// <summary>
    /// Экспорт общего сводного отчета по всем сменам/постам предприятия сразу.
    /// Возвращает фактический путь к сохраненному файлу.
    /// </summary>
    public string ExportConsolidatedRosterToExcel(
        string filePath,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList)
    {
        string actualPath = GetAvailableFilePath(filePath);
        using var workbook = new XLWorkbook();

        // 1. Главный лист: Общая сводка по всем сменам для внесения в 1С
        CreateConsolidated1CSummarySheet(workbook, year, month, postDataList);

        // 2. Индивидуальные листы по каждой смене
        foreach (var (schedule, cells, summaries) in postDataList)
        {
            string cleanSheetName = GetUniqueSheetName(workbook, schedule.PostName);
            Create1CSummarySheet(workbook, schedule, summaries, cleanSheetName);
        }

        // 3. Сводный бланк Т-13 по всем сменам предприятия
        CreateConsolidatedT13Sheet(workbook, year, month, postDataList);

        workbook.SaveAs(actualPath);
        return actualPath;
    }

    /// <summary>
    /// Создание листа с крупной и наглядной ведомостью по одной смене (без колонки «Статус»)
    /// </summary>
    private void Create1CSummarySheet(XLWorkbook workbook, ShiftSchedule schedule, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);

        string orgName = DatabaseService.Instance.GetOrganizationName();

        // Заголовок документа
        ws.Cell("A1").Value = "СВОДНАЯ ВЕДОМОСТЬ РАБОЧЕГО ВРЕМЕНИ ДЛЯ ВНЕСЕНИЯ В 1С";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 13;
        ws.Range("A1:J1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "________________")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:J2").Merge();

        ws.Cell("A3").Value = $"Подразделение/Пост: {schedule.PostName}  |  Отчетный период: {ShiftSchedule.GetMonthName(schedule.Month)} {schedule.Year} г.";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Range("A3:J3").Merge();

        int startRow = 5;
        WriteSummaryTableHeader(ws, startRow);

        int currentRow = startRow + 1;
        foreach (var s in summaries)
        {
            WriteSummaryRow(ws, currentRow, s);
            currentRow++;
        }

        // Строка ИТОГО
        WriteSummaryTotalRow(ws, startRow + 1, currentRow - 1, currentRow, $"ИТОГО ПО {schedule.PostName.ToUpper()}:");

        var tableRange = ws.Range(startRow, 1, currentRow, 10);
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        SetSummaryColumnWidths(ws);
    }

    /// <summary>
    /// Создание единого листа «Сводка по всем сменам» со всеми сменами и общими суммами
    /// </summary>
    private void CreateConsolidated1CSummarySheet(
        XLWorkbook workbook,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList)
    {
        var ws = workbook.Worksheets.Add("Общая сводка (Все смены)");
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);

        string orgName = DatabaseService.Instance.GetOrganizationName();

        ws.Cell("A1").Value = "ОБЩАЯ СВОДНАЯ ВЕДОМОСТЬ ПО ВСЕМ СМЕНАМ ДЛЯ ВНЕСЕНИЯ В 1С";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Range("A1:J1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "Все подразделения предприятия")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:J2").Merge();

        ws.Cell("A3").Value = $"Отчетный период: {ShiftSchedule.GetMonthName(month)} {year} г.";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Range("A3:J3").Merge();

        int currentRow = 5;
        var subtotalRows = new List<int>();
        int globalOrder = 1;

        foreach (var (schedule, cells, summaries) in postDataList)
        {
            // Разделитель смены
            ws.Cell(currentRow, 1).Value = $"▶ ПОДРАЗДЕЛЕНИЕ / СМЕНА: {schedule.PostName}";
            ws.Cell(currentRow, 1).Style.Font.Bold = true;
            ws.Cell(currentRow, 1).Style.Font.FontSize = 11;
            ws.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D0E1F9");
            ws.Range(currentRow, 1, currentRow, 10).Merge();
            currentRow++;

            int headerRow = currentRow;
            WriteSummaryTableHeader(ws, headerRow);
            currentRow++;

            int dataStartRow = currentRow;
            foreach (var s in summaries)
            {
                var sCopy = new EmployeeMonthSummary
                {
                    OrderNumber = globalOrder++,
                    EmployeeName = s.EmployeeName,
                    Position = s.Position,
                    IsSubstitute = s.IsSubstitute,
                    SubstitutedForName = s.SubstitutedForName,
                    DaysWorked = s.DaysWorked,
                    TotalHours = s.TotalHours,
                    DayHours = s.DayHours,
                    NightHours = s.NightHours,
                    HolidayHours = s.HolidayHours,
                    VacationDays = s.VacationDays,
                    SickDays = s.SickDays
                };
                WriteSummaryRow(ws, currentRow, sCopy);
                currentRow++;
            }

            int subtotalRow = currentRow;
            WriteSummaryTotalRow(ws, dataStartRow, currentRow - 1, subtotalRow, $"Подитог ({schedule.PostName}):");
            if (dataStartRow <= currentRow - 1)
            {
                subtotalRows.Add(subtotalRow);
            }

            var postRange = ws.Range(headerRow, 1, subtotalRow, 10);
            postRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            postRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 2; // отступ перед следующей сменой
        }

        // ОБЩИЙ ИТОГ ПО ВСЕМ СМЕНАМ ПРЕДПРИЯТИЯ
        int grandTotalRow = currentRow;
        ws.Cell(grandTotalRow, 2).Value = "ВСЕГО ПО ПРЕДПРИЯТИЮ (ВСЕ СМЕНЫ):";
        ws.Cell(grandTotalRow, 2).Style.Font.Bold = true;
        ws.Cell(grandTotalRow, 2).Style.Font.FontSize = 11;

        if (subtotalRows.Count > 0)
        {
            string daysFormula = string.Join("+", subtotalRows.Select(r => $"D{r}"));
            string totalHFormula = string.Join("+", subtotalRows.Select(r => $"E{r}"));
            string dayHFormula = string.Join("+", subtotalRows.Select(r => $"F{r}"));
            string nightHFormula = string.Join("+", subtotalRows.Select(r => $"G{r}"));
            string holHFormula = string.Join("+", subtotalRows.Select(r => $"H{r}"));
            string vacFormula = string.Join("+", subtotalRows.Select(r => $"I{r}"));
            string sickFormula = string.Join("+", subtotalRows.Select(r => $"J{r}"));

            ws.Cell(grandTotalRow, 4).FormulaA1 = daysFormula;
            ws.Cell(grandTotalRow, 5).FormulaA1 = totalHFormula;
            ws.Cell(grandTotalRow, 6).FormulaA1 = dayHFormula;
            ws.Cell(grandTotalRow, 7).FormulaA1 = nightHFormula;
            ws.Cell(grandTotalRow, 8).FormulaA1 = holHFormula;
            ws.Cell(grandTotalRow, 9).FormulaA1 = vacFormula;
            ws.Cell(grandTotalRow, 10).FormulaA1 = sickFormula;
        }
        else
        {
            for (int c = 4; c <= 10; c++) ws.Cell(grandTotalRow, c).Value = 0;
        }

        ws.Range(grandTotalRow, 1, grandTotalRow, 10).Style.Font.Bold = true;
        ws.Range(grandTotalRow, 1, grandTotalRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#C2D4EC");
        ws.Range(grandTotalRow, 1, grandTotalRow, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Double;
        for (int c = 4; c <= 10; c++)
        {
            ws.Cell(grandTotalRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        SetSummaryColumnWidths(ws);
    }

    private static void SetSummaryColumnWidths(IXLWorksheet ws)
    {
        ws.Column(1).Width = 6;   // № п/п
        ws.Column(2).Width = 32;  // ФИО сотрудника
        ws.Column(3).Width = 26;  // Должность
        ws.Column(4).Width = 13;  // Отработано дней (D)
        ws.Column(5).Width = 13;  // Всего часов (E)
        ws.Column(6).Width = 13;  // В т.ч. дневных (F)
        ws.Column(7).Width = 13;  // В т.ч. ночных (G)
        ws.Column(8).Width = 16;  // Праздничных (H)
        ws.Column(9).Width = 13;  // Дней отпуска (I)
        ws.Column(10).Width = 13; // Дней больничного (J)
    }

    private static void WriteSummaryTableHeader(IXLWorksheet ws, int row)
    {
        string[] headers = 
        {
            "№ п/п",
            "ФИО сотрудника",
            "Должность",
            "Отработано дней",
            "Всего часов",
            "В т.ч. дневных",
            "В т.ч. ночных",
            "Праздничных (двойная опл.)",
            "Дней отпуска",
            "Дней больничного"
        };

        for (int col = 0; col < headers.Length; col++)
        {
            var cell = ws.Cell(row, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 10;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2B3A4A");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
        ws.Row(row).Height = 28;
    }

    private static void WriteSummaryRow(IXLWorksheet ws, int row, EmployeeMonthSummary s)
    {
        ws.Cell(row, 1).Value = s.OrderNumber;
        ws.Cell(row, 2).Value = s.EmployeeName;
        ws.Cell(row, 3).Value = s.Position;
        ws.Cell(row, 4).Value = s.DaysWorked;
        ws.Cell(row, 5).Value = s.TotalHours;
        ws.Cell(row, 6).Value = s.DayHours;
        ws.Cell(row, 7).Value = s.NightHours;
        ws.Cell(row, 8).Value = s.HolidayHours;
        ws.Cell(row, 9).Value = s.VacationDays;
        ws.Cell(row, 10).Value = s.SickDays;

        if (s.IsSubstitute)
        {
            ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
        }
        else if (row % 2 == 0)
        {
            ws.Range(row, 1, row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
        }

        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 2).Style.Font.Bold = true;
        for (int c = 4; c <= 10; c++)
        {
            ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    private static void WriteSummaryTotalRow(IXLWorksheet ws, int dataStartRow, int dataEndRow, int totalRow, string title)
    {
        ws.Cell(totalRow, 2).Value = title;
        ws.Cell(totalRow, 2).Style.Font.Bold = true;

        if (dataStartRow <= dataEndRow)
        {
            ws.Cell(totalRow, 4).FormulaA1 = $"SUM(D{dataStartRow}:D{dataEndRow})";
            ws.Cell(totalRow, 5).FormulaA1 = $"SUM(E{dataStartRow}:E{dataEndRow})";
            ws.Cell(totalRow, 6).FormulaA1 = $"SUM(F{dataStartRow}:F{dataEndRow})";
            ws.Cell(totalRow, 7).FormulaA1 = $"SUM(G{dataStartRow}:G{dataEndRow})";
            ws.Cell(totalRow, 8).FormulaA1 = $"SUM(H{dataStartRow}:H{dataEndRow})";
            ws.Cell(totalRow, 9).FormulaA1 = $"SUM(I{dataStartRow}:I{dataEndRow})";
            ws.Cell(totalRow, 10).FormulaA1 = $"SUM(J{dataStartRow}:J{dataEndRow})";
        }
        else
        {
            for (int c = 4; c <= 10; c++) ws.Cell(totalRow, c).Value = 0;
        }

        ws.Range(totalRow, 1, totalRow, 10).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
        for (int c = 4; c <= 10; c++)
        {
            ws.Cell(totalRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    /// <summary>
    /// Создание сводного бланка Т-13 по всем сменам сразу (безопасное объединение без дублирования)
    /// </summary>
    private void CreateConsolidatedT13Sheet(
        XLWorkbook workbook,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList)
    {
        var allCells = postDataList.SelectMany(p => p.Cells).ToList();
        var allSummaries = postDataList.SelectMany(p => p.Summaries).ToList();
        var virtualSchedule = new ShiftSchedule
        {
            Year = year,
            Month = month,
            PostName = "Все подразделения предприятия"
        };

        CreateT13Sheet(workbook, virtualSchedule, allCells, allSummaries, "Сводный бланк Т-13");
    }

    /// <summary>
    /// Создание листа с унифицированной формой Т-13 (печать табеля учета рабочего времени).
    /// Полный учет 31-го числа в отдельной выделенной колонке.
    /// Форматирование под 1 лист А4 в альбомной ориентации.
    /// Пробел между ФИО и должностью в скобках.
    /// </summary>
    private void CreateT13Sheet(XLWorkbook workbook, ShiftSchedule schedule, List<RosterCell> cells, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);

        string orgName = DatabaseService.Instance.GetOrganizationName();

        ws.Cell("A1").Value = "Унифицированная форма № Т-13";
        ws.Cell("A1").Style.Font.FontSize = 8;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("A1:V1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "________________")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:V2").Merge();

        ws.Cell("A3").Value = "ТАБЕЛЬ УЧЕТА РАБОЧЕГО ВРЕМЕНИ";
        ws.Cell("A3").Style.Font.Bold = true;
        ws.Cell("A3").Style.Font.FontSize = 12;
        ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A3:V3").Merge();

        int daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        ws.Cell("A4").Value = $"Подразделение/Участок: {schedule.PostName}  |  Период: с 01.{schedule.Month:D2}.{schedule.Year} по {daysInMonth:D2}.{schedule.Month:D2}.{schedule.Year}";
        ws.Cell("A4").Style.Font.FontSize = 9;
        ws.Cell("A4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A4:V4").Merge();

        int startRow = 6;
        ws.Cell(startRow, 1).Value = "№ п/п";
        ws.Range(startRow, 1, startRow + 1, 1).Merge();

        ws.Cell(startRow, 2).Value = "Фамилия, инициалы, должность";
        ws.Range(startRow, 2, startRow + 1, 2).Merge();

        // Колонки дней: 1..15 (верхний ряд), 16..30 (нижний ряд)
        for (int d = 1; d <= 15; d++)
        {
            int colIdx = 2 + d;
            ws.Cell(startRow, colIdx).Value = d.ToString();
            ws.Cell(startRow + 1, colIdx).Value = (d + 15).ToString();
        }

        // Колонка 18: 31-й день месяца (в верхнем ряду ставится прочерк, в нижнем — 31)
        ws.Cell(startRow, 18).Value = "—";
        ws.Cell(startRow + 1, 18).Value = "31";

        // Колонка 19: Суммы за половины месяца
        ws.Cell(startRow, 19).Value = "Итого 1-15";
        ws.Cell(startRow + 1, 19).Value = "Итого 16-31";

        // Колонки 20, 21, 22: Итоги за месяц
        ws.Cell(startRow, 20).Value = "Всего дней";
        ws.Range(startRow, 20, startRow + 1, 20).Merge();

        ws.Cell(startRow, 21).Value = "Всего часов";
        ws.Range(startRow, 21, startRow + 1, 21).Merge();

        ws.Cell(startRow, 22).Value = "В т.ч. ночных";
        ws.Range(startRow, 22, startRow + 1, 22).Merge();

        var headerRange = ws.Range(startRow, 1, startRow + 1, 22);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontSize = 8.5;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E0E0");
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        int currentRow = startRow + 2;

        // Безопасная группировка сотрудников без сбоя при наличии подменных или множественных записей
        var empGroups = cells.GroupBy(c => c.EmployeeName).OrderBy(g => g.Key);
        int order = 1;

        foreach (var group in empGroups)
        {
            var empName = group.Key;
            var firstCell = group.First();
            string position = !string.IsNullOrWhiteSpace(firstCell.Position) ? firstCell.Position : "Оператор";

            // Безопасное формирование словаря ячеек по дням (исключает System.ArgumentException при дублях)
            var dayCellsGroup = group.GroupBy(c => c.DayOfMonth)
                                     .ToDictionary(g => g.Key, g => g.ToList());

            var summary = summaries.FirstOrDefault(s => s.EmployeeName == empName);

            ws.Cell(currentRow, 1).Value = order++;
            ws.Range(currentRow, 1, currentRow + 3, 1).Merge();
            ws.Cell(currentRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // ФИО и должность в скобках С ОБЯЗАТЕЛЬНЫМ ПРОБЕЛОМ: «Иванов И.И. (Оператор)»
            ws.Cell(currentRow, 2).Value = $"{empName} ({position})";
            ws.Range(currentRow, 2, currentRow + 3, 2).Merge();
            ws.Cell(currentRow, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 2).Style.Alignment.WrapText = true;
            ws.Cell(currentRow, 2).Style.Font.Bold = true;
            ws.Cell(currentRow, 2).Style.Font.FontSize = 8.5;

            // --- ПЕРВАЯ ПОЛОВИНА МЕСЯЦА (дни 1..15) ---
            double half1Hours = 0;
            int half1Days = 0;
            for (int d = 1; d <= 15; d++)
            {
                int colIdx = 2 + d;
                if (dayCellsGroup.TryGetValue(d, out var dayList))
                {
                    var primaryCell = dayList.OrderByDescending(c => c.TotalHours).First();
                    double totalH = dayList.Sum(c => c.TotalHours);
                    ws.Cell(currentRow, colIdx).Value = GetT13Code(primaryCell);
                    ws.Cell(currentRow + 1, colIdx).Value = totalH > 0 ? totalH.ToString("0.#") : "";
                    if (totalH > 0)
                    {
                        half1Hours += totalH;
                        half1Days++;
                    }
                }
                ws.Cell(currentRow, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow + 1, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Колонка 18 в первой половине месяца всегда закрыта («X»)
            ws.Cell(currentRow, 18).Value = "X";
            ws.Cell(currentRow + 1, 18).Value = "X";
            ws.Cell(currentRow, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow + 1, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Итог за первую половину месяца (1-15)
            ws.Cell(currentRow, 19).Value = $"{half1Days} дн. / {half1Hours} ч.";
            ws.Range(currentRow, 19, currentRow + 1, 19).Merge();
            ws.Cell(currentRow, 19).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 19).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // --- ВТОРАЯ ПОЛОВИНА МЕСЯЦА (дни 16..30) ---
            double half2Hours = 0;
            int half2Days = 0;
            for (int d = 16; d <= 30; d++)
            {
                int colIdx = 2 + (d - 15);
                if (d <= daysInMonth && dayCellsGroup.TryGetValue(d, out var dayList))
                {
                    var primaryCell = dayList.OrderByDescending(c => c.TotalHours).First();
                    double totalH = dayList.Sum(c => c.TotalHours);
                    ws.Cell(currentRow + 2, colIdx).Value = GetT13Code(primaryCell);
                    ws.Cell(currentRow + 3, colIdx).Value = totalH > 0 ? totalH.ToString("0.#") : "";
                    if (totalH > 0)
                    {
                        half2Hours += totalH;
                        half2Days++;
                    }
                }
                else
                {
                    ws.Cell(currentRow + 2, colIdx).Value = "X";
                    ws.Cell(currentRow + 3, colIdx).Value = "X";
                }
                ws.Cell(currentRow + 2, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow + 3, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // --- 31-Й ДЕНЬ МЕСЯЦА (Колонка 18 во второй половине) ---
            if (daysInMonth >= 31 && dayCellsGroup.TryGetValue(31, out var day31List))
            {
                var primaryCell31 = day31List.OrderByDescending(c => c.TotalHours).First();
                double totalH31 = day31List.Sum(c => c.TotalHours);
                ws.Cell(currentRow + 2, 18).Value = GetT13Code(primaryCell31);
                ws.Cell(currentRow + 3, 18).Value = totalH31 > 0 ? totalH31.ToString("0.#") : "";
                if (totalH31 > 0)
                {
                    half2Hours += totalH31;
                    half2Days++;
                }
            }
            else
            {
                ws.Cell(currentRow + 2, 18).Value = "X";
                ws.Cell(currentRow + 3, 18).Value = "X";
            }
            ws.Cell(currentRow + 2, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow + 3, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Итог за вторую половину месяца (16-31, включая 31-й день!)
            ws.Cell(currentRow + 2, 19).Value = $"{half2Days} дн. / {half2Hours} ч.";
            ws.Range(currentRow + 2, 19, currentRow + 3, 19).Merge();
            ws.Cell(currentRow + 2, 19).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow + 2, 19).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            // --- ИТОГИ ЗА ВЕСЬ МЕСЯЦ (Колонки 20, 21, 22) ---
            double totalMonthDays = summary?.DaysWorked ?? (half1Days + half2Days);
            double totalMonthHours = summary?.TotalHours ?? (half1Hours + half2Hours);
            double totalNightHours = summary?.NightHours ?? group.Sum(c => c.NightHours);

            ws.Cell(currentRow, 20).Value = totalMonthDays;
            ws.Range(currentRow, 20, currentRow + 3, 20).Merge();
            ws.Cell(currentRow, 20).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 20).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 20).Style.Font.Bold = true;

            ws.Cell(currentRow, 21).Value = totalMonthHours;
            ws.Range(currentRow, 21, currentRow + 3, 21).Merge();
            ws.Cell(currentRow, 21).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 21).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 21).Style.Font.Bold = true;

            ws.Cell(currentRow, 22).Value = totalNightHours;
            ws.Range(currentRow, 22, currentRow + 3, 22).Merge();
            ws.Cell(currentRow, 22).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 22).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var empBlock = ws.Range(currentRow, 1, currentRow + 3, 22);
            empBlock.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            empBlock.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 4;
        }

        currentRow += 1;
        ws.Cell(currentRow, 2).Value = "Руководитель подразделения: ________________ / ________________";
        ws.Cell(currentRow + 1, 2).Value = "Работник кадровой службы / Табельщик: ________________ / ________________";
        ws.Cell(currentRow, 2).Style.Font.FontSize = 9;
        ws.Cell(currentRow + 1, 2).Style.Font.FontSize = 9;

        // Оптимальная ширина колонок бланка Т-13 под лист А4
        ws.Column(1).Width = 5;   // № п/п
        ws.Column(2).Width = 26;  // ФИО (должность)
        for (int c = 3; c <= 18; c++)
        {
            ws.Column(c).Width = 4.2; // Дни месяца 1..15, 16..31
        }
        ws.Column(19).Width = 15;  // Итого 1-15 / 16-31
        ws.Column(20).Width = 8;   // Всего дней
        ws.Column(21).Width = 9;   // Всего часов
        ws.Column(22).Width = 9;   // В т.ч. ночных
    }

    /// <summary>
    /// Настройка страницы для гарантированного умещения на 1 листе А4 в альбомной ориентации
    /// </summary>
    private static void ApplyA4LandscapePageSetup(IXLWorksheet ws)
    {
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 1);
        ws.PageSetup.Margins.Left = 0.35;
        ws.PageSetup.Margins.Right = 0.35;
        ws.PageSetup.Margins.Top = 0.4;
        ws.PageSetup.Margins.Bottom = 0.4;
    }

    /// <summary>
    /// Генерация уникального и корректного имени листа Excel (не более 31 символа, без спецсимволов)
    /// </summary>
    private static string GetUniqueSheetName(XLWorkbook workbook, string desiredName)
    {
        string invalid = ":\\/?*[]";
        string clean = new string(desiredName.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (clean.Length > 28) clean = clean.Substring(0, 28);
        if (string.IsNullOrWhiteSpace(clean)) clean = "Смена";

        string unique = clean;
        int counter = 2;
        while (workbook.Worksheets.Any(w => string.Equals(w.Name, unique, StringComparison.OrdinalIgnoreCase)))
        {
            string suffix = $" ({counter})";
            if (clean.Length + suffix.Length > 31)
            {
                unique = clean.Substring(0, 31 - suffix.Length) + suffix;
            }
            else
            {
                unique = clean + suffix;
            }
            counter++;
        }
        return unique;
    }

    /// <summary>
    /// Защита от блокировки файла: если файл открыт в Excel, подбирается свободное имя с таймстемпом или суффиксом
    /// </summary>
    public static string GetAvailableFilePath(string filePath)
    {
        if (!IsFileLocked(filePath))
            return filePath;

        string dir = Path.GetDirectoryName(filePath) ?? "";
        string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath);

        // Попробуем добавить время
        string timePath = Path.Combine(dir, $"{nameWithoutExt}_{DateTime.Now:HH-mm-ss}{ext}");
        if (!IsFileLocked(timePath))
            return timePath;

        int counter = 1;
        while (true)
        {
            string candidate = Path.Combine(dir, $"{nameWithoutExt} ({counter}){ext}");
            if (!IsFileLocked(candidate))
                return candidate;
            counter++;
        }
    }

    private static bool IsFileLocked(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetT13Code(RosterCell cell)
    {
        if (cell.ShiftCode == "О") return "ОТ";
        if (cell.ShiftCode == "Б") return "Б";
        if (cell.ShiftCode == "В") return "В";
        if (cell.TotalHours > 0) return "Я";
        return cell.ShiftCode;
    }
}
