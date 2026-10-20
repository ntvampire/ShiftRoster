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
/// Табельные номера исключены согласно требованиям ТЗ.
/// </summary>
public class ExcelExportService
{
    private static ExcelExportService? _instance;
    public static ExcelExportService Instance => _instance ??= new ExcelExportService();

    /// <summary>
    /// Экспорт одной конкретной смены/поста
    /// </summary>
    public void ExportRosterToExcel(
        string filePath,
        ShiftSchedule schedule,
        List<RosterCell> cells,
        List<EmployeeMonthSummary> summaries)
    {
        using var workbook = new XLWorkbook();

        // 1. Лист «Сводка для оператора 1С» (наиболее удобный для ручного ввода)
        Create1CSummarySheet(workbook, schedule, summaries, "Сводка для 1С");

        // 2. Лист «Бланк Т-13 (Печатный табель)» (официальная форма)
        CreateT13Sheet(workbook, schedule, cells, summaries, "Бланк Т-13");

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Экспорт общего сводного отчета по всем сменам/постам предприятия сразу
    /// </summary>
    public void ExportConsolidatedRosterToExcel(
        string filePath,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList)
    {
        using var workbook = new XLWorkbook();

        // 1. Главный лист: Общая сводка по всем сменам для внесения в 1С
        CreateConsolidated1CSummarySheet(workbook, year, month, postDataList);

        // 2. Индивидуальные листы по каждой смене
        foreach (var (schedule, cells, summaries) in postDataList)
        {
            string cleanSheetName = CleanSheetName(schedule.PostName);
            Create1CSummarySheet(workbook, schedule, summaries, cleanSheetName);
        }

        // 3. Сводный бланк Т-13 по всем сменам предприятия
        CreateConsolidatedT13Sheet(workbook, year, month, postDataList);

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Создание листа с крупной и наглядной ведомостью по одной смене
    /// </summary>
    private void Create1CSummarySheet(XLWorkbook workbook, ShiftSchedule schedule, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;

        string orgName = DatabaseService.Instance.GetOrganizationName();

        // Заголовок документа
        ws.Cell("A1").Value = "СВОДНАЯ ВЕДОМОСТЬ РАБОЧЕГО ВРЕМЕНИ ДЛЯ ВНЕСЕНИЯ В 1С";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Range("A1:K1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "________________")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:K2").Merge();

        ws.Cell("A3").Value = $"Подразделение/Пост: {schedule.PostName}  |  Отчетный период: {ShiftSchedule.GetMonthName(schedule.Month)} {schedule.Year} г.";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Range("A3:K3").Merge();

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

        var tableRange = ws.Range(startRow, 1, currentRow, 11);
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        // Явное задание комфортной ширины столбцов, чтобы ФИО и должности не обрезались
        ws.Column(1).Width = 7;   // № п/п
        ws.Column(2).Width = 34;  // ФИО сотрудника (свободно вмещает полное ФИО)
        ws.Column(3).Width = 28;  // Должность
        ws.Column(4).Width = 16;  // Статус
        ws.Column(5).Width = 14;  // Отработано дней
        ws.Column(6).Width = 14;  // Всего часов
        ws.Column(7).Width = 14;  // В т.ч. дневных
        ws.Column(8).Width = 14;  // В т.ч. ночных
        ws.Column(9).Width = 18;  // Праздничных (двойная опл.)
        ws.Column(10).Width = 14; // Дней отпуска
        ws.Column(11).Width = 15; // Дней больничного
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

        string orgName = DatabaseService.Instance.GetOrganizationName();

        ws.Cell("A1").Value = "ОБЩАЯ СВОДНАЯ ВЕДОМОСТЬ ПО ВСЕМ СМЕНАМ ДЛЯ ВНЕСЕНИЯ В 1С";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 15;
        ws.Range("A1:K1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "Все подразделения предприятия")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 12;
        ws.Range("A2:K2").Merge();

        ws.Cell("A3").Value = $"Отчетный период: {ShiftSchedule.GetMonthName(month)} {year} г.";
        ws.Cell("A3").Style.Font.Italic = true;
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Range("A3:K3").Merge();

        int currentRow = 5;
        var subtotalRows = new List<int>();
        int globalOrder = 1;

        foreach (var (schedule, cells, summaries) in postDataList)
        {
            // Разделитель смены
            ws.Cell(currentRow, 1).Value = $"▶ ПОДРАЗДЕЛЕНИЕ / СМЕНА: {schedule.PostName}";
            ws.Cell(currentRow, 1).Style.Font.Bold = true;
            ws.Cell(currentRow, 1).Style.Font.FontSize = 12;
            ws.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#D0E1F9");
            ws.Range(currentRow, 1, currentRow, 11).Merge();
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
            subtotalRows.Add(subtotalRow);

            var postRange = ws.Range(headerRow, 1, subtotalRow, 11);
            postRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            postRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 2; // отступ перед следующей сменой
        }

        // ОБЩИЙ ИТОГ ПО ВСЕМ СМЕНАМ ПРЕДПРИЯТИЯ
        int grandTotalRow = currentRow;
        ws.Cell(grandTotalRow, 2).Value = "ВСЕГО ПО ПРЕДПРИЯТИЮ (ВСЕ СМЕНЫ):";
        ws.Cell(grandTotalRow, 2).Style.Font.Bold = true;
        ws.Cell(grandTotalRow, 2).Style.Font.FontSize = 12;

        if (subtotalRows.Count > 0)
        {
            string daysFormula = string.Join("+", subtotalRows.Select(r => $"E{r}"));
            string totalHFormula = string.Join("+", subtotalRows.Select(r => $"F{r}"));
            string dayHFormula = string.Join("+", subtotalRows.Select(r => $"G{r}"));
            string nightHFormula = string.Join("+", subtotalRows.Select(r => $"H{r}"));
            string holHFormula = string.Join("+", subtotalRows.Select(r => $"I{r}"));
            string vacFormula = string.Join("+", subtotalRows.Select(r => $"J{r}"));
            string sickFormula = string.Join("+", subtotalRows.Select(r => $"K{r}"));

            ws.Cell(grandTotalRow, 5).FormulaA1 = daysFormula;
            ws.Cell(grandTotalRow, 6).FormulaA1 = totalHFormula;
            ws.Cell(grandTotalRow, 7).FormulaA1 = dayHFormula;
            ws.Cell(grandTotalRow, 8).FormulaA1 = nightHFormula;
            ws.Cell(grandTotalRow, 9).FormulaA1 = holHFormula;
            ws.Cell(grandTotalRow, 10).FormulaA1 = vacFormula;
            ws.Cell(grandTotalRow, 11).FormulaA1 = sickFormula;
        }

        ws.Range(grandTotalRow, 1, grandTotalRow, 11).Style.Font.Bold = true;
        ws.Range(grandTotalRow, 1, grandTotalRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#C2D4EC");
        ws.Range(grandTotalRow, 1, grandTotalRow, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Double;
        for (int c = 5; c <= 11; c++)
        {
            ws.Cell(grandTotalRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Явное задание комфортной ширины столбцов
        ws.Column(1).Width = 7;   // № п/п
        ws.Column(2).Width = 34;  // ФИО сотрудника
        ws.Column(3).Width = 28;  // Должность
        ws.Column(4).Width = 16;  // Статус
        ws.Column(5).Width = 14;  // Отработано дней
        ws.Column(6).Width = 14;  // Всего часов
        ws.Column(7).Width = 14;  // В т.ч. дневных
        ws.Column(8).Width = 14;  // В т.ч. ночных
        ws.Column(9).Width = 18;  // Праздничных (двойная опл.)
        ws.Column(10).Width = 14; // Дней отпуска
        ws.Column(11).Width = 15; // Дней больничного
    }

    private static void WriteSummaryTableHeader(IXLWorksheet ws, int row)
    {
        string[] headers = 
        {
            "№ п/п",
            "ФИО сотрудника",
            "Должность",
            "Статус",
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
        ws.Cell(row, 4).Value = s.StatusText;
        ws.Cell(row, 5).Value = s.DaysWorked;
        ws.Cell(row, 6).Value = s.TotalHours;
        ws.Cell(row, 7).Value = s.DayHours;
        ws.Cell(row, 8).Value = s.NightHours;
        ws.Cell(row, 9).Value = s.HolidayHours;
        ws.Cell(row, 10).Value = s.VacationDays;
        ws.Cell(row, 11).Value = s.SickDays;

        if (s.IsSubstitute)
        {
            ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
        }
        else if (row % 2 == 0)
        {
            ws.Range(row, 1, row, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
        }

        ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(row, 2).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        for (int c = 5; c <= 11; c++)
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
            ws.Cell(totalRow, 5).FormulaA1 = $"SUM(E{dataStartRow}:E{dataEndRow})";
            ws.Cell(totalRow, 6).FormulaA1 = $"SUM(F{dataStartRow}:F{dataEndRow})";
            ws.Cell(totalRow, 7).FormulaA1 = $"SUM(G{dataStartRow}:G{dataEndRow})";
            ws.Cell(totalRow, 8).FormulaA1 = $"SUM(H{dataStartRow}:H{dataEndRow})";
            ws.Cell(totalRow, 9).FormulaA1 = $"SUM(I{dataStartRow}:I{dataEndRow})";
            ws.Cell(totalRow, 10).FormulaA1 = $"SUM(J{dataStartRow}:J{dataEndRow})";
            ws.Cell(totalRow, 11).FormulaA1 = $"SUM(K{dataStartRow}:K{dataEndRow})";
        }
        else
        {
            for (int c = 5; c <= 11; c++) ws.Cell(totalRow, c).Value = 0;
        }

        ws.Range(totalRow, 1, totalRow, 11).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
        for (int c = 5; c <= 11; c++)
        {
            ws.Cell(totalRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
    }

    /// <summary>
    /// Создание сводного бланка Т-13 по всем сменам сразу
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
    /// Создание листа с унифицированной формой Т-13 (печать табеля учета рабочего времени)
    /// </summary>
    private void CreateT13Sheet(XLWorkbook workbook, ShiftSchedule schedule, List<RosterCell> cells, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;

        string orgName = DatabaseService.Instance.GetOrganizationName();

        ws.Cell("A1").Value = "Унифицированная форма № Т-13";
        ws.Cell("A1").Style.Font.FontSize = 8;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("A1:U1").Merge();

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "________________")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:U2").Merge();

        ws.Cell("A3").Value = "ТАБЕЛЬ УЧЕТА РАБОЧЕГО ВРЕМЕНИ";
        ws.Cell("A3").Style.Font.Bold = true;
        ws.Cell("A3").Style.Font.FontSize = 12;
        ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A3:U3").Merge();

        ws.Cell("A4").Value = $"Подразделение/Участок: {schedule.PostName}  |  Период: с 01.{schedule.Month:D2}.{schedule.Year} по {DateTime.DaysInMonth(schedule.Year, schedule.Month):D2}.{schedule.Month:D2}.{schedule.Year}";
        ws.Cell("A4").Style.Font.FontSize = 10;
        ws.Cell("A4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A4:U4").Merge();

        int startRow = 6;
        ws.Cell(startRow, 1).Value = "№ п/п";
        ws.Range(startRow, 1, startRow + 1, 1).Merge();

        ws.Cell(startRow, 2).Value = "Фамилия, инициалы, должность";
        ws.Range(startRow, 2, startRow + 1, 2).Merge();

        for (int d = 1; d <= 15; d++)
        {
            ws.Cell(startRow, 2 + d).Value = d.ToString();
        }
        ws.Cell(startRow, 18).Value = "Дней / Часов (1-15)";
        ws.Cell(startRow + 1, 18).Value = "Дней / Часов (16-31)";

        ws.Cell(startRow, 19).Value = "Всего дней";
        ws.Range(startRow, 19, startRow + 1, 19).Merge();

        ws.Cell(startRow, 20).Value = "Всего часов";
        ws.Range(startRow, 20, startRow + 1, 20).Merge();

        ws.Cell(startRow, 21).Value = "В т.ч. ночных";
        ws.Range(startRow, 21, startRow + 1, 21).Merge();

        var headerRange = ws.Range(startRow, 1, startRow + 1, 21);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontSize = 9;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E0E0");
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        int daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        int currentRow = startRow + 2;

        var empGroups = cells.GroupBy(c => c.EmployeeName).OrderBy(g => g.Key);
        int order = 1;

        foreach (var group in empGroups)
        {
            var empName = group.Key;
            var firstCell = group.First();
            var dayCells = group.ToDictionary(c => c.DayOfMonth);
            var summary = summaries.FirstOrDefault(s => s.EmployeeName == empName);

            ws.Cell(currentRow, 1).Value = order++;
            ws.Range(currentRow, 1, currentRow + 3, 1).Merge();
            ws.Cell(currentRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 2).Value = $"{empName}\n({firstCell.Position})";
            ws.Range(currentRow, 2, currentRow + 3, 2).Merge();
            ws.Cell(currentRow, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 2).Style.Font.Bold = true;
            ws.Cell(currentRow, 2).Style.Font.FontSize = 9;

            double half1Hours = 0;
            int half1Days = 0;
            for (int d = 1; d <= 15; d++)
            {
                if (dayCells.TryGetValue(d, out var c))
                {
                    ws.Cell(currentRow, 2 + d).Value = GetT13Code(c);
                    ws.Cell(currentRow + 1, 2 + d).Value = c.TotalHours > 0 ? c.TotalHours.ToString("0.#") : "";
                    if (c.TotalHours > 0)
                    {
                        half1Hours += c.TotalHours;
                        half1Days++;
                    }
                }
                ws.Cell(currentRow, 2 + d).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow + 1, 2 + d).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            ws.Cell(currentRow, 18).Value = $"{half1Days} дн. / {half1Hours} ч.";
            ws.Cell(currentRow, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            double half2Hours = 0;
            int half2Days = 0;
            for (int d = 16; d <= 31; d++)
            {
                int colIdx = 2 + (d - 15);
                if (d <= daysInMonth && dayCells.TryGetValue(d, out var c))
                {
                    ws.Cell(currentRow + 2, colIdx).Value = GetT13Code(c);
                    ws.Cell(currentRow + 3, colIdx).Value = c.TotalHours > 0 ? c.TotalHours.ToString("0.#") : "";
                    if (c.TotalHours > 0)
                    {
                        half2Hours += c.TotalHours;
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
            ws.Cell(currentRow + 2, 18).Value = $"{half2Days} дн. / {half2Hours} ч.";
            ws.Cell(currentRow + 2, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 19).Value = summary?.DaysWorked ?? (half1Days + half2Days);
            ws.Range(currentRow, 19, currentRow + 3, 19).Merge();
            ws.Cell(currentRow, 19).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 19).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 20).Value = summary?.TotalHours ?? (half1Hours + half2Hours);
            ws.Range(currentRow, 20, currentRow + 3, 20).Merge();
            ws.Cell(currentRow, 20).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 20).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 21).Value = summary?.NightHours ?? 0;
            ws.Range(currentRow, 21, currentRow + 3, 21).Merge();
            ws.Cell(currentRow, 21).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 21).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var empBlock = ws.Range(currentRow, 1, currentRow + 3, 21);
            empBlock.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            empBlock.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 4;
        }

        currentRow += 2;
        ws.Cell(currentRow, 2).Value = "Руководитель подразделения: ________________ / ________________";
        ws.Cell(currentRow + 2, 2).Value = "Работник кадровой службы / Табельщик: ________________ / ________________";

        ws.Columns().AdjustToContents(5, 30);
    }

    private static string CleanSheetName(string name)
    {
        string invalid = ":\\/?*[]";
        string result = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        if (result.Length > 28) result = result.Substring(0, 28);
        return string.IsNullOrWhiteSpace(result) ? "Смена" : result;
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
