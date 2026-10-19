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
/// Табельные номера исключены согласно требованиям ТЗ.
/// </summary>
public class ExcelExportService
{
    private static ExcelExportService? _instance;
    public static ExcelExportService Instance => _instance ??= new ExcelExportService();

    public void ExportRosterToExcel(
        string filePath,
        ShiftSchedule schedule,
        List<RosterCell> cells,
        List<EmployeeMonthSummary> summaries)
    {
        using var workbook = new XLWorkbook();

        // 1. Лист «Сводка для оператора 1С» (наиболее удобный и востребованный для ручного ввода)
        Create1CSummarySheet(workbook, schedule, summaries);

        // 2. Лист «Бланк Т-13 (Печатный табель)» (официальная форма)
        CreateT13Sheet(workbook, schedule, cells, summaries);

        workbook.SaveAs(filePath);
    }

    /// <summary>
    /// Создание листа с крупной и наглядной ведомостью для переноса итоговых цифр в 1С
    /// </summary>
    private void Create1CSummarySheet(XLWorkbook workbook, ShiftSchedule schedule, List<EmployeeMonthSummary> summaries)
    {
        var ws = workbook.Worksheets.Add("Сводка для оператора 1С");
        ws.ShowGridLines = true;

        // Заголовок документа
        ws.Cell("A1").Value = "СВОДНАЯ ВЕДОМОСТЬ РАБОЧЕГО ВРЕМЕНИ ДЛЯ ВНЕСЕНИЯ В 1С";
        ws.Cell("A1").Style.Font.Bold = true;
        ws.Cell("A1").Style.Font.FontSize = 14;
        ws.Range("A1:K1").Merge();

        ws.Cell("A2").Value = $"Подразделение/Пост: {schedule.PostName}  |  Отчетный период: {ShiftSchedule.GetMonthName(schedule.Month)} {schedule.Year} г.";
        ws.Cell("A2").Style.Font.Italic = true;
        ws.Cell("A2").Style.Font.FontSize = 11;
        ws.Range("A2:K2").Merge();

        // Шапка таблицы
        int startRow = 4;
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
            var cell = ws.Cell(startRow, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2B3A4A");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }
        ws.Row(startRow).Height = 28;

        // Заполнение данных
        int currentRow = startRow + 1;
        foreach (var s in summaries)
        {
            ws.Cell(currentRow, 1).Value = s.OrderNumber;
            ws.Cell(currentRow, 2).Value = s.EmployeeName;
            ws.Cell(currentRow, 3).Value = s.Position;
            ws.Cell(currentRow, 4).Value = s.StatusText;
            ws.Cell(currentRow, 5).Value = s.DaysWorked;
            ws.Cell(currentRow, 6).Value = s.TotalHours;
            ws.Cell(currentRow, 7).Value = s.DayHours;
            ws.Cell(currentRow, 8).Value = s.NightHours;
            ws.Cell(currentRow, 9).Value = s.HolidayHours;
            ws.Cell(currentRow, 10).Value = s.VacationDays;
            ws.Cell(currentRow, 11).Value = s.SickDays;

            // Выделение подменных сотрудников мягким цветом
            if (s.IsSubstitute)
            {
                ws.Range(currentRow, 1, currentRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF3CD");
            }
            else if (currentRow % 2 == 0)
            {
                ws.Range(currentRow, 1, currentRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8F9FA");
            }

            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(currentRow, 2).Style.Font.Bold = true;
            ws.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            for (int c = 5; c <= 11; c++)
            {
                ws.Cell(currentRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            currentRow++;
        }

        // Строка ИТОГО
        var totalRow = currentRow;
        ws.Cell(totalRow, 2).Value = "ИТОГО ПО ПОСТУ:";
        ws.Cell(totalRow, 2).Style.Font.Bold = true;
        ws.Cell(totalRow, 5).FormulaA1 = $"SUM(E{startRow + 1}:E{totalRow - 1})";
        ws.Cell(totalRow, 6).FormulaA1 = $"SUM(F{startRow + 1}:F{totalRow - 1})";
        ws.Cell(totalRow, 7).FormulaA1 = $"SUM(G{startRow + 1}:G{totalRow - 1})";
        ws.Cell(totalRow, 8).FormulaA1 = $"SUM(H{startRow + 1}:H{totalRow - 1})";
        ws.Cell(totalRow, 9).FormulaA1 = $"SUM(I{startRow + 1}:I{totalRow - 1})";
        ws.Cell(totalRow, 10).FormulaA1 = $"SUM(J{startRow + 1}:J{totalRow - 1})";
        ws.Cell(totalRow, 11).FormulaA1 = $"SUM(K{startRow + 1}:K{totalRow - 1})";

        ws.Range(totalRow, 1, totalRow, 11).Style.Font.Bold = true;
        ws.Range(totalRow, 1, totalRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#E9ECEF");
        for (int c = 5; c <= 11; c++)
        {
            ws.Cell(totalRow, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Границы таблицы
        var tableRange = ws.Range(startRow, 1, totalRow, 11);
        tableRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tableRange.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

        ws.Columns().AdjustToContents(15, 45);
    }

    /// <summary>
    /// Создание листа с унифицированной формой Т-13 (печать табеля учета рабочего времени)
    /// </summary>
    private void CreateT13Sheet(XLWorkbook workbook, ShiftSchedule schedule, List<RosterCell> cells, List<EmployeeMonthSummary> summaries)
    {
        var ws = workbook.Worksheets.Add("Бланк Т-13");
        ws.ShowGridLines = true;

        // Шапка унифицированной формы
        ws.Cell("A1").Value = "Унифицированная форма № Т-13";
        ws.Cell("A1").Style.Font.FontSize = 8;
        ws.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range("A1:U1").Merge();

        ws.Cell("A2").Value = "ТАБЕЛЬ УЧЕТА РАБОЧЕГО ВРЕМЕНИ";
        ws.Cell("A2").Style.Font.Bold = true;
        ws.Cell("A2").Style.Font.FontSize = 12;
        ws.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A2:U2").Merge();

        ws.Cell("A3").Value = $"Подразделение/Участок: {schedule.PostName}  |  Период: с 01.{schedule.Month:D2}.{schedule.Year} по {DateTime.DaysInMonth(schedule.Year, schedule.Month):D2}.{schedule.Month:D2}.{schedule.Year}";
        ws.Cell("A3").Style.Font.FontSize = 10;
        ws.Cell("A3").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range("A3:U3").Merge();

        // Заголовки столбцов формы Т-13
        int startRow = 5;
        ws.Cell(startRow, 1).Value = "№ п/п";
        ws.Range(startRow, 1, startRow + 1, 1).Merge();

        ws.Cell(startRow, 2).Value = "Фамилия, инициалы, должность";
        ws.Range(startRow, 2, startRow + 1, 2).Merge();

        // Дни 1..15
        for (int d = 1; d <= 15; d++)
        {
            ws.Cell(startRow, 2 + d).Value = d.ToString();
        }
        ws.Cell(startRow, 18).Value = "Дней / Часов (1-15)";
        ws.Cell(startRow + 1, 18).Value = "Дней / Часов (16-31)";

        // Итоговые колонки
        ws.Cell(startRow, 19).Value = "Всего дней";
        ws.Range(startRow, 19, startRow + 1, 19).Merge();

        ws.Cell(startRow, 20).Value = "Всего часов";
        ws.Range(startRow, 20, startRow + 1, 20).Merge();

        ws.Cell(startRow, 21).Value = "В т.ч. ночных";
        ws.Range(startRow, 21, startRow + 1, 21).Merge();

        // Форматирование заголовка
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

            // В форме Т-13 на каждого сотрудника отводится 4 строки:
            // Строка 1: Отметки кодов за 1-15 числа
            // Строка 2: Количество часов за 1-15 числа
            // Строка 3: Отметки кодов за 16-31 числа
            // Строка 4: Количество часов за 16-31 числа

            ws.Cell(currentRow, 1).Value = order++;
            ws.Range(currentRow, 1, currentRow + 3, 1).Merge();
            ws.Cell(currentRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            ws.Cell(currentRow, 2).Value = $"{empName}\n({firstCell.Position})";
            ws.Range(currentRow, 2, currentRow + 3, 2).Merge();
            ws.Cell(currentRow, 2).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(currentRow, 2).Style.Font.Bold = true;
            ws.Cell(currentRow, 2).Style.Font.FontSize = 9;

            // 1-15 числа
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

            // 16-31 числа
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

            // Итоговые за месяц
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

            // Границы блока сотрудника
            var empBlock = ws.Range(currentRow, 1, currentRow + 3, 21);
            empBlock.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            empBlock.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            currentRow += 4;
        }

        // Подписи ответственных лиц
        currentRow += 2;
        ws.Cell(currentRow, 2).Value = "Руководитель подразделения: ________________ / ________________";
        ws.Cell(currentRow + 2, 2).Value = "Работник кадровой службы / Табельщик: ________________ / ________________";

        ws.Columns().AdjustToContents(5, 30);
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
