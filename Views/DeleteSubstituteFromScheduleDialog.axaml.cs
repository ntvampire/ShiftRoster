using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.ViewModels;

namespace ShiftRoster.Views;

public partial class DeleteSubstituteFromScheduleDialog : Window
{
    private List<EmployeeRosterRowViewModel> _substituteRows = new();
    public string? SelectedSubstituteName { get; private set; }
    public bool RestoreShifts => RestoreShiftsCheckBox.IsChecked == true;

    public DeleteSubstituteFromScheduleDialog()
    {
        InitializeComponent();

        SubstituteComboBox.SelectionChanged += OnSubstituteSelectionChanged;
        DeleteButton.Click += OnDeleteClicked;
        CancelButton.Click += (_, _) => Close(false);
    }

    public void Initialize(
        string postName, 
        int year, 
        int month, 
        List<EmployeeRosterRowViewModel> substituteRows, 
        string? preselectedName = null)
    {
        _substituteRows = substituteRows ?? new List<EmployeeRosterRowViewModel>();

        string monthName = ShiftSchedule.GetMonthName(month);
        SubtitleText.Text = $"Подразделение: {postName}  |  Период: {monthName} {year} г.";

        var names = _substituteRows.Select(r => r.EmployeeName).Distinct().ToList();
        SubstituteComboBox.ItemsSource = names;

        if (!string.IsNullOrWhiteSpace(preselectedName) && names.Contains(preselectedName))
        {
            SubstituteComboBox.SelectedItem = preselectedName;
        }
        else if (names.Count > 0)
        {
            SubstituteComboBox.SelectedIndex = 0;
        }

        UpdateCardInfo();
    }

    private void OnSubstituteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateCardInfo();
    }

    private void UpdateCardInfo()
    {
        string? selectedName = SubstituteComboBox.SelectedItem as string;
        var row = _substituteRows.FirstOrDefault(r => r.EmployeeName == selectedName);

        if (row != null)
        {
            string mainEmp = !string.IsNullOrWhiteSpace(row.SubstitutedForName) ? row.SubstitutedForName : "—";
            SubstitutedForText.Text = mainEmp;
            AssignedHoursText.Text = $"{row.TotalHours:0.#} ч. ({row.DaysWorked} смен)";

            if (!string.IsNullOrWhiteSpace(row.SubstitutedForName))
            {
                RestoreShiftsText.Text = $"Вернуть рабочие смены основному сотруднику ({row.SubstitutedForName})";
                RestoreShiftsCheckBox.IsEnabled = true;
                RestoreShiftsCheckBox.IsChecked = true;
            }
            else
            {
                RestoreShiftsText.Text = "Смены не привязаны к конкретному сотруднику";
                RestoreShiftsCheckBox.IsEnabled = false;
                RestoreShiftsCheckBox.IsChecked = false;
            }
            DeleteButton.IsEnabled = true;
        }
        else
        {
            SubstitutedForText.Text = "—";
            AssignedHoursText.Text = "0.0 ч.";
            RestoreShiftsText.Text = "Вернуть рабочие смены основному сотруднику";
            DeleteButton.IsEnabled = false;
        }
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        SelectedSubstituteName = SubstituteComboBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(SelectedSubstituteName))
        {
            Close(true);
        }
    }
}
