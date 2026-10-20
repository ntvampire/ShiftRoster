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
        ExportConsolidatedButton.Click += OnExportConsolidatedClicked;
        ManageEmployeesButton.Click += OnManageEmployeesClicked;
        ExportCurrentMenuItem.Click += OnExportExcelClicked;
        ExportAllMenuItem.Click += OnExportConsolidatedClicked;
    }

    private async void OnManageEmployeesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialog = new ManageEmployeesDialog();
        dialog.Initialize(vm.SelectedPost);

        bool result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            // Обновляем список смен и перезагружаем график
            vm.RefreshPosts();
            if (vm.AvailablePosts.Contains(dialog.CurrentSelectedPost))
            {
                vm.SelectedPost = dialog.CurrentSelectedPost;
            }
            vm.LoadMonth(vm.CurrentYear, vm.CurrentMonth);
            vm.StatusMessage = "Данные сотрудников обновлены, график пересчитан.";
        }
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
            OpenFileOrFolder(exportedPath);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Ошибка экспорта: {ex.Message}";
        }
    }

    private void OnExportConsolidatedClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        try
        {
            string exportedPath = vm.ExportConsolidatedExcel();
            OpenFileOrFolder(exportedPath);
        }
        catch (Exception ex)
        {
            vm.StatusMessage = $"Ошибка формирования общего сводного отчета: {ex.Message}";
        }
    }

    private static void OpenFileOrFolder(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
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
}
