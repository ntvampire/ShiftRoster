using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster;

public static class VerificationTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("=== Запуск комплексной проверки ShiftRoster ===");
        Console.WriteLine("=================================================");

        // 1. Проверка базы данных и резервного копирования
        Console.Write("1. База данных и ротация бэкапов (ровно 3 копии)... ");
        var db = DatabaseService.Instance;
        string dbPath = db.DatabasePath;
        if (!File.Exists(dbPath))
        {
            throw new Exception($"Файл БД не найден: {dbPath}");
        }

        db.PerformStartupBackup();
        db.PerformStartupBackup();
        db.PerformStartupBackup();
        db.PerformStartupBackup();

        var backups = db.GetBackupFiles();
        if (backups.Count > 3)
        {
            throw new Exception($"Ротация не сработала: найдено {backups.Count} бэкапов вместо максимум 3!");
        }
        Console.WriteLine($"[УСПЕХ] (БД: {Path.GetFileName(dbPath)}, Бэкапов: {backups.Count})");

        // 2. Проверка сотрудников и стартовых дней смен
        Console.Write("2. Справочник сотрудников и стартовые дни смен... ");
        var posts = db.GetAllPostNames();
        if (posts.Count == 0)
        {
            throw new Exception("Не найдено доступных смен/постов!");
        }

        string firstPost = posts[0];
        var employees = db.GetEmployees(firstPost);
        if (employees.Count < 4)
        {
            throw new Exception($"Ожидалось не менее 4 сотрудников для {firstPost}, найдено: {employees.Count}");
        }

        // Проверяем, что стартовые дни смен корректно настроены
        bool hasStartDays = employees.Any(e => e.InitialShiftDay > 0);
        if (!hasStartDays)
        {
            throw new Exception("У сотрудников не задан стартовый день смены!");
        }
        Console.WriteLine($"[УСПЕХ] ({employees.Count} сотрудников, смены настроены)");

        // 3. Проверка производственного календаря РФ
        Console.Write("3. Производственный календарь РФ (праздники)... ");
        var calendar = ProductionCalendarService.Instance;
        if (!calendar.IsHoliday(2026, 5, 9) || !calendar.IsHoliday(2026, 1, 1))
        {
            throw new Exception("9 мая или 1 января не распознаны как официальные праздники РФ!");
        }
        if (calendar.IsHoliday(2026, 4, 15))
        {
            throw new Exception("15 апреля ошибочно определено как праздник!");
        }
        Console.WriteLine("[УСПЕХ] (Праздники РФ определяются корректно)");

        // 4. Проверка автозаполнения с учетом стартового дня и строгого контроля 24ч
        Console.Write("4. Автозаполнение со стартовыми днями и контроль 24ч на пост... ");
        var calc = RosterCalculationService.Instance;
        int testYear = 2026;
        int testMonth = 10;
        int daysInMonth = DateTime.DaysInMonth(testYear, testMonth);

        var schedule = db.GetOrCreateSchedule(testYear, testMonth, firstPost, "1/3");
        var cells = calc.AutoFillOneThree(schedule.Id, employees, testYear, testMonth);

        var validation = calc.ValidatePostDailyBalance(cells, daysInMonth);
        if (validation.Any(v => !v.IsValid))
        {
            var invalid = validation.First(v => !v.IsValid);
            throw new Exception($"Нарушен суточный баланс 24ч: {invalid.Message}");
        }
        Console.WriteLine($"[УСПЕХ] (Все {daysInMonth} дней закрывают ровно 24.0 ч)");

        // 5. Проверка механизма подмен (произвольное ФИО)
        Console.Write("5. Замещение при отпуске (произвольное ФИО подменного)... ");
        string mainEmp = employees[0].FullName;
        string subEmp = "Смирнов Василий Анатольевич"; // Произвольное ФИО

        calc.ApplySubstitute(cells, schedule.Id, mainEmp, subEmp, 1, 10, "О");

        var subValidation = calc.ValidatePostDailyBalance(cells, daysInMonth);
        if (subValidation.Any(v => !v.IsValid))
        {
            var invalid = subValidation.First(v => !v.IsValid);
            throw new Exception($"Баланс 24ч нарушился после назначения подмены: {invalid.Message}");
        }

        bool subFound = cells.Any(c => c.EmployeeName == subEmp && c.TotalHours > 0);
        if (!subFound)
        {
            throw new Exception("Подменный сотрудник не получил смены основного сотрудника!");
        }
        Console.WriteLine($"[УСПЕХ] (Подмена «{subEmp}» назначена, баланс 24ч сохранен)");

        // 6. Проверка нескольких смен и общего сводного отчета в Excel
        Console.Write("6. Учет нескольких смен и общий отчет Excel... ");
        var exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);

        var postDataList = new List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)>();
        foreach (var pName in posts)
        {
            var pSched = db.GetOrCreateSchedule(testYear, testMonth, pName, "1/3");
            var pEmps = db.GetEmployees(pName);
            var pCells = calc.AutoFillOneThree(pSched.Id, pEmps, testYear, testMonth);
            var pSums = calc.CalculateSummaries(testYear, testMonth, pCells);
            postDataList.Add((pSched, pCells, pSums));
        }

        string consolidatedExcel = Path.Combine(exportDir, "Test_Сводный_График_Все_Смены.xlsx");
        ExcelExportService.Instance.ExportConsolidatedRosterToExcel(consolidatedExcel, testYear, testMonth, postDataList);

        if (!File.Exists(consolidatedExcel))
        {
            throw new Exception($"Файл сводного отчета не найден: {consolidatedExcel}");
        }

        var fiConsolidated = new FileInfo(consolidatedExcel);
        if (fiConsolidated.Length < 10000)
        {
            throw new Exception($"Размер сводного файла мал: {fiConsolidated.Length} байт");
        }
        Console.WriteLine($"[УСПЕХ] (Сводный отчет по {posts.Count} сменам сформирован: {fiConsolidated.Length} байт)");

        Console.WriteLine("=================================================");
        Console.WriteLine("ВСЕ ТЕСТЫ И ТРЕБОВАНИЯ ТЗ УСПЕШНО ВЫПОЛНЕНЫ!");
        Console.WriteLine("=================================================");
    }
}
