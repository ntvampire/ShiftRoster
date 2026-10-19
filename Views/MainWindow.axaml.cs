using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.ViewModels;

namespace ShiftRoster.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AssignSubstituteButton.Click += OnAssignSubstituteClicked;
        ExportExcelButton.Click += OnExportExcelClicked;
    }

    private async void OnAssignSubstituteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialog = new AssignSubstituteDialog();
        var employeeNames = vm.Rows
            .Where(r => !r.IsSubstitute)
            .Select(r => r.EmployeeName)
            .Distinct()
            .ToList();

        if (employeeNames.Count == 0)
        {
            vm.StatusMessage = "В текущем графике нет штатных сотрудников!";
            return;
        }

        int maxDays = DateTime.DaysInMonth(vm.CurrentYear, vm.CurrentMonth);
        dialog.Initialize(employeeNames, maxDays);

        bool result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            vm.AssignSubstitute(
                dialog.SelectedMainEmployeeName,
                dialog.EnteredSubstituteFullName,
                dialog.StartDay,
                dialog.EndDay,
                dialog.ReasonCode);
        }
    }

    private void OnExportExcelClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            string exportedPath = vm.ExportToExcel();
            if (File.Exists(exportedPath))
            {
                // Открываем созданный Excel-файл системным приложением
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exportedPath,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Если Excel не сопоставлен, открываем папку
                    string? dir = Path.GetDirectoryName(exportedPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }
}