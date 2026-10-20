using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class AssignSubstituteDialog : Window
{
    private List<ExistingAbsencePeriod> _existingAbsences = new();
    private List<SubstituteRecord> _savedSubstitutes = new();
    private bool _isUpdatingInternally;

    public string SelectedMainEmployeeName { get; private set; } = string.Empty;
    public string EnteredSubstituteFullName { get; private set; } = string.Empty;
    public string EnteredSubstitutePosition { get; private set; } = "Оператор (подмена)";
    public int StartDay { get; private set; } = 1;
    public int EndDay { get; private set; } = 1;
    public string ReasonCode { get; private set; } = "О";

    public AssignSubstituteDialog()
    {
        InitializeComponent();

        ApplyButton.Click += OnApplyClicked;
        CancelButton.Click += (_, _) => Close(false);
        ExistingAbsencesComboBox.SelectionChanged += OnExistingAbsenceSelectionChanged;
        MainEmployeeComboBox.SelectionChanged += OnMainEmployeeSelectionChanged;

        SubstituteNameBox.KeyUp += (_, _) => OnSubstituteNameTyped();
        SubstituteNameBox.SelectionChanged += (_, _) => OnSubstituteNameTyped();
    }

    /// <summary>
    /// Инициализация диалога со списком существующих отсутствий в месяце
    /// </summary>
    public void Initialize(
        IEnumerable<string> employeeNames, 
        int maxDaysInMonth, 
        List<ExistingAbsencePeriod>? existingAbsences = null,
        string? preselectedEmployee = null,
        int? preselectedStartDay = null,
        int? preselectedEndDay = null,
        string? preselectedReason = null)
    {
        _isUpdatingInternally = true;
        try
        {
            var empList = employeeNames.ToList();
            MainEmployeeComboBox.ItemsSource = empList;

            EndDayNumeric.Maximum = maxDaysInMonth;
            StartDayNumeric.Maximum = maxDaysInMonth;

            // Загружаем сохраненных в БД подменных сотрудников
            _savedSubstitutes = DatabaseService.Instance.GetSubstitutes();
            var substituteNames = _savedSubstitutes.Select(s => s.FullName).Distinct().ToList();
            SubstituteNameBox.ItemsSource = substituteNames;

            // Настройка списка существующих отсутствий
            _existingAbsences = existingAbsences ?? new List<ExistingAbsencePeriod>();
            
            var comboItems = new List<string>();
            foreach (var p in _existingAbsences)
            {
                comboItems.Add(p.DisplayText);
            }
            comboItems.Add("✏️ Указать сотрудника и даты вручную");

            ExistingAbsencesComboBox.ItemsSource = comboItems;

            if (_existingAbsences.Count > 0)
            {
                ExistingAbsencesBorder.IsVisible = true;
                // Ищем период без подмены
                int firstWithoutSub = _existingAbsences.FindIndex(a => !a.HasSubstitute);
                ExistingAbsencesComboBox.SelectedIndex = firstWithoutSub >= 0 ? firstWithoutSub : 0;
            }
            else
            {
                ExistingAbsencesBorder.IsVisible = false;
                ExistingAbsencesComboBox.SelectedIndex = comboItems.Count - 1;
            }

            // Если переданы предзаполненные данные (например, вызов сразу после оформления отпуска)
            if (!string.IsNullOrWhiteSpace(preselectedEmployee))
            {
                MainEmployeeComboBox.SelectedItem = preselectedEmployee;
                if (preselectedStartDay.HasValue) StartDayNumeric.Value = preselectedStartDay.Value;
                if (preselectedEndDay.HasValue) EndDayNumeric.Value = preselectedEndDay.Value;

                if (!string.IsNullOrWhiteSpace(preselectedReason))
                {
                    ReasonComboBox.SelectedIndex = preselectedReason == "Б" ? 1 : 0;
                }
            }
            else if (MainEmployeeComboBox.SelectedIndex < 0 && empList.Count > 0)
            {
                MainEmployeeComboBox.SelectedIndex = 0;
                StartDayNumeric.Value = 1;
                EndDayNumeric.Value = Math.Min(14, maxDaysInMonth);
            }

            // Проверяем сохраненного подменного для выбранного сотрудника
            AutoFillSubstituteForCurrentEmployee();
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void OnMainEmployeeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingInternally) return;
        AutoFillSubstituteForCurrentEmployee();
    }

    private void AutoFillSubstituteForCurrentEmployee()
    {
        var mainEmp = MainEmployeeComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(mainEmp)) return;

        // 1. Ищем подменного, который ранее заменял именно этого сотрудника
        var previousSub = DatabaseService.Instance.GetSubstituteForEmployee(mainEmp);
        if (previousSub != null)
        {
            SubstituteNameBox.Text = previousSub.FullName;
            SubstitutePositionTextBox.Text = previousSub.Position;
            return;
        }

        // 2. Если для этого сотрудника еще не было подменного, берем самого недавнего из базы (если есть)
        if (string.IsNullOrWhiteSpace(SubstituteNameBox.Text) && _savedSubstitutes.Count > 0)
        {
            var latest = _savedSubstitutes[0];
            SubstituteNameBox.Text = latest.FullName;
            SubstitutePositionTextBox.Text = latest.Position;
            return;
        }

        // 3. Если поле должности пустое, выставляем значение по умолчанию
        if (string.IsNullOrWhiteSpace(SubstitutePositionTextBox.Text))
        {
            SubstitutePositionTextBox.Text = "Оператор (подмена)";
        }
    }

    private void OnSubstituteNameTyped()
    {
        if (_isUpdatingInternally) return;

        string currentText = SubstituteNameBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(currentText)) return;

        // Если введенное имя совпадает с ранее сохраненным подменным — подставляем его сохраненную должность
        var match = _savedSubstitutes.FirstOrDefault(s => s.FullName.Equals(currentText, StringComparison.OrdinalIgnoreCase));
        if (match != null && !string.IsNullOrWhiteSpace(match.Position))
        {
            SubstitutePositionTextBox.Text = match.Position;
        }
    }

    private void OnExistingAbsenceSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int idx = ExistingAbsencesComboBox.SelectedIndex;
        if (idx >= 0 && idx < _existingAbsences.Count)
        {
            var period = _existingAbsences[idx];
            _isUpdatingInternally = true;
            try
            {
                MainEmployeeComboBox.SelectedItem = period.EmployeeName;
                ReasonComboBox.SelectedIndex = period.AbsenceCode == "Б" ? 1 : 0;
                StartDayNumeric.Value = period.StartDay;
                EndDayNumeric.Value = period.EndDay;

                if (!string.IsNullOrWhiteSpace(period.SubstituteAssignedName))
                {
                    SubstituteNameBox.Text = period.SubstituteAssignedName;
                    var match = _savedSubstitutes.FirstOrDefault(s => s.FullName.Equals(period.SubstituteAssignedName, StringComparison.OrdinalIgnoreCase));
                    SubstitutePositionTextBox.Text = match?.Position ?? "Оператор (подмена)";
                }
                else
                {
                    AutoFillSubstituteForCurrentEmployee();
                }
            }
            finally
            {
                _isUpdatingInternally = false;
            }

            ShiftTransferInfoText.Text = $"К замещению: {period.ScheduledShiftsCount} смен ({period.ScheduledHours} ч). Все рабочие часы будут перенесены на подменного!";
            ShiftTransferInfoBorder.IsVisible = true;
        }
        else
        {
            ShiftTransferInfoText.Text = "Плановые дежурства основного сотрудника будут автоматически перенесены на подменного.";
        }
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        var mainEmp = MainEmployeeComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(mainEmp))
        {
            MainEmployeeComboBox.Focus();
            return;
        }

        var subName = SubstituteNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(subName))
        {
            SubstituteNameBox.Focus();
            return;
        }

        string subPos = SubstitutePositionTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(subPos))
        {
            subPos = "Оператор (подмена)";
        }

        int start = (int)(StartDayNumeric.Value ?? 1);
        int end = (int)(EndDayNumeric.Value ?? 1);

        if (start > end)
        {
            (start, end) = (end, start);
        }

        string reason = "О";
        if (ReasonComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            reason = tag;
        }

        SelectedMainEmployeeName = mainEmp;
        EnteredSubstituteFullName = subName;
        EnteredSubstitutePosition = subPos;
        StartDay = start;
        EndDay = end;
        ReasonCode = reason;

        // Сохраняем подменного в базу данных с должностью и привязкой к сотруднику
        DatabaseService.Instance.SaveSubstitute(subName, subPos, mainEmp);

        Close(true);
    }
}
