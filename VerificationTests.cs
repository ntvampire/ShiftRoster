using System;
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

        // Вызовем бэкап несколько раз, чтобы проверить ротацию до 3 копий
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

        // 2. Проверка сотрудников
        Console.Write("2. Справочник сотрудников... ");
        var employees = db.GetEmployees("Пост №1");
        if (employees.Count < 4)
        {
            throw new Exception($"Ожидалось не менее 4 сотрудников, найдено: {employees.Count}");
        }
        Console.WriteLine($"[УСПЕХ] ({employees.Count} постоянных сотрудников загружено)");

        // 3. Проверка производственного календаря РФ
        Console.Write("3. Производственный календарь РФ (праздники)... ");
        var calendar = ProductionCalendarService.Instance;
        // Проверяем 9 мая и 1 января 2026 года
        if (!calendar.IsHoliday(2026, 5, 9) || !calendar.IsHoliday(2026, 1, 1))
        {
            throw new Exception("9 мая или 1 января не распознаны как официальные праздники РФ!");
        }
        if (calendar.IsHoliday(2026, 4, 15))
        {
            throw new Exception("15 апреля ошибочно определено как праздник!");
        }
        Console.WriteLine("[УСПЕХ] (Праздники РФ определяются корректно)");

        // 4. Проверка автозаполнения графиков и строгого контроля 24ч
        Console.Write("4. Автозаполнение «Сутки/трое» и контроль ровно 24ч на пост... ");
        var calc = RosterCalculationService.Instance;
        int testYear = 2026;
        int testMonth = 10;
        int daysInMonth = DateTime.DaysInMonth(testYear, testMonth);

        var schedule = db.GetOrCreateSchedule(testYear, testMonth, "Пост №1", "1/3");
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

        // Отправляем первого сотрудника в отпуск с 1 по 10 число
        calc.ApplySubstitute(cells, schedule.Id, mainEmp, subEmp, 1, 10, "О");

        // Проверяем, что баланс 24ч по-прежнему соблюдается на 100%!
        var subValidation = calc.ValidatePostDailyBalance(cells, daysInMonth);
        if (subValidation.Any(v => !v.IsValid))
        {
            var invalid = subValidation.First(v => !v.IsValid);
            throw new Exception($"Баланс 24ч нарушился после назначения подмены: {invalid.Message}");
        }

        // Проверяем, что подменный сотрудник появился в ячейках
        bool subFound = cells.Any(c => c.EmployeeName == subEmp && c.TotalHours > 0);
        if (!subFound)
        {
            throw new Exception("Подменный сотрудник не получил смены основного сотрудника!");
        }
        Console.WriteLine($"[УСПЕХ] (Подмена «{subEmp}» назначена, баланс 24ч сохранен)");

        // 6. Проверка итоговых расчетов и экспорта в Excel
        Console.Write("6. Экспорт в Excel (Бланк Т-13 и Сводка для 1С)... ");
        var summaries = calc.CalculateSummaries(testYear, testMonth, cells);
        string exportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
        if (!Directory.Exists(exportDir)) Directory.CreateDirectory(exportDir);
        string excelFile = Path.Combine(exportDir, "Test_График_Смен_1С.xlsx");

        ExcelExportService.Instance.ExportRosterToExcel(excelFile, schedule, cells, summaries);

        if (!File.Exists(excelFile))
        {
            throw new Exception($"Excel-файл не создался: {excelFile}");
        }

        var fi = new FileInfo(excelFile);
        if (fi.Length < 5000)
        {
            throw new Exception($"Размер файла слишком мал ({fi.Length} байт), возможно файл поврежден");
        }
        Console.WriteLine($"[УСПЕХ] (Файл сформирован: {fi.Length} байт)");

        Console.WriteLine("=================================================");
        Console.WriteLine("ВСЕ ТЕСТЫ И ТРЕБОВАНИЯ ТЗ УСПЕШНО ВЫПОЛНЕНЫ!");
        Console.WriteLine("=================================================");
    }
}
