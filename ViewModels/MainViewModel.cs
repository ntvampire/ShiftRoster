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
    private string _currentPostName = string.Empty;

    [ObservableProperty]
    private string _selectedPost = string.Empty;

    [ObservableProperty]
    private string _currentPostPattern = "1/3";

    [ObservableProperty]
    private string _organizationName = string.Empty;

    [ObservableProperty]
    private string _windowTitle = "График смен";

    [ObservableProperty]
    private string _balanceStatusBanner = "Проверка баланса...";

    [ObservableProperty]
    private string _balanceBannerBackground = "#107C41";

    [ObservableProperty]
    private string _statusMessage = "Готово к работе";

    [ObservableProperty]
    private bool _isSidebarOpen = true;

    [ObservableProperty]
    private RosterCellViewModel? _selectedCell;

    public ObservableCollection<RosterCellViewModel> SelectedCells { get; } = new();
    public ObservableCollection<string> AvailablePosts { get; } = new();
    public ObservableCollection<EmployeeRosterRowViewModel> Rows { get; } = new();
    public ObservableCollection<DayColumnHeaderViewModel> DayHeaders { get; } = new();
    public ObservableCollection<DayBalanceViewModel> DayBalances { get; } = new();

    public bool ShouldRunWizardOnStartup { get; set; }

    public MainViewModel()
    {
        RefreshPosts();
        if (AvailablePosts.Count > 0)
        {
            _selectedPost = AvailablePosts[0];
            _currentPostName = _selectedPost;
            ShouldRunWizardOnStartup = false;
        }
        else
        {
            _selectedPost = string.Empty;
            _currentPostName = string.Empty;
            ShouldRunWizardOnStartup = true;
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

        // Загружаем производственный календарь на выбранный год
        ProductionCalendarService.Instance.LoadYear(year);

        // Построение заголовков дней
        DayHeaders.Clear();
        DayBalances.Clear();
        int daysInMonth = DateTime.DaysInMonth(year, month);
        for (int d = 1; d <= daysInMonth; d++)
        {
            DayHeaders.Add(new DayColumnHeaderViewModel(year, month, d));
            DayBalances.Add(new DayBalanceViewModel(d));
        }

        OrganizationName = DatabaseService.Instance.GetOrganizationName();
        string orgPrefix = string.IsNullOrWhiteSpace(OrganizationName) ? "" : $"{OrganizationName} — ";

        // Если подразделение еще не настроено или удалено
        if (string.IsNullOrWhiteSpace(CurrentPostName) || AvailablePosts.Count == 0)
        {
            WindowTitle = $"{orgPrefix}График смен — {CurrentMonthName} {year} (Подразделение не выбрано)";
            Rows.Clear();
            SelectedCells.Clear();
            SelectedCell = null;
            BalanceStatusBanner = "ℹ️ Подразделения не настроены. Нажмите «➕ Новая смена» или «🧙 Мастер настройки».";
            BalanceBannerBackground = "#2B3A4A";
            StatusMessage = "Создайте подразделение для формирования графика";
            return;
        }

        CurrentPostPattern = DatabaseService.Instance.GetPostPattern(CurrentPostName);
        WindowTitle = $"{orgPrefix}График смен — {CurrentMonthName} {year} ({CurrentPostName})";

        // Получаем или создаем график в БД
        _currentSchedule = DatabaseService.Instance.GetOrCreateSchedule(year, month, CurrentPostName, CurrentPostPattern);

        // Загружаем ячейки из БД
        var cells = DatabaseService.Instance.GetRosterCells(_currentSchedule.Id);

        // Если ячеек еще нет (новый месяц) — автоматически заполняем по схеме смены со сквозной непрерывностью
        if (cells.Count == 0)
        {
            var employees = DatabaseService.Instance.GetEmployees(CurrentPostName);
            if (employees.Count > 0)
            {
                var (baseYear, baseMonth) = DatabaseService.Instance.GetPostBaseMonth(CurrentPostName);
                if (CurrentPostPattern == "2/2")
                {
                    cells = RosterCalculationService.Instance.AutoFillTwoTwo(_currentSchedule.Id, employees, year, month, baseYear, baseMonth);
                }
                else
                {
                    cells = RosterCalculationService.Instance.AutoFillOneThree(_currentSchedule.Id, employees, year, month, baseYear, baseMonth);
                }
                DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, cells);
            }
        }

        // Построение строк сотрудников в ТОЧНОМ порядке, заданном в окне «Штат»
        Rows.Clear();
        SelectedCells.Clear();
        SelectedCell = null;

        var orderedEmployees = DatabaseService.Instance.GetEmployees(CurrentPostName);
        var empRankMap = orderedEmployees.Select((e, idx) => (e.FullName, idx)).ToDictionary(x => x.FullName, x => x.idx);

        var empGroups = cells.GroupBy(c => c.EmployeeName)
            .OrderBy(g => g.First().IsSubstitute)
            .ThenBy(g => empRankMap.TryGetValue(g.Key, out int r) ? r : 999)
            .ThenBy(g => g.Key);

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
        StatusMessage = $"Загружен график смены «{CurrentPostName}» ({CurrentMonthName} {year} г.)";
    }

    /// <summary>
    /// Пересчет баланса круглосуточного поста (24 часа в сутки)
    /// </summary>
    public void RecalculateDayBalancesAndValidate()
    {
        int daysInMonth = DateTime.DaysInMonth(CurrentYear, CurrentMonth);

        if (Rows.Count == 0)
        {
            BalanceStatusBanner = $"ℹ️ В смене «{CurrentPostName}» нет сотрудников. Добавьте работников в карточке «Штат и сотрудники».";
            BalanceBannerBackground = "#2B3A4A";
            foreach (var b in DayBalances) b.UpdateHours(0);
            return;
        }

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
            BalanceStatusBanner = $"✅ Баланс поста соблюден: во все {daysInMonth} дней месяца на посту закрыто ровно 24 часа.";
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
    public void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
    }

    public void SelectCell(RosterCellViewModel cell, bool isCtrlPressed = false, bool isShiftPressed = false)
    {
        if (cell == null) return;

        // Если зажат Shift и есть предыдущая ячейка того же сотрудника — выделяем диапазон
        if (isShiftPressed && SelectedCell != null)
        {
            var row = Rows.FirstOrDefault(r => r.DayCells.Contains(cell));
            if (row != null && row.DayCells.Contains(SelectedCell))
            {
                int minDay = Math.Min(SelectedCell.DayOfMonth, cell.DayOfMonth);
                int maxDay = Math.Max(SelectedCell.DayOfMonth, cell.DayOfMonth);

                foreach (var c in row.DayCells.Where(c => c.DayOfMonth >= minDay && c.DayOfMonth <= maxDay))
                {
                    if (!SelectedCells.Contains(c))
                    {
                        c.IsSelected = true;
                        SelectedCells.Add(c);
                    }
                }

                StatusMessage = $"Выделен диапазон: {row.EmployeeName}, дни с {minDay} по {maxDay} ({SelectedCells.Count} ячеек). Выберите статус в палитре.";
                return;
            }
        }

        // Если зажат Ctrl — множественный выбор отдельных ячеек
        if (isCtrlPressed)
        {
            if (cell.IsSelected)
            {
                cell.IsSelected = false;
                SelectedCells.Remove(cell);
                SelectedCell = SelectedCells.LastOrDefault();
            }
            else
            {
                cell.IsSelected = true;
                SelectedCells.Add(cell);
                SelectedCell = cell;
            }
            StatusMessage = $"Выделено ячеек: {SelectedCells.Count}. Выберите статус в нижней палитре.";
            return;
        }

        // Одиночный клик без модификаторов — сбрасываем предыдущие выделения
        foreach (var c in SelectedCells)
        {
            c.IsSelected = false;
        }
        SelectedCells.Clear();

        if (SelectedCell != null)
        {
            SelectedCell.IsSelected = false;
        }

        SelectedCell = cell;
        cell.IsSelected = true;
        SelectedCells.Add(cell);
        StatusMessage = $"Выбрана ячейка: {cell.Model.EmployeeName}, день {cell.DayOfMonth} ({cell.ShiftCode}, {cell.TotalHours} ч). Зажмите Shift для диапазона.";
    }

    [RelayCommand]
    public void ApplyShift(string shiftType)
    {
        var targets = SelectedCells.Count > 0 ? SelectedCells.ToList() : (SelectedCell != null ? new List<RosterCellViewModel> { SelectedCell } : new List<RosterCellViewModel>());

        if (targets.Count == 0)
        {
            StatusMessage = "Сначала выберите одну или несколько ячеек в таблице!";
            return;
        }

        var affectedRows = new HashSet<EmployeeRosterRowViewModel>();

        foreach (var target in targets)
        {
            switch (shiftType)
            {
                case "16":
                    target.SetShift("16", 14.0, 2.0);
                    break;
                case "8":
                    target.SetShift("8", 2.0, 6.0);
                    break;
                case "С24":
                    target.SetShift("С24", 16.0, 8.0);
                    break;
                case "Д12":
                    target.SetShift("Д12", 12.0, 0.0);
                    break;
                case "Н12":
                    target.SetShift("Н12", 0.0, 12.0);
                    break;
                case "В":
                    target.SetShift("В", 0.0, 0.0);
                    break;
                case "О":
                    target.SetShift("О", 0.0, 0.0);
                    break;
                case "Б":
                    target.SetShift("Б", 0.0, 0.0);
                    break;
            }

            var row = Rows.FirstOrDefault(r => r.DayCells.Contains(target));
            if (row != null) affectedRows.Add(row);
        }

        foreach (var row in affectedRows)
        {
            row.RecalculateSummary(CurrentYear, CurrentMonth);
        }

        RecalculateDayBalancesAndValidate();
        SaveRoster();

        string shiftLabel = shiftType switch
        {
            "О" => "Отпуск (О)",
            "Б" => "Больничный (Б)",
            "В" => "Выходной (В)",
            "16" => "16ч (Заступление)",
            "8" => "8ч (Сдача)",
            "Д12" => "12ч День",
            "Н12" => "12ч Ночь",
            _ => shiftType
        };

        StatusMessage = targets.Count == 1 
            ? $"Смена обновлена: {shiftLabel}" 
            : $"Статус «{shiftLabel}» применен к {targets.Count} ячейкам.";
    }

    /// <summary>
    /// Массовое назначение периода отсутствия (отпуск/больничный/выходной) для сотрудника
    /// </summary>
    public void ApplyRangeForEmployee(string employeeName, string shiftCode, int startDay, int endDay)
    {
        var row = Rows.FirstOrDefault(r => r.EmployeeName == employeeName);
        if (row == null) return;

        double dHours = 0, nHours = 0;
        switch (shiftCode)
        {
            case "16": dHours = 14.0; nHours = 2.0; break;
            case "8": dHours = 2.0; nHours = 6.0; break;
            case "Д12": dHours = 12.0; nHours = 0.0; break;
            case "Н12": dHours = 0.0; nHours = 12.0; break;
            case "В":
            case "О":
            case "Б":
            default:
                dHours = 0.0; nHours = 0.0; break;
        }

        int count = 0;
        foreach (var cell in row.DayCells.Where(c => c.DayOfMonth >= startDay && c.DayOfMonth <= endDay))
        {
            cell.SetShift(shiftCode, dHours, nHours);
            count++;
        }

        row.RecalculateSummary(CurrentYear, CurrentMonth);
        RecalculateDayBalancesAndValidate();
        SaveRoster();

        string codeLabel = shiftCode == "О" ? "Отпуск (О)" : (shiftCode == "Б" ? "Больничный (Б)" : shiftCode);
        StatusMessage = $"Сотруднику «{employeeName}» назначен статус «{codeLabel}» с {startDay} по {endDay} число ({count} дн.).";
    }

    /// <summary>
    /// Автозаполнение текущего графика по схеме подразделения (1/3 или 2/2)
    /// </summary>
    [RelayCommand]
    public void AutoFillCurrentSchedule()
    {
        if (_currentSchedule == null || string.IsNullOrWhiteSpace(CurrentPostName)) return;

        string pattern = DatabaseService.Instance.GetPostPattern(CurrentPostName);
        var employees = DatabaseService.Instance.GetEmployees(CurrentPostName);

        if (employees.Count == 0)
        {
            StatusMessage = "В подразделении нет сотрудников для заполнения графика!";
            return;
        }

        var (baseYear, baseMonth) = DatabaseService.Instance.GetPostBaseMonth(CurrentPostName);

        List<RosterCell> cells;
        if (pattern == "2/2")
        {
            cells = RosterCalculationService.Instance.AutoFillTwoTwo(_currentSchedule.Id, employees, CurrentYear, CurrentMonth, baseYear, baseMonth);
            StatusMessage = "График заполнен по схеме «2 через 2» (по 12ч День/Ночь).";
        }
        else
        {
            cells = RosterCalculationService.Instance.AutoFillOneThree(_currentSchedule.Id, employees, CurrentYear, CurrentMonth, baseYear, baseMonth);
            StatusMessage = "График заполнен по схеме «Сутки через трое» (16ч/8ч, 24ч на пост).";
        }

        DatabaseService.Instance.SaveRosterCells(_currentSchedule.Id, cells);
        LoadMonth(CurrentYear, CurrentMonth);
    }

    [RelayCommand]
    public void ClearCurrentMonthRoster()
    {
        if (_currentSchedule == null) return;

        foreach (var row in Rows)
        {
            foreach (var cell in row.DayCells)
            {
                cell.SetShift("В", 0, 0);
            }
            row.RecalculateSummary(CurrentYear, CurrentMonth);
        }

        RecalculateDayBalancesAndValidate();
        SaveRoster();
        StatusMessage = "Все смены текущего месяца очищены (выставлены Выходные 'В').";
    }

    public void DeleteCurrentPost()
    {
        if (string.IsNullOrWhiteSpace(CurrentPostName)) return;

        string deletedPost = CurrentPostName;
        DatabaseService.Instance.DeletePost(deletedPost);

        RefreshPosts();
        if (AvailablePosts.Count > 0)
        {
            SelectedPost = AvailablePosts[0];
            CurrentPostName = SelectedPost;
        }
        else
        {
            SelectedPost = string.Empty;
            CurrentPostName = string.Empty;
        }

        LoadMonth(CurrentYear, CurrentMonth);
        StatusMessage = $"Подразделение «{deletedPost}» успешно удалено.";
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
            string safePostName = CurrentPostName.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
            targetFilePath = Path.Combine(exportDir, $"График_Смен_{safePostName}_{CurrentMonthName}_{CurrentYear}.xlsx");
        }

        ExcelExportService.Instance.ExportRosterToExcel(targetFilePath, _currentSchedule, allCells, summaries);
        StatusMessage = $"Табель смены «{CurrentPostName}» экспортирован в Excel: {Path.GetFileName(targetFilePath)}";
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
            string pattern = DatabaseService.Instance.GetPostPattern(post);
            var schedule = DatabaseService.Instance.GetOrCreateSchedule(CurrentYear, CurrentMonth, post, pattern);
            var cells = DatabaseService.Instance.GetRosterCells(schedule.Id);
            if (cells.Count == 0)
            {
                var employees = DatabaseService.Instance.GetEmployees(post);
                if (employees.Count > 0)
                {
                    var (baseYear, baseMonth) = DatabaseService.Instance.GetPostBaseMonth(post);
                    if (pattern == "2/2")
                    {
                        cells = RosterCalculationService.Instance.AutoFillTwoTwo(schedule.Id, employees, CurrentYear, CurrentMonth, baseYear, baseMonth);
                    }
                    else
                    {
                        cells = RosterCalculationService.Instance.AutoFillOneThree(schedule.Id, employees, CurrentYear, CurrentMonth, baseYear, baseMonth);
                    }
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
        StatusMessage = $"Сводный отчет по всем {allPosts.Count} подразделениям экспортирован: {Path.GetFileName(targetFilePath)}";
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
