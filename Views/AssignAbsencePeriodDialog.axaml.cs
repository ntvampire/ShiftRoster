using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ShiftRoster.Views;

public partial class AssignAbsencePeriodDialog : Window
{
    public string SelectedEmployee { get; private set; } = string.Empty;
    public string SelectedStatusTag { get; private set; } = "О";
    public int StartDay { get; private set; } = 1;
    public int EndDay { get; private set; } = 14;

    public AssignAbsencePeriodDialog()
    {
        InitializeComponent();

        ApplyButton.Click += OnApplyClicked;
        CancelButton.Click += (_, _) => Close(false);
    }

    public void Initialize(List<string> employees, int currentMonthDays, string? preselectedEmployee = null, int initialDay = 1)
    {
        EmployeeComboBox.ItemsSource = employees;
        if (!string.IsNullOrWhiteSpace(preselectedEmployee) && employees.Contains(preselectedEmployee))
        {
            EmployeeComboBox.SelectedItem = preselectedEmployee;
        }
        else if (employees.Count > 0)
        {
            EmployeeComboBox.SelectedIndex = 0;
        }

        StartDayNumeric.Maximum = currentMonthDays;
        EndDayNumeric.Maximum = currentMonthDays;

        StartDayNumeric.Value = Math.Clamp(initialDay, 1, currentMonthDays);
        EndDayNumeric.Value = Math.Min(currentMonthDays, Math.Max(initialDay, 1) + 13);
    }

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (EmployeeComboBox.SelectedItem is not string emp || string.IsNullOrWhiteSpace(emp))
        {
            EmployeeComboBox.Focus();
            return;
        }

        SelectedEmployee = emp;
        if (StatusTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            SelectedStatusTag = tag;
        }

        int start = (int)(StartDayNumeric.Value ?? 1);
        int end = (int)(EndDayNumeric.Value ?? 1);

        StartDay = Math.Min(start, end);
        EndDay = Math.Max(start, end);

        Close(true);
    }
}
