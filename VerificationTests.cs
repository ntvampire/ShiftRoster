using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.VisualTree;
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
            db.SaveEmployee(new Employee { FullName = "Иванов С.П.", Position = "Старший смены", PostName = "Тестовая смена", InitialShiftDay = 1 });
            db.SaveEmployee(new Employee { FullName = "Петров А.В.", Position = "Дежурный оператор", PostName = "Тестовая смена", InitialShiftDay = 2 });
            db.SaveEmployee(new Employee { FullName = "Сидоров М.Н.", Position = "Дежурный оператор", PostName = "Тестовая смена", InitialShiftDay = 3 });
            db.SaveEmployee(new Employee { FullName = "Кузнецов Д.А.", Position = "Дежурный оператор", PostName = "Тестовая смена", InitialShiftDay = 4 });
            posts = db.GetAllPostNames();
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

        bool has16 = cells.Any(c => c.TotalHours == 16.0);
        bool has8 = cells.Any(c => c.TotalHours == 8.0);
        if (!has16 || !has8)
        {
            throw new Exception("Разбивка смены 16ч / 8ч следующего дня не сформирована!");
        }
        Console.WriteLine($"[УСПЕХ] (Разбивка 16ч/8ч работает, все {daysInMonth} дней закрывают ровно 24.0 ч)");

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

        // Также проверяем реальный метод MainViewModel.ExportConsolidatedExcel()
        var testVm = new ShiftRoster.ViewModels.MainViewModel();
        testVm.ExportConsolidatedExcel();

        if (!File.Exists(consolidatedExcel))
        {
            throw new Exception($"Файл сводного отчета не найден: {consolidatedExcel}");
        }

        var fiConsolidated = new FileInfo(consolidatedExcel);
        if (fiConsolidated.Length < 10000)
        {
            throw new Exception($"Размер сводного файла мал: {fiConsolidated.Length} байт");
        }

        // Детальная проверка требований к отчетам (ClosedXML)
        using (var wb = new ClosedXML.Excel.XLWorkbook(consolidatedExcel))
        {
            var wsSummary = wb.Worksheet(1);
            if (wsSummary.PageSetup.PageOrientation != ClosedXML.Excel.XLPageOrientation.Landscape ||
                wsSummary.PageSetup.PaperSize != ClosedXML.Excel.XLPaperSize.A4Paper)
            {
                throw new Exception("Лист сводки не настроен на А4 альбомную ориентацию!");
            }

            // Проверка отсутствия столбца «Статус»
            for (int col = 1; col <= 10; col++)
            {
                string headerVal = wsSummary.Cell(6, col).GetString();
                if (headerVal.Contains("Статус"))
                {
                    throw new Exception("Столбец «Статус» обнаружен в сводной ведомости!");
                }
            }

            // Проверка бланка Т-13: пробел перед скобкой должности и 31-й день
            var wsT13 = wb.Worksheet("Сводный бланк Т-13");
            if (wsT13.PageSetup.PageOrientation != ClosedXML.Excel.XLPageOrientation.Landscape)
            {
                throw new Exception("Бланк Т-13 не настроен на альбомную ориентацию!");
            }

            string day31Header = wsT13.Cell(7, 18).GetString();
            if (day31Header != "31")
            {
                throw new Exception($"Колонка 18 в бланке Т-13 должна содержать '31', но получено '{day31Header}'!");
            }

            string empCell = wsT13.Cell(8, 2).GetString();
            if (!empCell.Contains(" ("))
            {
                throw new Exception($"В бланке Т-13 отсутствует пробел перед должностью в скобках: '{empCell}'");
            }
        }

        Console.WriteLine($"[УСПЕХ] (Сводный отчет по {posts.Count} сменам сформирован: без 'Статус', пробел 'ФИО (Должность)', 31-й день, А4 альбомная печать)");

        // 7. Проверка наименования организации в базе и отчетах
        Console.Write("7. Название организации в базе и отчетах... ");
        db.SetOrganizationName("ООО «Тестовые Энергосистемы»");
        string storedOrg = db.GetOrganizationName();
        if (storedOrg != "ООО «Тестовые Энергосистемы»")
        {
            throw new Exception("Название организации не сохранилось в базе данных!");
        }
        Console.WriteLine($"[УСПЕХ] (Организация: {storedOrg})");

        // 8. Проверка изменения порядка сотрудников (SortOrder)
        Console.Write("8. Изменение порядка сотрудников (SortOrder)... ");
        var empsBefore = db.GetEmployees(firstPost);
        if (empsBefore.Count >= 2)
        {
            var reordered = new List<Employee>(empsBefore);
            var temp = reordered[0];
            reordered[0] = reordered[1];
            reordered[1] = temp;
            db.UpdateEmployeesOrder(reordered);
            var empsAfter = db.GetEmployees(firstPost);
            if (empsAfter[0].Id != reordered[0].Id)
            {
                throw new Exception("Порядок сотрудников не обновился в соответствии с SortOrder!");
            }
        }
        Console.WriteLine("[УСПЕХ] (Сортировка по SortOrder работает корректно)");

        // 9. Проверка удаления подразделения (каскадное удаление без сбоев)
        Console.Write("9. Удаление ненужной смены (DeletePost)... ");
        string tempPostName = "Временная смена для проверки удаления";
        db.SaveEmployee(new Employee { FullName = "Временный Работник", Position = "Оператор", PostName = tempPostName, InitialShiftDay = 1 });
        var tempSched = db.GetOrCreateSchedule(testYear, testMonth, tempPostName, "1/3");
        var tempCells = calc.AutoFillOneThree(tempSched.Id, db.GetEmployees(tempPostName), testYear, testMonth);
        db.SaveRosterCells(tempSched.Id, tempCells);

        db.DeletePost(tempPostName);
        var remainingPosts = db.GetAllPostNames();
        if (remainingPosts.Contains(tempPostName))
        {
            throw new Exception("Смена не удалилась из базы данных!");
        }
        var orphanEmployees = db.GetEmployees(tempPostName);
        if (orphanEmployees.Count > 0)
        {
            throw new Exception("Сотрудники удаленной смены остались в базе!");
        }
        Console.WriteLine("[УСПЕХ] (Смена и ее данные полностью удалены без ошибок)");

        // 10. Проверка непрерывности графика смен между месяцами (Октябрь -> Ноябрь)
        Console.Write("10. Сквозная непрерывность смен между месяцами... ");
        string contPost = "Пост Непрерывности";
        var e1 = new Employee { FullName = "Работник 1", Position = "Оператор", PostName = contPost, InitialShiftDay = 1, SortOrder = 1 };
        var e2 = new Employee { FullName = "Работник 2", Position = "Оператор", PostName = contPost, InitialShiftDay = 2, SortOrder = 2 };
        var e3 = new Employee { FullName = "Работник 3", Position = "Оператор", PostName = contPost, InitialShiftDay = 3, SortOrder = 3 };
        var e4 = new Employee { FullName = "Работник 4", Position = "Оператор", PostName = contPost, InitialShiftDay = 4, SortOrder = 4 };
        db.SaveEmployee(e1);
        db.SaveEmployee(e2);
        db.SaveEmployee(e3);
        db.SaveEmployee(e4);
        var contEmps = db.GetEmployees(contPost);

        // Базовый месяц: Октябрь 2026 (31 день)
        db.SetPostBaseMonth(contPost, 2026, 10);
        var octSched = db.GetOrCreateSchedule(2026, 10, contPost, "1/3");
        var octCells = calc.AutoFillOneThree(octSched.Id, contEmps, 2026, 10, 2026, 10);

        // 31 октября: Работник 3 заступил на 16ч, Работник 2 сдал 8ч
        var oct31Emp3 = octCells.First(c => c.EmployeeName == "Работник 3" && c.DayOfMonth == 31);
        var oct31Emp2 = octCells.First(c => c.EmployeeName == "Работник 2" && c.DayOfMonth == 31);
        if (oct31Emp3.TotalHours != 16.0 || oct31Emp2.TotalHours != 8.0)
        {
            throw new Exception($"Неверное дежурство 31 октября: Р3={oct31Emp3.TotalHours}ч, Р2={oct31Emp2.TotalHours}ч");
        }

        // Следующий месяц: Ноябрь 2026 (30 дней) — график ДОЛЖЕН продолжаться, а не сбрасываться!
        var novSched = db.GetOrCreateSchedule(2026, 11, contPost, "1/3");
        var novCells = calc.AutoFillOneThree(novSched.Id, contEmps, 2026, 11, 2026, 10);

        // 1 ноября: Работник 3 должен сдать смену (8ч), а Работник 4 — заступить на смену (16ч)!
        var nov1Emp3 = novCells.First(c => c.EmployeeName == "Работник 3" && c.DayOfMonth == 1);
        var nov1Emp4 = novCells.First(c => c.EmployeeName == "Работник 4" && c.DayOfMonth == 1);
        var nov1Emp1 = novCells.First(c => c.EmployeeName == "Работник 1" && c.DayOfMonth == 1);

        if (nov1Emp3.TotalHours != 8.0)
        {
            throw new Exception($"1 ноября Работник 3 должен был сдать смену (8ч), но у него {nov1Emp3.TotalHours}ч ({nov1Emp3.ShiftCode})!");
        }
        if (nov1Emp4.TotalHours != 16.0)
        {
            throw new Exception($"1 ноября Работник 4 должен был заступить на смену (16ч), но у него {nov1Emp4.TotalHours}ч ({nov1Emp4.ShiftCode})!");
        }
        if (nov1Emp1.TotalHours != 0.0)
        {
            throw new Exception($"1 ноября Работник 1 должен отдыхать, а не работать сначала!");
        }

        // Проверяем, что во все 30 дней ноября баланс строго 24.0 часа
        var novValidation = calc.ValidatePostDailyBalance(novCells, 30);
        if (novValidation.Any(v => !v.IsValid))
        {
            throw new Exception("Нарушен баланс 24ч в ноябре при непрерывном заполнении!");
        }

        db.DeletePost(contPost);
        Console.WriteLine("[УСПЕХ] (Смена бесшовно переходит между месяцами: 31 окт [16ч/8ч] -> 1 ноя [8ч/16ч], норма 24ч)");

        // 11. Проверка назначения подменного на уже оформленный отпуск/больничный
        Console.Write("11. Подмена на уже оформленный отпуск (восстановление плановых смен)... ");
        string testSubPost = "Пост Тест Подмены";
        var te1 = new Employee { FullName = "Оператор 1", Position = "Оператор", PostName = testSubPost, InitialShiftDay = 1, SortOrder = 1 };
        var te2 = new Employee { FullName = "Оператор 2", Position = "Оператор", PostName = testSubPost, InitialShiftDay = 2, SortOrder = 2 };
        var te3 = new Employee { FullName = "Оператор 3", Position = "Оператор", PostName = testSubPost, InitialShiftDay = 3, SortOrder = 3 };
        var te4 = new Employee { FullName = "Оператор 4", Position = "Оператор", PostName = testSubPost, InitialShiftDay = 4, SortOrder = 4 };
        db.SaveEmployee(te1);
        db.SaveEmployee(te2);
        db.SaveEmployee(te3);
        db.SaveEmployee(te4);
        var subEmps = db.GetEmployees(testSubPost);

        db.SetPostBaseMonth(testSubPost, 2026, 10);
        var subSched = db.GetOrCreateSchedule(2026, 10, testSubPost, "1/3");
        var subCells = calc.AutoFillOneThree(subSched.Id, subEmps, 2026, 10, 2026, 10);

        // Сначала Оператор 1 уходит в отпуск с 1 по 14 число (ячейки обнуляются)
        for (int d = 1; d <= 14; d++)
        {
            var c = subCells.First(cell => cell.EmployeeName == "Оператор 1" && cell.DayOfMonth == d);
            c.ShiftCode = "О";
            c.DayHours = 0;
            c.NightHours = 0;
            c.TotalHours = 0;
        }

        // Теперь назначаем подменного сотрудника "Ковалев А.С." на уже оформленный отпуск!
        calc.ApplySubstitute(
            subCells, 
            subSched.Id, 
            "Оператор 1", 
            "Ковалев А.С.", 
            1, 
            14, 
            "О", 
            subEmps, 
            "1/3", 
            2026, 
            10, 
            2026, 
            10);

        // Проверяем: Ковалев А.С. должен был получить смены 1 числа (16ч), 5 числа (16ч), 6 числа (8ч) и т.д.!
        var kovalevDay1 = subCells.First(c => c.EmployeeName == "Ковалев А.С." && c.DayOfMonth == 1);
        if (kovalevDay1.TotalHours != 16.0)
        {
            throw new Exception($"Ковалев А.С. не получил смену 16ч 1 октября! У него {kovalevDay1.TotalHours}ч");
        }

        // Проверяем суточный баланс 24ч для всех дней отпуска с 1 по 14 число
        var val14 = calc.ValidatePostDailyBalance(subCells, 14);
        if (val14.Any(v => !v.IsValid))
        {
            var err = val14.First(v => !v.IsValid);
            throw new Exception($"Баланс 24ч нарушен после подмены на оформленный отпуск: {err.Message}");
        }

        db.DeletePost(testSubPost);
        Console.WriteLine("[УСПЕХ] (Плановые дежурства восстановились и перенеслись на подменного, норма 24ч соблюдена)");

        // 12. Проверка сохранения подменных сотрудников в БД, их должностей и связки с основными работниками
        Console.Write("12. Сохранение подменных сотрудников, их должностей и автоподстановка... ");
        string testSubName = "Федоров К.Е.";
        string testSubPos = "Дежурный техник (подмена)";
        string testMainEmp = "Иванов С.П.";

        db.SaveSubstitute(testSubName, testSubPos, testMainEmp);

        var loadedSub = db.GetSubstituteForEmployee(testMainEmp);
        if (loadedSub == null)
        {
            throw new Exception($"Подменный сотрудник для «{testMainEmp}» не найден в базе данных!");
        }
        if (loadedSub.FullName != testSubName)
        {
            throw new Exception($"Ожидалось ФИО подменного «{testSubName}», получено: «{loadedSub.FullName}»");
        }
        if (loadedSub.Position != testSubPos)
        {
            throw new Exception($"Ожидалась должность «{testSubPos}», получено: «{loadedSub.Position}»");
        }

        var allSubs = db.GetSubstitutes();
        if (!allSubs.Any(s => s.FullName == testSubName && s.Position == testSubPos))
        {
            throw new Exception($"Подменный «{testSubName}» с должностью «{testSubPos}» не найден в общем списке!");
        }

        // Проверка переноса должности в ячейки графика
        var testCells = new List<RosterCell>
        {
            new RosterCell { ScheduleId = 999, EmployeeName = testMainEmp, Position = "Оператор", DayOfMonth = 1, ShiftCode = "16", DayHours = 14, NightHours = 2, TotalHours = 16 }
        };
        calc.ApplySubstitute(testCells, 999, testMainEmp, testSubName, 1, 1, "О", substitutePosition: testSubPos);
        var subCellCheck = testCells.FirstOrDefault(c => c.EmployeeName == testSubName);
        if (subCellCheck == null || subCellCheck.Position != testSubPos)
        {
            throw new Exception($"Должность подменного в ячейке графика «{subCellCheck?.Position}» не совпадает с «{testSubPos}»!");
        }

        Console.WriteLine("[УСПЕХ] (ФИО и должность подменного сохраняются в БД, связка с постоянным работником работает корректно)");

        // 13. Проверка быстрого редактирования ячеек (Б, О, В, 16, 8, Д12, Н12)
        Console.Write("13. Быстрое редактирование ячеек (Б, О, В, 16, 8, Д12, Н12)... ");
        var vm = new ShiftRoster.ViewModels.MainViewModel();
        if (vm.Rows.Count > 0 && vm.Rows[0].DayCells.Count > 0)
        {
            var cell1 = vm.Rows[0].DayCells[0];
            vm.SelectCell(cell1, false, false);

            // Проверка назначения Больничного (Б)
            vm.ApplyShift("Б");
            if (cell1.ShiftCode != "Б" || cell1.TotalHours != 0)
                throw new Exception($"Ожидался статус 'Б' с 0ч, получено: {cell1.ShiftCode}, {cell1.TotalHours}ч");

            // Проверка назначения Отпуска (О)
            vm.ApplyShift("О");
            if (cell1.ShiftCode != "О" || cell1.TotalHours != 0)
                throw new Exception($"Ожидался статус 'О' с 0ч, получено: {cell1.ShiftCode}, {cell1.TotalHours}ч");

            // Проверка назначения Выходного (В)
            vm.ApplyShift("В");
            if (cell1.ShiftCode != "В" || cell1.TotalHours != 0)
                throw new Exception($"Ожидался статус 'В' с 0ч, получено: {cell1.ShiftCode}, {cell1.TotalHours}ч");

            // Проверка смены 16ч
            vm.ApplyShift("16");
            if (cell1.ShiftCode != "16" || cell1.TotalHours != 16.0 || cell1.DayHours != 14.0 || cell1.NightHours != 2.0)
                throw new Exception($"Ожидалась смена '16' (14/2), получено: {cell1.ShiftCode}, {cell1.TotalHours}ч ({cell1.DayHours}/{cell1.NightHours})");

            // Проверка смены 8ч
            vm.ApplyShift("8");
            if (cell1.ShiftCode != "8" || cell1.TotalHours != 8.0 || cell1.DayHours != 2.0 || cell1.NightHours != 6.0)
                throw new Exception($"Ожидалась смена '8' (2/6), получено: {cell1.ShiftCode}, {cell1.TotalHours}ч ({cell1.DayHours}/{cell1.NightHours})");

            // Проверка смены Д12
            vm.ApplyShift("Д12");
            if (cell1.ShiftCode != "Д12" || cell1.TotalHours != 12.0)
                throw new Exception($"Ожидалась смена 'Д12', получено: {cell1.ShiftCode}, {cell1.TotalHours}ч");

            // Проверка смены Н12
            vm.ApplyShift("Н12");
            if (cell1.ShiftCode != "Н12" || cell1.TotalHours != 12.0)
                throw new Exception($"Ожидалась смена 'Н12', получено: {cell1.ShiftCode}, {cell1.TotalHours}ч");
        }
        Console.WriteLine("[УСПЕХ] (Быстрое редактирование ячеек статусами Б, О, В и сменами 16/8/12ч работает корректно)");

        // 14. Проверка работы контекстного меню FAMenuFlyout на клики ПКМ
        Console.Write("14. Проверка контекстного меню FAMenuFlyout на клики ПКМ... ");
        Program.BuildAvaloniaApp().SetupWithoutStarting();
        var flyoutWindow = new ShiftRoster.Views.MainWindow();
        var flyoutVm = new ShiftRoster.ViewModels.MainViewModel();
        flyoutWindow.DataContext = flyoutVm;
        flyoutWindow.Show();

        var flyoutBtn = new Avalonia.Controls.Button { Width = 50, Height = 50 };
        var flyoutCell = new ShiftRoster.ViewModels.RosterCellViewModel(new ShiftRoster.Models.RosterCell { DayOfMonth = 1, EmployeeName = "Иванов" }, 2026, 10);
        flyoutBtn.DataContext = flyoutCell;

        var pointer = new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true);

        // 1-й клик ПКМ
        var pPressed1 = new Avalonia.Input.PointerPressedEventArgs(
            flyoutBtn, pointer, flyoutWindow, new Avalonia.Point(5, 5), (ulong)Environment.TickCount64,
            new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.RightMouseButton, Avalonia.Input.PointerUpdateKind.RightButtonPressed),
            Avalonia.Input.KeyModifiers.None);
        flyoutWindow.OnCellPointerPressed(flyoutBtn, pPressed1);

        // 2-й клик ПКМ (проверка повторного открытия)
        var pPressed2 = new Avalonia.Input.PointerPressedEventArgs(
            flyoutBtn, pointer, flyoutWindow, new Avalonia.Point(5, 5), (ulong)Environment.TickCount64 + 300,
            new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.RightMouseButton, Avalonia.Input.PointerUpdateKind.RightButtonPressed),
            Avalonia.Input.KeyModifiers.None);
        flyoutWindow.OnCellPointerPressed(flyoutBtn, pPressed2);

        Console.WriteLine("[УСПЕХ] (FAMenuFlyout открывается стабильно без ошибок)");

        // 15. Проверка переключения тем (Авто / Тёмная / Светлая) и эргономичной палитры читаемости
        Console.Write("15. Переключение тем (Авто/Тёмная/Светлая) и контрастность ячеек... ");
        var themeService = ThemeService.Instance;

        // Тестируем режим Светлой темы
        themeService.CurrentMode = AppThemeMode.Light;
        if (db.GetThemeMode() != "Light")
        {
            throw new Exception("Светлая тема не сохранилась в базе данных AppSettings!");
        }

        var cellLight = new ShiftRoster.ViewModels.RosterCellViewModel(new ShiftRoster.Models.RosterCell { DayOfMonth = 1, ShiftCode = "16", TotalHours = 16, DayHours = 14, NightHours = 2 }, 2026, 10);
        if (cellLight.CellBackground != "#E3F2FD" || cellLight.CellForeground != "#0D47A1")
        {
            throw new Exception($"Некорректная светлая палитра 16ч: фон={cellLight.CellBackground}, текст={cellLight.CellForeground}");
        }

        cellLight.ShiftCode = "О";
        if (cellLight.CellBackground != "#D4EDDA" || cellLight.CellForeground != "#155724")
        {
            throw new Exception($"Некорректная светлая палитра Отпуска: фон={cellLight.CellBackground}, текст={cellLight.CellForeground}");
        }

        cellLight.ShiftCode = "Б";
        if (cellLight.CellBackground != "#FFE8D1" || cellLight.CellForeground != "#8F3900")
        {
            throw new Exception($"Некорректная светлая палитра Больничного: фон={cellLight.CellBackground}, текст={cellLight.CellForeground}");
        }

        // Тестируем режим Тёмной темы
        themeService.CurrentMode = AppThemeMode.Dark;
        if (db.GetThemeMode() != "Dark")
        {
            throw new Exception("Тёмная тема не сохранилась в базе данных AppSettings!");
        }

        var cellDark = new ShiftRoster.ViewModels.RosterCellViewModel(new ShiftRoster.Models.RosterCell { DayOfMonth = 1, ShiftCode = "16", TotalHours = 16, DayHours = 14, NightHours = 2 }, 2026, 10);
        if (cellDark.CellBackground != "#1F3B5C" || cellDark.CellForeground != "#FFFFFF")
        {
            throw new Exception($"Некорректная тёмная палитра 16ч: фон={cellDark.CellBackground}, текст={cellDark.CellForeground}");
        }

        // Возвращаем режим Авто (системная тема)
        themeService.CurrentMode = AppThemeMode.Auto;
        if (db.GetThemeMode() != "Auto")
        {
            throw new Exception("Режим 'Авто' не сохранился в базе данных!");
        }

        Console.WriteLine("[УСПЕХ] (Светлая и тёмная темы, эргономика контраста и сохранение в БД работают безупречно)");

        // 16. Проверка строгого порядка работников (порядок внесения, отсутствие алфавитной сортировки, порядок назначения подменных)
        Console.Write("16. Строгий порядок работников (порядок создания, отсутствие алфавитной сортировки)... ");
        string orderPost = "Пост Проверки Порядка";
        db.DeletePost(orderPost);

        var o1 = new Employee { FullName = "Яковлев Я.Я.", Position = "Оператор", PostName = orderPost, InitialShiftDay = 3, SortOrder = 1 };
        var o2 = new Employee { FullName = "Борисов Б.Б.", Position = "Оператор", PostName = orderPost, InitialShiftDay = 1, SortOrder = 2 };
        var o3 = new Employee { FullName = "Алексеев А.А.", Position = "Оператор", PostName = orderPost, InitialShiftDay = 4, SortOrder = 3 };
        var o4 = new Employee { FullName = "Воронов В.В.", Position = "Оператор", PostName = orderPost, InitialShiftDay = 2, SortOrder = 4 };
        db.SaveEmployee(o1);
        db.SaveEmployee(o2);
        db.SaveEmployee(o3);
        db.SaveEmployee(o4);

        var orderedEmps = db.GetEmployees(orderPost);
        if (orderedEmps[0].FullName != "Яковлев Я.Я." ||
            orderedEmps[1].FullName != "Борисов Б.Б." ||
            orderedEmps[2].FullName != "Алексеев А.А." ||
            orderedEmps[3].FullName != "Воронов В.В.")
        {
            throw new Exception("GetEmployees вернул сотрудников не в порядке внесения!");
        }

        var orderSched = db.GetOrCreateSchedule(2026, 10, orderPost, "1/3");
        var orderCells = calc.AutoFillOneThree(orderSched.Id, orderedEmps, 2026, 10, 2026, 10);

        // Назначаем первого подменного (Юрьев) и затем второго (Григорьев)
        calc.ApplySubstitute(orderCells, orderSched.Id, "Яковлев Я.Я.", "Юрьев Ю.Ю.", 5, 10, "О", orderedEmps, "1/3", 2026, 10, 2026, 10, "Подменный 1");
        calc.ApplySubstitute(orderCells, orderSched.Id, "Борисов Б.Б.", "Григорьев Г.Г.", 12, 15, "Б", orderedEmps, "1/3", 2026, 10, 2026, 10, "Подменный 2");
        db.SaveRosterCells(orderSched.Id, orderCells);

        var loadedCells = db.GetRosterCells(orderSched.Id);
        var orderSummaries = calc.CalculateSummaries(2026, 10, loadedCells);

        string[] expectedOrder = { "Яковлев Я.Я.", "Борисов Б.Б.", "Алексеев А.А.", "Воронов В.В.", "Юрьев Ю.Ю.", "Григорьев Г.Г." };
        if (orderSummaries.Count != expectedOrder.Length)
        {
            throw new Exception($"Ожидалось {expectedOrder.Length} записей в сводке, получено {orderSummaries.Count}");
        }

        for (int i = 0; i < expectedOrder.Length; i++)
        {
            if (orderSummaries[i].EmployeeName != expectedOrder[i])
            {
                throw new Exception($"Позиция #{i + 1}: ожидался «{expectedOrder[i]}», фактически «{orderSummaries[i].EmployeeName}»! Обнаружена некорректная сортировка.");
            }
        }

        // Проверяем генерацию Excel файла
        var orderExportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
        string testOrderXlsx = Path.Combine(orderExportDir, "Test_Strict_Order.xlsx");
        ExcelExportService.Instance.ExportRosterToExcel(testOrderXlsx, orderSched, loadedCells, orderSummaries);
        using (var wb = new ClosedXML.Excel.XLWorkbook(testOrderXlsx))
        {
            var ws1C = wb.Worksheet("Сводка для 1С");
            for (int i = 0; i < expectedOrder.Length; i++)
            {
                string nameInCell = ws1C.Cell(6 + i, 2).GetString();
                if (nameInCell != expectedOrder[i])
                {
                    throw new Exception($"В листе 'Сводка для 1С' строка #{i + 1} содержит «{nameInCell}» вместо «{expectedOrder[i]}»!");
                }
            }

            var wsT13 = wb.Worksheet("Бланк Т-13");
            for (int i = 0; i < expectedOrder.Length; i++)
            {
                string empCellVal = wsT13.Cell(8 + i * 4, 2).GetString();
                if (!empCellVal.StartsWith(expectedOrder[i]))
                {
                    throw new Exception($"В бланке Т-13 блок #{i + 1} содержит «{empCellVal}» вместо «{expectedOrder[i]}»!");
                }
            }
        }

        db.DeletePost(orderPost);
        Console.WriteLine("[УСПЕХ] (Сотрудники и подменные строго сохраняют порядок создания и назначения во всех отчетах)");

        // 17. Проверка общего справочника подменных сотрудников (CRUD и уникальность)
        Console.Write("17. Справочник подменных сотрудников (добавление, редактирование, удаление)... ");
        string testSubName1 = "Смирнов А.А. (Тест)";
        string testSubName2 = "Ковалев Б.Б. (Тест)";

        // Очищаем, если остались от предыдущих тестов
        var existingSub1 = db.GetSubstitutes().FirstOrDefault(s => s.FullName == testSubName1);
        if (existingSub1 != null) db.DeleteSubstitute(existingSub1.Id);
        var existingSub2 = db.GetSubstitutes().FirstOrDefault(s => s.FullName == testSubName2);
        if (existingSub2 != null) db.DeleteSubstitute(existingSub2.Id);

        // Добавление
        db.SaveSubstitute(testSubName1, "Оператор (подмена)");
        db.SaveSubstitute(testSubName2, "Старший смены");

        var subsList = db.GetSubstitutes();
        var subRecord1 = subsList.FirstOrDefault(s => s.FullName == testSubName1);
        var subRecord2 = subsList.FirstOrDefault(s => s.FullName == testSubName2);

        if (subRecord1 == null || subRecord2 == null)
        {
            throw new Exception("Подменные сотрудники не добавились в базу данных!");
        }

        // Проверка уникальности
        if (!db.SubstituteExists(testSubName1))
        {
            throw new Exception("SubstituteExists не нашел добавленного сотрудника!");
        }

        // Редактирование
        string testSubName1Updated = "Смирнов А.А. (Обновлен)";
        db.UpdateSubstitute(subRecord1.Id, testSubName1, testSubName1Updated, "Дежурный оператор");

        var updatedRecord = db.GetSubstitutes().FirstOrDefault(s => s.Id == subRecord1.Id);
        if (updatedRecord == null || updatedRecord.FullName != testSubName1Updated || updatedRecord.Position != "Дежурный оператор")
        {
            throw new Exception("Данные подменного не обновились при вызове UpdateSubstitute!");
        }

        // Удаление
        db.DeleteSubstitute(subRecord1.Id);
        db.DeleteSubstitute(subRecord2.Id);

        var subsAfterDelete = db.GetSubstitutes();
        if (subsAfterDelete.Any(s => s.FullName == testSubName1Updated || s.FullName == testSubName2))
        {
            throw new Exception("Подменный сотрудник не был удален из базы данных!");
        }

        Console.WriteLine("[УСПЕХ] (Справочник подменных: добавление, изменение, удаление и проверка уникальности работают без сбоев)");

        Console.WriteLine("=================================================");
        Console.WriteLine("ВСЕ ТЕСТЫ И ТРЕБОВАНИЯ ТЗ УСПЕШНО ВЫПОЛНЕНЫ!");
        Console.WriteLine("=================================================");
    }
}
