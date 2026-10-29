using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class ShiftCoverageDialog : Window
{
    private string _postName = string.Empty;
    private string _postPattern = "1/3";
    private int _year;
    private int _month;
    private int _scheduleId;
    private int _baseYear;
    private int _baseMonth;
    private List<Employee> _postEmployees = new();

    public bool HasChanges { get; private set; }

    public class JournalItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public object SourceObject { get; set; } = null!;
    }

    public ShiftCoverageDialog()
    {
        InitializeComponent();

        CloseButton.Click += (_, _) => Close(HasChanges);
        DayNumeric.ValueChanged += (_, _) => OnCoverageParamsChanged();
        AbsentEmployeeComboBox.SelectionChanged += (_, _) => OnCoverageParamsChanged();
        ApplyCoverageButton.Click += OnApplyCoverageClicked;

        SwapEmp1ComboBox.SelectionChanged += (_, _) => OnSwapParamsChanged();
        SwapDay1Numeric.ValueChanged += (_, _) => OnSwapParamsChanged();
        SwapEmp2ComboBox.SelectionChanged += (_, _) => OnSwapParamsChanged();
        SwapDay2Numeric.ValueChanged += (_, _) => OnSwapParamsChanged();
        ApplySwapButton.Click += OnApplySwapClicked;
    }

    public void Initialize(
        string postName,
        string postPattern,
        int year,
        int month,
        int scheduleId,
        List<Employee> postEmployees,
        int? preselectedDay = null,
        string? preselectedAbsentEmployee = null)
    {
        _postName = postName;
        _postPattern = postPattern;
        _year = year;
        _month = month;
        _scheduleId = scheduleId;
        _postEmployees = postEmployees.Where(e => !string.IsNullOrWhiteSpace(e.FullName)).ToList();

        var (bYear, bMonth) = DatabaseService.Instance.GetPostBaseMonth(postName);
        _baseYear = bYear > 0 ? bYear : year;
        _baseMonth = bMonth > 0 ? bMonth : month;

        string monthName = ShiftSchedule.GetMonthName(month);
        SubtitleText.Text = $"Подразделение: «{postName}» ({postPattern}) | Период: {monthName} {year} г.";

        int daysInMonth = DateTime.DaysInMonth(year, month);
        DayNumeric.Maximum = daysInMonth;
        SwapDay1Numeric.Maximum = daysInMonth;
        SwapDay2Numeric.Maximum = daysInMonth;

        AbsentEmployeeComboBox.ItemsSource = _postEmployees.Select(e => e.FullName).ToList();
        SwapEmp1ComboBox.ItemsSource = _postEmployees.Select(e => e.FullName).ToList();
        SwapEmp2ComboBox.ItemsSource = _postEmployees.Select(e => e.FullName).ToList();

        if (preselectedDay.HasValue && preselectedDay.Value >= 1 && preselectedDay.Value <= daysInMonth)
        {
            DayNumeric.Value = preselectedDay.Value;
            SwapDay1Numeric.Value = preselectedDay.Value;
        }

        if (!string.IsNullOrWhiteSpace(preselectedAbsentEmployee))
        {
            AbsentEmployeeComboBox.SelectedItem = preselectedAbsentEmployee;
            SwapEmp1ComboBox.SelectedItem = preselectedAbsentEmployee;
        }
        else if (_postEmployees.Count > 0)
        {
            AbsentEmployeeComboBox.SelectedIndex = 0;
            SwapEmp1ComboBox.SelectedIndex = 0;
        }

        if (_postEmployees.Count > 1)
        {
            SwapEmp2ComboBox.SelectedIndex = 1;
        }

        OnCoverageParamsChanged();
        OnSwapParamsChanged();
        RefreshJournal();
    }

    private void OnCoverageParamsChanged()
    {
        int day = (int)(DayNumeric.Value ?? 1);
        string? empName = AbsentEmployeeComboBox.SelectedItem as string;

        var emp = _postEmployees.FirstOrDefault(e => e.FullName.Equals(empName, StringComparison.OrdinalIgnoreCase));
        if (emp == null)
        {
            PlannedShiftTextBox.Text = "—";
            CandidatesListBox.ItemsSource = null;
            ApplyCoverageButton.IsEnabled = false;
            return;
        }

        var planned = RosterCalculationService.Instance.GetPlannedShiftForDay(
            emp, _postPattern, _year, _month, day, _baseYear, _baseMonth);

        if (planned.TotalHours > 0)
        {
            PlannedShiftTextBox.Text = $"{planned.ShiftCode} ({planned.TotalHours:0.#} ч: {planned.DayHours:0.#}ч день / {planned.NightHours:0.#}ч ночь)";
            ApplyCoverageButton.IsEnabled = true;
        }
        else
        {
            PlannedShiftTextBox.Text = "Выходной день (В, 0 ч)";
            ApplyCoverageButton.IsEnabled = false;
        }

        // Подбираем кандидатов из числа штатных коллег
        var candidates = RosterCalculationService.Instance.FindCoverageCandidates(
            _postEmployees, _postPattern, _year, _month, day, emp.FullName, _baseYear, _baseMonth);

        CandidatesListBox.ItemsSource = candidates;
        if (candidates.Count > 0)
        {
            CandidatesListBox.SelectedItem = candidates.FirstOrDefault(c => c.IsRecommended) ?? candidates[0];
        }
    }

    private void OnApplyCoverageClicked(object? sender, RoutedEventArgs e)
    {
        int day = (int)(DayNumeric.Value ?? 1);
        string? empName = AbsentEmployeeComboBox.SelectedItem as string;
        var emp = _postEmployees.FirstOrDefault(e => e.FullName.Equals(empName, StringComparison.OrdinalIgnoreCase));
        if (emp == null) return;

        if (CandidatesListBox.SelectedItem is not CoverageCandidate candidate) return;

        var planned = RosterCalculationService.Instance.GetPlannedShiftForDay(
            emp, _postPattern, _year, _month, day, _baseYear, _baseMonth);

        if (planned.TotalHours <= 0) return;

        string reason = "Подмена";
        if (CoverageReasonComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            reason = tag;
        }

        var sub = new ShiftSubstitution
        {
            ScheduleId = _scheduleId,
            PostName = _postName,
            DayOfMonth = day,
            ScheduledEmployeeName = emp.FullName,
            ActualEmployeeName = candidate.FullName,
            IsExternalSubstitute = false,
            ShiftCode = planned.ShiftCode,
            DayHours = planned.DayHours,
            NightHours = planned.NightHours,
            TotalHours = planned.TotalHours,
            ReasonCode = reason,
            Notes = $"Подмена в выходной за {emp.FullName} ({reason})"
        };

        DatabaseService.Instance.SaveShiftSubstitution(sub);
        HasChanges = true;

        RefreshJournal();
        OnCoverageParamsChanged();
    }

    private void OnSwapParamsChanged()
    {
        int day1 = (int)(SwapDay1Numeric.Value ?? 1);
        int day2 = (int)(SwapDay2Numeric.Value ?? 2);

        string? name1 = SwapEmp1ComboBox.SelectedItem as string;
        string? name2 = SwapEmp2ComboBox.SelectedItem as string;

        var emp1 = _postEmployees.FirstOrDefault(e => e.FullName.Equals(name1, StringComparison.OrdinalIgnoreCase));
        var emp2 = _postEmployees.FirstOrDefault(e => e.FullName.Equals(name2, StringComparison.OrdinalIgnoreCase));

        if (emp1 != null)
        {
            var p1 = RosterCalculationService.Instance.GetPlannedShiftForDay(emp1, _postPattern, _year, _month, day1, _baseYear, _baseMonth);
            SwapShift1InfoText.Text = $"Плановая смена {day1}-го числа: {p1.ShiftCode} ({p1.TotalHours:0.#} ч)";
        }
        else
        {
            SwapShift1InfoText.Text = "Плановая смена: —";
        }

        if (emp2 != null)
        {
            var p2 = RosterCalculationService.Instance.GetPlannedShiftForDay(emp2, _postPattern, _year, _month, day2, _baseYear, _baseMonth);
            SwapShift2InfoText.Text = $"Плановая смена {day2}-го числа: {p2.ShiftCode} ({p2.TotalHours:0.#} ч)";
        }
        else
        {
            SwapShift2InfoText.Text = "Плановая смена: —";
        }

        ApplySwapButton.IsEnabled = (emp1 != null && emp2 != null && !emp1.FullName.Equals(emp2.FullName, StringComparison.OrdinalIgnoreCase));
    }

    private void OnApplySwapClicked(object? sender, RoutedEventArgs e)
    {
        int day1 = (int)(SwapDay1Numeric.Value ?? 1);
        int day2 = (int)(SwapDay2Numeric.Value ?? 2);

        string? name1 = SwapEmp1ComboBox.SelectedItem as string;
        string? name2 = SwapEmp2ComboBox.SelectedItem as string;

        if (string.IsNullOrWhiteSpace(name1) || string.IsNullOrWhiteSpace(name2) || name1.Equals(name2, StringComparison.OrdinalIgnoreCase))
            return;

        var swap = new ShiftSwap
        {
            ScheduleId = _scheduleId,
            PostName = _postName,
            Day1 = day1,
            Employee1Name = name1,
            Day2 = day2,
            Employee2Name = name2,
            Reason = "Взаимная договоренность"
        };

        DatabaseService.Instance.SaveShiftSwap(swap);
        HasChanges = true;

        RefreshJournal();
        OnSwapParamsChanged();
    }

    private void RefreshJournal()
    {
        var items = new List<JournalItem>();

        var subs = DatabaseService.Instance.GetShiftSubstitutions(_scheduleId)
            .Where(s => !s.IsExternalSubstitute)
            .ToList();

        foreach (var s in subs)
        {
            items.Add(new JournalItem
            {
                Title = $"⚡ Выход коллеги: {s.ActualEmployeeName} за {s.ScheduledEmployeeName} ({s.DayOfMonth}-е число)",
                Subtitle = $"Смена: {s.ShiftCode} ({s.TotalHours}ч) | Причина: {s.ReasonCode}",
                SourceObject = s
            });
        }

        var swaps = DatabaseService.Instance.GetShiftSwaps(_scheduleId);
        foreach (var sw in swaps)
        {
            items.Add(new JournalItem
            {
                Title = $"🔄 Обмен сменами: {sw.Employee1Name} ({sw.Day1}-е) ⇄ {sw.Employee2Name} ({sw.Day2}-е)",
                Subtitle = $"Причина: {sw.Reason ?? "По согласованию"}",
                SourceObject = sw
            });
        }

        ActiveEventsListBox.ItemsSource = items;
    }

    private void OnCancelEventClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is JournalItem item)
        {
            if (item.SourceObject is ShiftSubstitution sub)
            {
                DatabaseService.Instance.DeleteShiftSubstitution(sub.Id);
                HasChanges = true;
            }
            else if (item.SourceObject is ShiftSwap swap)
            {
                DatabaseService.Instance.DeleteShiftSwap(swap.Id);
                HasChanges = true;
            }

            RefreshJournal();
            OnCoverageParamsChanged();
            OnSwapParamsChanged();
        }
    }
}
