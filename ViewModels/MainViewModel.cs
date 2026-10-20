using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private ShiftSchedule? _currentSchedule;

    [ObservableProperty]
    private int _currentYear = DateTime.Now.Year;

    [ObservableProperty]
    private int _currentMonth = DateTime.Now.Month;

    [ObservableProperty]
    private string _currentMonthName = string.Empty;

    [ObservableProperty]
    private string _currentPostName = "Смена №1 (Пост 1)";

    [ObservableProperty]
    private string _selectedPost = "Смена №1 (Пост 1)";

    [ObservableProperty]
    private string _windowTitle = "График смен";

    [ObservableProperty]
    private string _balanceStatusBanner = "Проверка баланса...";

    [ObservableProperty]
    private string _balanceBannerBackground = "#107C41";

    [ObservableProperty]
    private string _statusMessage = "Готово к работе";

    [ObservableProperty]
    private RosterCellViewModel? _selectedCell;

    public ObservableCollection<string> AvailablePosts { get; } = new();
    public ObservableCollection<EmployeeRosterRowViewModel> Rows { get; } = new();
    public ObservableCollection<DayColumnHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<DayBalanceViewModel> DayBalances { get; } = new();

    public MainViewModel()
    {
        RefreshPosts();
        if (AvailablePosts.Count > 0)
        {
            _selectedPost = AvailablePosts[0];
            _currentPostName = _selectedPost;
        }
        LoadMonth(CurrentYear, CurrentMonth);
    }

    public void RefreshPosts()
    {
        AvailablePosts.Clear();
        var posts = DatabaseService.Instance.GetAllPostNames();
        foreach (var p in posts)
        {
            AvailablePosts.Add(p);
        }
    }

    partial void OnSelectedPostChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != CurrentPostName)
        {
            CurrentPostName = value;
            LoadMonth(CurrentYear, CurrentMonth);
        }
    }

    public void LoadMonth(int year, int month)
    {
        CurrentYear = year;
        CurrentMonth = month;
        CurrentMonthName = ShiftSchedule.GetMonthName(month);
        WindowTitle = $"График смен — {CurrentMonthName} {year} ({CurrentPostName})";

        // Загружаем производственный календарь на выбранный год
        ProductionCalendarService.Instance.LoadYear(year);

        // Получаем или создаем график в БД
        _currentSchedule = DatabaseService.Instance.GetOrCreateSchedule(year, month, CurrentPostName, "1/3");

        // Загружаем ячейки из БД
        var cells = DatabaseService.Instance.GetRosterCells(_currentSchedule.Id);

        // Если ячеек еще нет (новый месяц) — автоматически заполняем по схеме сутки/трое
        if (cells.Count == 0)
        {
            var employees = DatabaseService.Instance.GetEmployees(CurrentPostName);
            if (employees.Count > 0)
            {
                cells = RosterCalculationService.Instance.AutoFillOneThree(_currentSchedule.Id, employees, year, month);
                DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, cells);
            }
        }

        // Построение заголовков дней
        DayHeaders.Clear();
        DayBalances.Clear();
        int daysInMonth = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= daysInMonth; d++)
        {
            DayHeaders.Add(new DayColumnHeaderViewModel(year, month, d));
            DayBalances.Add(new DayBalanceViewModel(d));
        }

        // Построение строк сотрудников
        Rows.Clear();
        var empGroups = cells.GroupBy(c => c.EmployeeName).OrderBy(g => g.First().IsSubstitute).ThenBy(g => g.Key);

        foreach (var group in empGroups)
        {
            var first = group.First();
            var rowVm = new EmployeeRosterRowViewModel(
                first.EmployeeName,
                first.Position,
                first.IsSubstitute,
                first.SubstitutedForName);

            foreach (var cell in group.OrderBy(c => c.DayOfMonth))
            {
                var cellVm = new RosterCellViewModel(cell, year, month);
                rowVm.DayCells.Add(cellVm);
            }

            rowVm.RecalculateSummary(year, month);
            Rows.Add(rowVm);
        }

        RecalculateDayBalancesAndValidate();
        StatusMessage = $"Загружен график за {CurrentMonthName} {year} г.";
    }

    /// <summary>
    /// Пересчет баланса круглосуточного поста (24 часа в сутки)
    /// </summary>
    public void RecalculateDayBalancesAndValidate()
    {
        int daysInMonth = DateTime.DaysInMonth(CurrentYear, CurrentMonth);
        int invalidDaysCount = 0;

        for (int day = 1; day <= daysInMonth; day++)
        {
            double sumForDay = Rows.Sum(r => r.DayCells.FirstOrDefault(c => c.DayOfMonth == day)?.TotalHours ?? 0);
            var balanceVm = DayBalances.FirstOrDefault(b => b.DayOfMonth == day);
            balanceVm?.UpdateHours(sumForDay);

            if (Math.Abs(sumForDay - 24.0) > 0.01)
            {
                invalidDaysCount++;
            }
        }

        if (invalidDaysCount == 0)
        {
            BalanceStatusBanner = $"✅ Баланс круглосуточного поста соблюден: во все {daysInMonth} дней месяца закрыто ровно 24 часа.";
            BalanceBannerBackground = "#107C41"; // WinUI Dark Green
        }
        else
        {
            BalanceStatusBanner = $"⚠️ Внимание: в {invalidDaysCount} днях обнаружен дисбаланс смен (не 24 ч на пост)! Проверьте ячейки.";
            BalanceBannerBackground = "#C42B1C"; // WinUI Dark Red
        }
    }

    [RelayCommand]
    public void NextMonth()
    {
        if (CurrentMonth == 12)
        {
            LoadMonth(CurrentYear + 1, 1);
        }
        else
        {
            LoadMonth(CurrentYear, CurrentMonth + 1);
        }
    }

    [RelayCommand]
    public void PrevMonth()
    {
        if (CurrentMonth == 1)
        {
            LoadMonth(CurrentYear - 1, 12);
        }
        else
        {
            LoadMonth(CurrentYear, CurrentMonth - 1);
        }
    }

    [RelayCommand]
    public void SelectCell(RosterCellViewModel cell)
    {
        if (SelectedCell != null)
        {
            SelectedCell.IsSelected = false;
        }

        SelectedCell = cell;
        if (SelectedCell != null)
        {
            SelectedCell.IsSelected = true;
            StatusMessage = $"Выбрана ячейка: {cell.Model.EmployeeName}, день {cell.DayOfMonth} ({cell.ShiftCode}, {cell.TotalHours} ч)";
        }
    }

    [RelayCommand]
    public void ApplyShift(string shiftType)
    {
        if (SelectedCell == null)
        {
            StatusMessage = "Сначала выберите ячейку в таблице!";
            return;
        }

        switch (shiftType)
        {
            case "С24":
                SelectedCell.SetShift("С24", 16.0, 8.0);
                break;
            case "Д12":
                SelectedCell.SetShift("Д12", 12.0, 0.0);
                break;
            case "Н12":
                SelectedCell.SetShift("Н12", 0.0, 12.0);
                break;
            case "8":
                SelectedCell.SetShift("8", 8.0, 0.0);
                break;
            case "В":
                SelectedCell.SetShift("В", 0.0, 0.0);
                break;
            case "О":
                SelectedCell.SetShift("О", 0.0, 0.0);
                break;
            case "Б":
                SelectedCell.SetShift("Б", 0.0, 0.0);
                break;
        }

        // Пересчитываем итоги по строке и по суточному балансу
        var row = Rows.FirstOrDefault(r => r.DayCells.Contains(SelectedCell));
        row?.RecalculateSummary(CurrentYear, CurrentMonth);

        RecalculateDayBalancesAndValidate();
        SaveRoster();
        StatusMessage = $"Смена обновлена: {SelectedCell.ShiftCode} ({SelectedCell.TotalHours} ч)";
    }

    [RelayCommand]
    public void AutoFillOneThree()
    {
        if (_currentSchedule == null) return;

        var employees = DatabaseService.Instance.GetEmployees(CurrentPostName);
        if (employees.Count == 0)
        {
            StatusMessage = "Нет активных сотрудников для автозаполнения!";
            return;
        }

        var cells = RosterCalculationService.Instance.AutoFillOneThree(_currentSchedule.Id, employees, CurrentYear, CurrentMonth);
        DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, cells);

        LoadMonth(CurrentYear, CurrentMonth);
        StatusMessage = "График успешно заполнен по схеме «Сутки через трое» (24ч на пост)!";
    }

    [RelayCommand]
    public void AutoFillTwoTwo()
    {
        if (_currentSchedule == null) return;

        var employees = DatabaseService.Instance.GetEmployees(CurrentPostName);
        if (employees.Count < 4)
        {
            StatusMessage = "Для схемы «2 через 2» требуется минимум 4 сотрудника!";
            return;
        }

        var cells = RosterCalculationService.Instance.AutoFillTwoTwo(_currentSchedule.Id, employees, CurrentYear, CurrentMonth);
        DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, cells);

        LoadMonth(CurrentYear, CurrentMonth);
        StatusMessage = "График успешно заполнен по схеме «2 через 2» (День 12ч / Ночь 12ч)!";
    }

    [RelayCommand]
    public void SaveRoster()
    {
        if (_currentSchedule == null) return;

        var allCells = Rows.SelectMany(r => r.DayCells.Select(c => c.Model)).ToList();
        DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, allCells);
        StatusMessage = "График успешно сохранен в базе данных.";
    }

    /// <summary>
    /// Назначение подменного сотрудника на даты отпуска/болезни
    /// </summary>
    public void AssignSubstitute(string mainEmployeeName, string substituteFullName, int startDay, int endDay, string reasonCode)
    {
        if (_currentSchedule == null) return;

        var allCells = Rows.SelectMany(r => r.DayCells.Select(c => c.Model)).ToList();
        RosterCalculationService.Instance.ApplySubstitute(
            allCells,
            _currentSchedule.Id,
            mainEmployeeName,
            substituteFullName,
            startDay,
            endDay,
            reasonCode);

        DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, allCells);
        LoadMonth(CurrentYear, CurrentMonth);
        StatusMessage = $"Подменный работник «{substituteFullName}» назначен взамен «{mainEmployeeName}».";
    }

    /// <summary>
    /// Экспорт табеля в Excel (Т-13 и сводка для 1С)
    /// </summary>
    public string ExportToExcel(string? targetFilePath = null)
    {
        if (_currentSchedule == null) return string.Empty;

        var allCells = Rows.SelectMany(r => r.DayCells.Select(c => c.Model)).ToList();
        var summaries = RosterCalculationService.Instance.CalculateSummaries(CurrentYear, CurrentMonth, allCells);

        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exportDir = Path.Combine(baseDir, "Exports");
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }
            targetFilePath = Path.Combine(exportDir, $"График_Смен_{CurrentMonthName}_{CurrentYear}.xlsx");
        }

        ExcelExportService.Instance.ExportRosterToExcel(targetFilePath, _currentSchedule, allCells, summaries);
        StatusMessage = $"Табель успешно экспортирован в Excel: {Path.GetFileName(targetFilePath)}";
        return targetFilePath;
    }

    /// <summary>
    /// Экспорт общего консолидированного отчета по всем сменам сразу в один файл Excel
    /// </summary>
    public string ExportConsolidatedExcel(string? targetFilePath = null)
    {
        var postDataList = new List<(ShiftSchedule Schedule, List<RosterCell> Cells, List<EmployeeMonthSummary> Summaries)>();
        var allPosts = DatabaseService.Instance.GetAllPostNames();

        foreach (var post in allPosts)
        {
            var schedule = DatabaseService.Instance.GetOrCreateSchedule(CurrentYear, CurrentMonth, post, "1/3");
            var cells = DatabaseService.Instance.GetRosterCells(schedule.Id);
            if (cells.Count == 0)
            {
                var employees = DatabaseService.Instance.GetEmployees(post);
                if (employees.Count > 0)
                {
                    cells = RosterCalculationService.Instance.AutoFillOneThree(schedule.Id, employees, CurrentYear, CurrentMonth);
                    DatabaseService.Instance.SaveRosterCells(schedule.Id, cells);
                }
            }

            var summaries = RosterCalculationService.Instance.CalculateSummaries(CurrentYear, CurrentMonth, cells);
            postDataList.Add((schedule, cells, summaries));
        }

        if (string.IsNullOrWhiteSpace(targetFilePath))
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string exportDir = Path.Combine(baseDir, "Exports");
            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }
            targetFilePath = Path.Combine(exportDir, $"Сводный_График_Смен_Все_Смены_{CurrentMonthName}_{CurrentYear}.xlsx");
        }

        ExcelExportService.Instance.ExportConsolidatedRosterToExcel(targetFilePath, CurrentYear, CurrentMonth, postDataList);
        StatusMessage = $"Сводный отчет по всем {allPosts.Count} сменам экспортирован: {Path.GetFileName(targetFilePath)}";
        return targetFilePath;
    }

    [RelayCommand]
    public async Task SyncCalendarAsync()
    {
        StatusMessage = "Синхронизация производственного календаря РФ...";
        bool ok = await ProductionCalendarService.Instance.SyncCalendarFromInternetAsync(CurrentYear);
        if (ok)
        {
            StatusMessage = $"Производственный календарь РФ на {CurrentYear} г. успешно обновлен из интернета!";
            LoadMonth(CurrentYear, CurrentMonth);
        }
        else
        {
            StatusMessage = $"Используется встроенный офлайн-календарь РФ на {CurrentYear} г. (сеть недоступна)";
        }
    }
}
