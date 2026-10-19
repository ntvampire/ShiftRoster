using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class AssignSubstituteDialog : Window
{
    public string SelectedMainEmployeeName { get; private set; } = string.Empty;
    public string EnteredSubstituteFullName { get; private set; } = string.Empty;
    public int StartDay { get; private set; } = 1;
    public int EndDay { get; private set; } = 1;
    public string ReasonCode { get; private set; } = "О";

    public AssignSubstituteDialog()
    {
        InitializeComponent();

        ApplyButton.Click += OnApplyClicked;
        CancelButton.Click += (_, _) => Close(false);
    }

    public void Initialize(IEnumerable<string> employeeNames, int maxDaysInMonth)
    {
        MainEmployeeComboBox.ItemsSource = employeeNames;
        MainEmployeeComboBox.SelectedIndex = 0;

        EndDayNumeric.Maximum = maxDaysInMonth;
        StartDayNumeric.Maximum = maxDaysInMonth;
        EndDayNumeric.Value = Math.Min(14, maxDaysInMonth);

        // Заполняем подсказки из ранее использовавшихся подменных сотрудников
        var recentSubstitutes = DatabaseService.Instance.GetRecentSubstituteNames();
        SubstituteNameBox.ItemsSource = recentSubstitutes;
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        var mainEmp = MainEmployeeComboBox.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(mainEmp))
        {
            return;
        }

        var subName = SubstituteNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(subName))
        {
            SubstituteNameBox.Focus();
            return;
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
        StartDay = start;
        EndDay = end;
        ReasonCode = reason;

        Close(true);
    }
}
