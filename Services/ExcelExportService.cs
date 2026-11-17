using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using ShiftRoster.Models;

namespace ShiftRoster.Services;

/// <summary>
/// Сервис экспорта графика и табеля в Excel (ClosedXML).
/// Формирует унифицированный бланк Т-12 и сводную ведомость для быстрого ручного ввода в 1С.
/// Поддерживает сводный отчет по нескольким сменам/постам сразу.
/// Табельные номера и колонка «Статус» исключены согласно требованиям ТЗ.
/// Все листы оптимизированы под печать на листе А4 в альбомной ориентации.
/// </summary>
public class ExcelExportService
{
    private static ExcelExportService? _instance;
    public static ExcelExportService Instance => _instance ??= new ExcelExportService();

    /// <summary>
    /// Экспорт одной конкретной смены/поста.
    /// Возвращает фактический путь к сохраненному файлу (с защитой от блокировки файла Excel).
    /// Включает строго 3 листа: «График работы», «Бланк Т-12» и «Бланк Т-13».
    /// Автоматически производит ротацию отчетов (не более 25 файлов).
    /// </summary>
    public string ExportRosterToExcel(
        string filePath,
        ShiftSchedule schedule,
        List<RosterCell> cells,
        List<EmployeeMonthSummary> summaries)
    {
        string actualPath = GetAvailableFilePath(filePath);
        using var workbook = new XLWorkbook();

        // 1. Лист «График работы» (рабочий график сотрудников смены)
        CreateWorkScheduleSheet(workbook, schedule, cells, summaries, "График работы");

        // 2. Лист «Бланк Т-12» (официальная форма Т-12)
        CreateT12Sheet(workbook, schedule, cells, summaries, "Бланк Т-12");

        // 3. Лист «Бланк Т-13» (официальная форма Т-13)
        CreateT13Sheet(workbook, schedule, cells, summaries, "Бланк Т-13");

        workbook.SaveAs(actualPath);
        RotateExportFiles(Path.GetDirectoryName(actualPath), 25);
        return actualPath;
    }

    /// <summary>
    /// Экспорт общего сводного отчета по всем сменам/постам предприятия сразу.
    /// Возвращает фактический путь к сохраненному файлу.
    /// Включает строго 3 листа: «График работы», «Бланк Т-12» и «Бланк Т-13».
    /// Автоматически производит ротацию отчетов (не более 25 файлов).
    /// </summary>
    public string ExportConsolidatedRosterToExcel(
        string filePath,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList)
    {
        string actualPath = GetAvailableFilePath(filePath);
        using var workbook = new XLWorkbook();

        // 1. Лист «График работы» (сводный график работы всех работников всех смен)
        CreateConsolidatedWorkScheduleSheet(workbook, year, month, postDataList, "График работы");

        // 2. Лист «Бланк Т-12» (сводный бланк Т-12 по всем сменам предприятия)
        CreateConsolidatedT12Sheet(workbook, year, month, postDataList, "Бланк Т-12");

        // 3. Лист «Бланк Т-13» (сводный бланк Т-13 по всем сменам предприятия)
        CreateConsolidatedT13Sheet(workbook, year, month, postDataList, "Бланк Т-13");

        workbook.SaveAs(actualPath);
        RotateExportFiles(Path.GetDirectoryName(actualPath), 25);
        return actualPath;
    }

    /// <summary>
    /// Создание листа «График работы» по одной смене согласно утвержденному образцу.
    /// Содержит сетку чисел месяца с днями недели, смены/часы сотрудников,
    /// итоговую сумму часов по формуле Excel и подпись составителя.
    /// Форматирование оптимизировано для печати на листе А4 в альбомной ориентации.
    /// </summary>
    private void CreateWorkScheduleSheet(
        XLWorkbook workbook,
        ShiftSchedule schedule,
        List<RosterCell> cells,
        List<EmployeeMonthSummary> summaries,
        string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);
        ws.PageSetup.FitToPages(1, 0);

        string orgName = DatabaseService.Instance.GetOrganizationName();
        int daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        int totalCols = daysInMonth + 3; // 1: №, 2: Ф.И.О., 3..daysInMonth+2: дни, daysInMonth+3: Всего часов

        // Заголовок графика (строка 1)
        string monthName = ShiftSchedule.GetMonthName(schedule.Month).ToLower();
        string postTitle = !string.IsNullOrWhiteSpace(schedule.PostName)
            ? $"смены «{schedule.PostName}»"
            : "диспетчерской службы";
        string titleText = !string.IsNullOrWhiteSpace(orgName)
            ? $"{orgName}\nГрафик работы {postTitle} на {monthName} {schedule.Year} года"
            : $"График работы {postTitle} на {monthName} {schedule.Year} года";

        ws.Cell(1, 1).Value = titleText;
        ws.Range(1, 1, 1, totalCols).Merge();
        ws.Row(1).Height = 36;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 12;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(1, 1).Style.Alignment.WrapText = true;

        // Шапка таблицы (строки 2-3)
        ws.Cell(2, 1).Value = "№";
        ws.Range(2, 1, 3, 1).Merge();

        ws.Cell(2, 2).Value = "Ф.И.О.";
        ws.Range(2, 2, 3, 2).Merge();

        for (int d = 1; d <= daysInMonth; d++)
        {
            int col = 2 + d;
            ws.Cell(2, col).Value = d;

            var dt = new DateTime(schedule.Year, schedule.Month, d);
            string dow = dt.DayOfWeek switch
            {
                DayOfWeek.Monday => "Пн",
                DayOfWeek.Tuesday => "Вт",
                DayOfWeek.Wednesday => "Ср",
                DayOfWeek.Thursday => "Чт",
                DayOfWeek.Friday => "Пт",
                DayOfWeek.Saturday => "Сб",
                DayOfWeek.Sunday => "Вс",
                _ => ""
            };
            ws.Cell(3, col).Value = dow;
        }

        ws.Cell(2, totalCols).Value = "Всего\nчасов";
        ws.Range(2, totalCols, 3, totalCols).Merge();

        // Стилизация шапки (экономия тонера — светло-серый фон #F0F0F0)
        var headerRange = ws.Range(2, 1, 3, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontSize = 10;
        headerRange.Style.Font.FontColor = XLColor.Black;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F0F0F0");
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#777777");
        headerRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#CCCCCC");
        ws.Row(2).Height = 20;
        ws.Row(3).Height = 20;

        // Строки сотрудников
        int currentRow = 4;
        int empIndex = 1;

        foreach (var summary in summaries)
        {
            var empCells = cells
                .Where(c => c.EmployeeName == summary.EmployeeName)
                .ToDictionary(c => c.DayOfMonth);

            ws.Cell(currentRow, 1).Value = empIndex;
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 2).Value = summary.EmployeeName;
            ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(currentRow, 2).Style.Alignment.Indent = 1;

            for (int d = 1; d <= daysInMonth; d++)
            {
                int col = 2 + d;
                var dayCell = ws.Cell(currentRow, col);
                dayCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                if (empCells.TryGetValue(d, out var cell))
                {
                    if (cell.ShiftCode == "О")
                    {
                        dayCell.Value = "О";
                    }
                    else if (cell.ShiftCode == "Б")
                    {
                        dayCell.Value = "Б";
                    }
                    else if (cell.TotalHours > 0)
                    {
                        dayCell.Value = cell.TotalHours;
                    }
                    else if (cell.ShiftCode is not "В" and not "" and not null)
                    {
                        dayCell.Value = cell.ShiftCode;
                    }
                    else
                    {
                        dayCell.Value = "";
                    }
                }
                else
                {
                    dayCell.Value = "";
                }
            }

            // Автосумма часов по строке месяца
            string startColLetter = ws.Cell(currentRow, 3).Address.ColumnLetter;
            string endColLetter = ws.Cell(currentRow, 2 + daysInMonth).Address.ColumnLetter;
            var totalCell = ws.Cell(currentRow, totalCols);
            totalCell.FormulaA1 = $"SUM({startColLetter}{currentRow}:{endColLetter}{currentRow})";
            totalCell.Style.Font.Bold = true;
            totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var rowRange = ws.Range(currentRow, 1, currentRow, totalCols);
            rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            rowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CCCCCC");
            rowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E0E0E0");
            ws.Row(currentRow).Height = 20;

            empIndex++;
            currentRow++;
        }

        // Подвал: Составил
        currentRow += 2;
        ws.Cell(currentRow, 2).Value = "Составил: _____________________________________________ / _____________________________";
        ws.Cell(currentRow, 2).Style.Font.Bold = true;
        ws.Cell(currentRow, 2).Style.Font.FontSize = 10;

        // Ширины колонок
        ws.Column(1).Width = 5;
        ws.Column(2).Width = 24;
        for (int d = 1; d <= daysInMonth; d++)
        {
            ws.Column(2 + d).Width = 4.5;
        }
        ws.Column(totalCols).Width = 11;
    }

    /// <summary>
    /// Создание сводного листа «График работы» по всем сменам предприятия сразу.
    /// Объединяет работников всех подразделений в единую таблицу со сквозной нумерацией
    /// и разделителями смен согласно утвержденному образцу.
    /// Форматирование оптимизировано для печати на листе А4 в альбомной ориентации.
    /// </summary>
    private void CreateConsolidatedWorkScheduleSheet(
        XLWorkbook workbook,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList,
        string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);
        ws.PageSetup.FitToPages(1, 0);

        string orgName = DatabaseService.Instance.GetOrganizationName();
        int daysInMonth = DateTime.DaysInMonth(year, month);
        int totalCols = daysInMonth + 3;

        // Заголовок сводного графика (строка 1)
        string monthName = ShiftSchedule.GetMonthName(month).ToLower();
        string titleText = !string.IsNullOrWhiteSpace(orgName)
            ? $"{orgName}\nСводный график работы всех смен на {monthName} {year} года"
            : $"Сводный график работы всех смен на {monthName} {year} года";

        ws.Cell(1, 1).Value = titleText;
        ws.Range(1, 1, 1, totalCols).Merge();
        ws.Row(1).Height = 36;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 12;
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Cell(1, 1).Style.Alignment.WrapText = true;

        // Шапка таблицы (строки 2-3)
        ws.Cell(2, 1).Value = "№";
        ws.Range(2, 1, 3, 1).Merge();

        ws.Cell(2, 2).Value = "Ф.И.О.";
        ws.Range(2, 2, 3, 2).Merge();

        for (int d = 1; d <= daysInMonth; d++)
        {
            int col = 2 + d;
            ws.Cell(2, col).Value = d;

            var dt = new DateTime(year, month, d);
            string dow = dt.DayOfWeek switch
            {
                DayOfWeek.Monday => "Пн",
                DayOfWeek.Tuesday => "Вт",
                DayOfWeek.Wednesday => "Ср",
                DayOfWeek.Thursday => "Чт",
                DayOfWeek.Friday => "Пт",
                DayOfWeek.Saturday => "Сб",
                DayOfWeek.Sunday => "Вс",
                _ => ""
            };
            ws.Cell(3, col).Value = dow;
        }

        ws.Cell(2, totalCols).Value = "Всего\nчасов";
        ws.Range(2, totalCols, 3, totalCols).Merge();

        // Стилизация шапки (экономия тонера)
        var headerRange = ws.Range(2, 1, 3, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontSize = 10;
        headerRange.Style.Font.FontColor = XLColor.Black;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#F0F0F0");
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#777777");
        headerRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#CCCCCC");
        ws.Row(2).Height = 20;
        ws.Row(3).Height = 20;

        // Строки сотрудников по сменам
        int currentRow = 4;
        int empIndex = 1;

        foreach (var (schedule, cells, summaries) in postDataList)
        {
            // Разделитель смены
            if (postDataList.Count > 1)
            {
                var sectionRange = ws.Range(currentRow, 1, currentRow, totalCols);
                sectionRange.Merge();
                ws.Cell(currentRow, 1).Value = $"Подразделение: {schedule.PostName}";
                sectionRange.Style.Font.Bold = true;
                sectionRange.Style.Font.Italic = true;
                sectionRange.Style.Font.FontSize = 9.5;
                sectionRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EAEAEA");
                sectionRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                sectionRange.Style.Alignment.Indent = 1;
                sectionRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                sectionRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CCCCCC");
                ws.Row(currentRow).Height = 20;
                currentRow++;
            }

            foreach (var summary in summaries)
            {
                var empCells = cells
                    .Where(c => c.EmployeeName == summary.EmployeeName)
                    .ToDictionary(c => c.DayOfMonth);

                ws.Cell(currentRow, 1).Value = empIndex;
                ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                ws.Cell(currentRow, 2).Value = summary.EmployeeName;
                ws.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(currentRow, 2).Style.Alignment.Indent = 1;

                for (int d = 1; d <= daysInMonth; d++)
                {
                    int col = 2 + d;
                    var dayCell = ws.Cell(currentRow, col);
                    dayCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                    if (empCells.TryGetValue(d, out var cell))
                    {
                        if (cell.ShiftCode == "О")
                        {
                            dayCell.Value = "О";
                        }
                        else if (cell.ShiftCode == "Б")
                        {
                            dayCell.Value = "Б";
                        }
                        else if (cell.TotalHours > 0)
                        {
                            dayCell.Value = cell.TotalHours;
                        }
                        else if (cell.ShiftCode is not "В" and not "" and not null)
                        {
                            dayCell.Value = cell.ShiftCode;
                        }
                        else
                        {
                            dayCell.Value = "";
                        }
                    }
                    else
                    {
                        dayCell.Value = "";
                    }
                }

                // Автосумма часов по строке месяца
                string startColLetter = ws.Cell(currentRow, 3).Address.ColumnLetter;
                string endColLetter = ws.Cell(currentRow, 2 + daysInMonth).Address.ColumnLetter;
                var totalCell = ws.Cell(currentRow, totalCols);
                totalCell.FormulaA1 = $"SUM({startColLetter}{currentRow}:{endColLetter}{currentRow})";
                totalCell.Style.Font.Bold = true;
                totalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                var rowRange = ws.Range(currentRow, 1, currentRow, totalCols);
                rowRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                rowRange.Style.Border.OutsideBorderColor = XLColor.FromHtml("#CCCCCC");
                rowRange.Style.Border.InsideBorderColor = XLColor.FromHtml("#E0E0E0");
                ws.Row(currentRow).Height = 20;

                empIndex++;
                currentRow++;
            }
        }

        // Подвал: Составил
        currentRow += 2;
        ws.Cell(currentRow, 2).Value = "Составил: _____________________________________________ / _____________________________";
        ws.Cell(currentRow, 2).Style.Font.Bold = true;
        ws.Cell(currentRow, 2).Style.Font.FontSize = 10;

        // Ширины колонок
        ws.Column(1).Width = 5;
        ws.Column(2).Width = 24;
        for (int d = 1; d <= daysInMonth; d++)
        {
            ws.Column(2 + d).Width = 4.5;
        }
        ws.Column(totalCols).Width = 11;
    }

    /// <summary>
    /// Создание сводного бланка Т-12 по всем сменам сразу (безопасное объединение без дублирования)
    /// </summary>
    private void CreateConsolidatedT12Sheet(
        XLWorkbook workbook,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList,
        string sheetName = "Бланк Т-12")
    {
        var allCells = postDataList.SelectMany(p => p.Cells).ToList();
        var allSummaries = postDataList.SelectMany(p => p.Summaries).ToList();
        var virtualSchedule = new ShiftSchedule
        {
            Year = year,
            Month = month,
            PostName = "Все подразделения предприятия"
        };

        CreateT12Sheet(workbook, virtualSchedule, allCells, allSummaries, sheetName);
    }

    /// <summary>
    /// Создание листа с унифицированной формой Т-12 (печать табеля учета рабочего времени).
    /// Раздел 1 «Учет рабочего времени» согласно Постановлению Госкомстата РФ от 05.01.2004 № 1 (ОКУД 0301007).
    /// Дни месяца 1..15 и 16..31 развернуты по горизонтали с промежуточными итогами.
    /// Форматирование под альбомную печать.
    /// Пробел между ФИО и должностью в скобках.
    /// </summary>
    private void CreateT12Sheet(XLWorkbook workbook, ShiftSchedule schedule, List<RosterCell> cells, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);

        string orgName = DatabaseService.Instance.GetOrganizationName();
        int daysInMonth = DateTime.DaysInMonth(schedule.Year, schedule.Month);
        const int totalCols = 40;

        ws.Cell("A1").Value = "Унифицированная форма № Т-12\nУтверждена Постановлением Госкомстата России от 05.01.2004 № 1\nКод по ОКУД 0301007";
        ws.Cell("A1").Style.Font.FontSize = 8;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell("A1").Style.Alignment.WrapText = true;
        ws.Range(1, 1, 1, totalCols).Merge();
        ws.Row(1).Height = 30;

        ws.Cell("A2").Value = $"Организация: {(!string.IsNullOrWhiteSpace(orgName) ? orgName : "________________")}";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range(2, 1, 2, totalCols).Merge();

        ws.Cell("A3").Value = "ТАБЕЛЬ УЧЕТА РАБОЧЕГО ВРЕМЕНИ И РАСЧЕТА ОПЛАТЫ ТРУДА";
        ws.Cell("A3").Style.Font.Bold = true;
        ws.Cell("A3").Style.Font.FontSize = 12;
        ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(3, 1, 3, totalCols).Merge();

        ws.Cell("A4").Value = "Раздел 1. Учет рабочего времени";
        ws.Cell("A4").Style.Font.Bold = true;
        ws.Cell("A4").Style.Font.FontSize = 10;
        ws.Cell("A4").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(4, 1, 4, totalCols).Merge();

        ws.Cell("A5").Value = $"Подразделение/Участок: {schedule.PostName}  |  Период: с 01.{schedule.Month:D2}.{schedule.Year} по {daysInMonth:D2}.{schedule.Month:D2}.{schedule.Year}";
        ws.Cell("A5").Style.Font.FontSize = 9;
        ws.Cell("A5").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(5, 1, 5, totalCols).Merge();

        int startRow = 6;
        ws.Cell(startRow, 1).Value = "№ п/п";
        ws.Range(startRow, 1, startRow + 1, 1).Merge();

        ws.Cell(startRow, 2).Value = "Фамилия, инициалы, должность";
        ws.Range(startRow, 2, startRow + 1, 2).Merge();

        // Графа 4: Дни 1..15
        ws.Cell(startRow, 3).Value = "Отметки о явках и неявках на работу по числам месяца (1-15)";
        ws.Range(startRow, 3, startRow, 17).Merge();
        for (int d = 1; d <= 15; d++)
        {
            ws.Cell(startRow + 1, 2 + d).Value = d.ToString();
        }

        // Графа 5: Итого за 1-15
        ws.Cell(startRow, 18).Value = "Итого за\n1-15";
        ws.Cell(startRow + 1, 18).Value = "дн. / ч.";

        // Графа 6: Дни 16..31
        ws.Cell(startRow, 19).Value = "Отметки о явках и неявках на работу по числам месяца (16-31)";
        ws.Range(startRow, 19, startRow, 34).Merge();
        for (int d = 16; d <= 31; d++)
        {
            int colIdx = 18 + (d - 15);
            ws.Cell(startRow + 1, colIdx).Value = d.ToString();
        }

        // Графа 7: Итого за 16-31
        ws.Cell(startRow, 35).Value = "Итого за\n16-31";
        ws.Cell(startRow + 1, 35).Value = "дн. / ч.";

        // Графы 8-13: Отработано за месяц
        ws.Cell(startRow, 36).Value = "Отработано за месяц";
        ws.Range(startRow, 36, startRow, 39).Merge();
        ws.Cell(startRow + 1, 36).Value = "Всего дней";
        ws.Cell(startRow + 1, 37).Value = "Всего часов";
        ws.Cell(startRow + 1, 38).Value = "В т.ч. ночных";
        ws.Cell(startRow + 1, 39).Value = "В т.ч. праздн.";

        // Графа 17: Выходных дней
        ws.Cell(startRow, 40).Value = "Выходных\nдней";
        ws.Range(startRow, 40, startRow + 1, 40).Merge();

        var headerRange = ws.Range(startRow, 1, startRow + 1, totalCols);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Font.FontSize = 8.5;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        headerRange.Style.Alignment.WrapText = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0E0E0");
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        int currentRow = startRow + 2;

        // Фиксируем строгий порядок сотрудников согласно ведомости сводки (без алфавитной сортировки!):
        // сначала постоянные работники в порядке внесения в смену, затем подменные по порядку назначения
        var summaryOrderMap = new Dictionary<string, int>();
        int sIdx = 0;
        foreach (var s in summaries)
        {
            if (!summaryOrderMap.ContainsKey(s.EmployeeName))
            {
                summaryOrderMap[s.EmployeeName] = sIdx++;
            }
        }

        var cellOrderMap = new Dictionary<string, int>();
        for (int i = 0; i < cells.Count; i++)
        {
            if (!cellOrderMap.ContainsKey(cells[i].EmployeeName))
            {
                cellOrderMap[cells[i].EmployeeName] = i;
            }
        }

        var empGroups = cells.GroupBy(c => c.EmployeeName)
            .OrderBy(g => summaryOrderMap.TryGetValue(g.Key, out int idx) ? idx : 999)
            .ThenBy(g => g.First().IsSubstitute ? 1 : 0)
            .ThenBy(g => cellOrderMap.TryGetValue(g.Key, out int cIdx) ? cIdx : 999);
        int order = 1;

        foreach (var group in empGroups)
        {
            var empName = group.Key;
            var firstCell = group.First();
            string position = !string.IsNullOrWhiteSpace(firstCell.Position) ? firstCell.Position : "Оператор";

            // Безопасное формирование словаря ячеек по дням (исключает System.ArgumentException при дублях)
            var dayCellsGroup = group.GroupBy(c => c.DayOfMonth)
                                     .ToDictionary(g => g.Key, g => g.ToList());

            var matchingSummaries = summaries.Where(s => s.EmployeeName == empName).ToList();

            // 1. № п/п
            ws.Cell(currentRow, 1).Value = order++;
            ws.Range(currentRow, 1, currentRow + 1, 1).Merge();
            ws.Cell(currentRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // 2. ФИО и должность в скобках С ОБЯЗАТЕЛЬНЫМ ПРОБЕЛОМ: «Иванов И.И. (Оператор)»
            ws.Cell(currentRow, 2).Value = $"{empName} ({position})";
            ws.Range(currentRow, 2, currentRow + 1, 2).Merge();
            ws.Cell(currentRow, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 2).Style.Alignment.WrapText = true;
            ws.Cell(currentRow, 2).Style.Font.Bold = true;
            ws.Cell(currentRow, 2).Style.Font.FontSize = 8.5;

            // 3. Первая половина месяца: дни 1..15 (колонки 3..17)
            double half1Hours = 0;
            int half1Days = 0;
            for (int d = 1; d <= 15; d++)
            {
                int colIdx = 2 + d;
                if (dayCellsGroup.TryGetValue(d, out var dayList))
                {
                    var primaryCell = dayList.OrderByDescending(c => c.TotalHours).First();
                    double totalH = dayList.Sum(c => c.TotalHours);
                    ws.Cell(currentRow, colIdx).Value = GetT12Code(primaryCell);
                    ws.Cell(currentRow + 1, colIdx).Value = totalH > 0 ? totalH.ToString("0.#") : "";
                    if (totalH > 0)
                    {
                        half1Hours += totalH;
                        half1Days++;
                    }
                }
                else
                {
                    ws.Cell(currentRow, colIdx).Value = "В";
                    ws.Cell(currentRow + 1, colIdx).Value = "";
                }
                ws.Cell(currentRow, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow + 1, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // 4. Итого за 1-15 (Колонка 18): верхняя строка — дней, нижняя — часов
            ws.Cell(currentRow, 18).Value = half1Days > 0 ? half1Days.ToString() : "0";
            ws.Cell(currentRow + 1, 18).Value = half1Hours > 0 ? half1Hours.ToString("0.#") : "0";
            ws.Cell(currentRow, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow + 1, 18).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 18).Style.Font.Bold = true;
            ws.Cell(currentRow + 1, 18).Style.Font.Bold = true;

            // 5. Вторая половина месяца: дни 16..31 (колонки 19..34)
            double half2Hours = 0;
            int half2Days = 0;
            for (int d = 16; d <= 31; d++)
            {
                int colIdx = 18 + (d - 15);
                if (d <= daysInMonth)
                {
                    if (dayCellsGroup.TryGetValue(d, out var dayList))
                    {
                        var primaryCell = dayList.OrderByDescending(c => c.TotalHours).First();
                        double totalH = dayList.Sum(c => c.TotalHours);
                        ws.Cell(currentRow, colIdx).Value = GetT12Code(primaryCell);
                        ws.Cell(currentRow + 1, colIdx).Value = totalH > 0 ? totalH.ToString("0.#") : "";
                        if (totalH > 0)
                        {
                            half2Hours += totalH;
                            half2Days++;
                        }
                    }
                    else
                    {
                        ws.Cell(currentRow, colIdx).Value = "В";
                        ws.Cell(currentRow + 1, colIdx).Value = "";
                    }
                }
                else
                {
                    ws.Cell(currentRow, colIdx).Value = "X";
                    ws.Cell(currentRow + 1, colIdx).Value = "X";
                }
                ws.Cell(currentRow, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(currentRow + 1, colIdx).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // 6. Итого за 16-31 (Колонка 35): верхняя строка — дней, нижняя — часов
            ws.Cell(currentRow, 35).Value = half2Days > 0 ? half2Days.ToString() : "0";
            ws.Cell(currentRow + 1, 35).Value = half2Hours > 0 ? half2Hours.ToString("0.#") : "0";
            ws.Cell(currentRow, 35).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow + 1, 35).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 35).Style.Font.Bold = true;
            ws.Cell(currentRow + 1, 35).Style.Font.Bold = true;

            // 7. Итоги за весь месяц (Колонки 36..40)
            double totalMonthDays = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.DaysWorked)
                : (half1Days + half2Days);
            double totalMonthHours = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.TotalHours)
                : (half1Hours + half2Hours);
            double totalNightHours = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.NightHours)
                : group.Sum(c => c.NightHours);
            double totalHolidayHours = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.HolidayHours)
                : 0;
            int daysOffCount = daysInMonth - (int)totalMonthDays;
            if (daysOffCount < 0) daysOffCount = 0;

            // Колонка 36: Всего дней
            ws.Cell(currentRow, 36).Value = totalMonthDays;
            ws.Range(currentRow, 36, currentRow + 1, 36).Merge();
            ws.Cell(currentRow, 36).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 36).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 36).Style.Font.Bold = true;

            // Колонка 37: Всего часов
            ws.Cell(currentRow, 37).Value = totalMonthHours;
            ws.Range(currentRow, 37, currentRow + 1, 37).Merge();
            ws.Cell(currentRow, 37).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 37).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 37).Style.Font.Bold = true;

            // Колонка 38: В т.ч. ночных
            ws.Cell(currentRow, 38).Value = totalNightHours;
            ws.Range(currentRow, 38, currentRow + 1, 38).Merge();
            ws.Cell(currentRow, 38).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 38).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Колонка 39: В т.ч. праздничных
            ws.Cell(currentRow, 39).Value = totalHolidayHours;
            ws.Range(currentRow, 39, currentRow + 1, 39).Merge();
            ws.Cell(currentRow, 39).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 39).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Колонка 40: Выходных дней
            ws.Cell(currentRow, 40).Value = daysOffCount;
            ws.Range(currentRow, 40, currentRow + 1, 40).Merge();
            ws.Cell(currentRow, 40).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 40).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            var empBlock = ws.Range(currentRow, 1, currentRow + 1, totalCols);
            empBlock.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            empBlock.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 2;
        }

        currentRow += 1;
        ws.Cell(currentRow, 2).Value = "Руководитель структурного подразделения: ________________ / ________________";
        ws.Cell(currentRow + 1, 2).Value = "Работник кадровой службы / Табельщик: ________________ / ________________";
        ws.Cell(currentRow, 2).Style.Font.FontSize = 9;
        ws.Cell(currentRow + 1, 2).Style.Font.FontSize = 9;

        // Оптимальная ширина колонок бланка Т-12 под печать
        ws.Column(1).Width = 4.5;  // № п/п
        ws.Column(2).Width = 24;   // ФИО (должность)
        for (int c = 3; c <= 17; c++)
        {
            ws.Column(c).Width = 3.5; // Дни 1..15
        }
        ws.Column(18).Width = 7.5; // Итого 1-15
        for (int c = 19; c <= 34; c++)
        {
            ws.Column(c).Width = 3.5; // Дни 16..31
        }
        ws.Column(35).Width = 7.5; // Итого 16-31
        ws.Column(36).Width = 7.0; // Всего дней
        ws.Column(37).Width = 8.0; // Всего часов
        ws.Column(38).Width = 7.5; // В т.ч. ночных
        ws.Column(39).Width = 7.5; // В т.ч. праздничных
        ws.Column(40).Width = 7.0; // Выходных дней
    }

    /// <summary>
    /// Создание сводного бланка Т-13 по всем сменам сразу (безопасное объединение без дублирования)
    /// </summary>
    private void CreateConsolidatedT13Sheet(
        XLWorkbook workbook,
        int year,
        int month,
        List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)> postDataList,
        string sheetName = "Бланк Т-13")
    {
        var allCells = postDataList.SelectMany(p => p.Cells).ToList();
        var allSummaries = postDataList.SelectMany(p => p.Summaries).ToList();
        var virtualSchedule = new ShiftSchedule
        {
            Year = year,
            Month = month,
            PostName = "Все подразделения предприятия"
        };

        CreateT13Sheet(workbook, virtualSchedule, allCells, allSummaries, sheetName);
    }

    /// <summary>
    /// Создание листа с унифицированной формой Т-13 (печать табеля учета рабочего времени).
    /// Полный учет 31-го числа в отдельной выделенной колонке.
    /// Форматирование под лист А4 в альбомной ориентации.
    /// Пробел между ФИО и должностью в скобках.
    /// </summary>
    private void CreateT13Sheet(XLWorkbook workbook, ShiftSchedule schedule, List<RosterCell> cells, List<EmployeeMonthSummary> summaries, string sheetName)
    {
        var ws = workbook.Worksheets.Add(sheetName);
        ws.ShowGridLines = true;
        ApplyA4LandscapePageSetup(ws);

        string orgName = DatabaseService.Instance.GetOrganizationName();

        ws.Cell("A1").Value = "Унифицированная форма № Т-13\nУтверждена Постановлением Госкомстата России от 05.01.2004 № 1\nКод по ОКУД 0301008";
        ws.Cell("A1").Style.Font.FontSize = 8;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell("A1").Style.Alignment.WrapText = true;
        ws.Range("A1:V1").Merge();
        ws.Row(1).Height = 30;

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

        var summaryOrderMap = new Dictionary<string, int>();
        int sIdx = 0;
        foreach (var s in summaries)
        {
            if (!summaryOrderMap.ContainsKey(s.EmployeeName))
            {
                summaryOrderMap[s.EmployeeName] = sIdx++;
            }
        }

        var cellOrderMap = new Dictionary<string, int>();
        for (int i = 0; i < cells.Count; i++)
        {
            if (!cellOrderMap.ContainsKey(cells[i].EmployeeName))
            {
                cellOrderMap[cells[i].EmployeeName] = i;
            }
        }

        var empGroups = cells.GroupBy(c => c.EmployeeName)
            .OrderBy(g => summaryOrderMap.TryGetValue(g.Key, out int idx) ? idx : 999)
            .ThenBy(g => g.First().IsSubstitute ? 1 : 0)
            .ThenBy(g => cellOrderMap.TryGetValue(g.Key, out int cIdx) ? cIdx : 999);
        int order = 1;

        foreach (var group in empGroups)
        {
            var empName = group.Key;
            var firstCell = group.First();
            string position = !string.IsNullOrWhiteSpace(firstCell.Position) ? firstCell.Position : "Оператор";

            var dayCellsGroup = group.GroupBy(c => c.DayOfMonth)
                                     .ToDictionary(g => g.Key, g => g.ToList());

            var matchingSummaries = summaries.Where(s => s.EmployeeName == empName).ToList();

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
                    ws.Cell(currentRow, colIdx).Value = GetT12Code(primaryCell);
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
                    ws.Cell(currentRow + 2, colIdx).Value = GetT12Code(primaryCell);
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
                ws.Cell(currentRow + 2, 18).Value = GetT12Code(primaryCell31);
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
            double totalMonthDays = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.DaysWorked)
                : (half1Days + half2Days);
            double totalMonthHours = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.TotalHours)
                : (half1Hours + half2Hours);
            double totalNightHours = matchingSummaries.Count > 0
                ? matchingSummaries.Sum(s => s.NightHours)
                : group.Sum(c => c.NightHours);

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
        ws.Cell(currentRow, 2).Value = "Руководитель структурного подразделения: ________________ / ________________";
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

    private static string GetT12Code(RosterCell cell)
    {
        if (cell.ShiftCode == "О") return "ОТ";
        if (cell.ShiftCode == "Б") return "Б";
        if (cell.ShiftCode == "В") return "В";
        if (cell.TotalHours > 0) return "Я";
        return cell.ShiftCode;
    }

    private static string GetT13Code(RosterCell cell) => GetT12Code(cell);

    /// <summary>
    /// Получение списка всех сгенерированных отчетов Excel в папке экспорта (от новых к старым).
    /// </summary>
    public static List<FileInfo> GetExportFiles(string? directory = null)
    {
        string dir = !string.IsNullOrWhiteSpace(directory)
            ? directory
            : DatabaseService.Instance.ExportsDirectory;

        if (!Directory.Exists(dir))
            return new List<FileInfo>();

        return Directory.GetFiles(dir, "*.xlsx")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    /// <summary>
    /// Ограничение количества файлов отчетов в папке экспорта (максимум maxFiles, по умолчанию 25).
    /// Старые файлы автоматически удаляются.
    /// </summary>
    public static void RotateExportFiles(string? directory = null, int maxFiles = 25)
    {
        try
        {
            var files = GetExportFiles(directory);
            if (files.Count > maxFiles)
            {
                foreach (var file in files.Skip(maxFiles))
                {
                    try
                    {
                        if (file.Exists && !IsFileLocked(file.FullName))
                        {
                            file.Delete();
                        }
                    }
                    catch
                    {
                        // Игнорируем заблокированные или занятые файлы
                    }
                }
            }
        }
        catch
        {
            // Не прерываем выполнение при ошибках доступа к файловой системе
        }
    }
}
