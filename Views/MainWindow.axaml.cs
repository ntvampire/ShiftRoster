using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using ShiftRoster.ViewModels;

namespace ShiftRoster.Views;

public partial class MainWindow : Window
{
    private bool _isCtrlPressed;
    private bool _isShiftPressed;

    public MainWindow()
    {
        InitializeComponent();

        AddNewPostSidebarButton.Click += OnAddNewPostClicked;
        DeletePostSidebarButton.Click += OnDeletePostClicked;
        ManageEmployeesSidebarButton.Click += OnManageEmployeesClicked;
        SetupWizardSidebarButton.Click += OnSetupWizardClicked;
        AssignAbsencePeriodSidebarButton.Click += OnAssignAbsencePeriodClicked;
        AssignSubstituteSidebarButton.Click += OnAssignSubstituteClicked;
        ExportExcelSidebarButton.Click += OnExportExcelClicked;
        ExportConsolidatedSidebarButton.Click += OnExportConsolidatedClicked;

        Loaded += OnMainWindowLoaded;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl) _isCtrlPressed = true;
        if (e.Key is Key.LeftShift or Key.RightShift) _isShiftPressed = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl) _isCtrlPressed = false;
        if (e.Key is Key.LeftShift or Key.RightShift) _isShiftPressed = false;
    }

    private async void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.ShouldRunWizardOnStartup)
        {
            await OpenSetupWizardAsync(vm);
        }
    }

    private async void OnSetupWizardClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await OpenSetupWizardAsync(vm);
        }
    }

    private async System.Threading.Tasks.Task OpenSetupWizardAsync(MainViewModel vm)
    {
        var dialog = new SetupWizardDialog();
        bool result = await dialog.ShowDialog<bool>(this);
        if (result && dialog.HasCreatedAnyPost)
        {
            vm.RefreshPosts();
            vm.SelectedPost = dialog.FirstCreatedPost;
            vm.LoadMonth(vm.CurrentYear, vm.CurrentMonth);
            vm.StatusMessage = $"Подразделение «{dialog.FirstCreatedPost}» настроено! График сформирован.";
        }
    }

    public void OnCellButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is RosterCellViewModel cellVm && DataContext is MainViewModel vm)
        {
            vm.SelectCell(cellVm, _isCtrlPressed, _isShiftPressed);
        }
    }

    private async void OnAddNewPostClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialog = new CreatePostDialog();
        bool result = await dialog.ShowDialog<bool>(this);
        if (result && !string.IsNullOrWhiteSpace(dialog.CreatedPostName))
        {
            vm.RefreshPosts();
            vm.SelectedPost = dialog.CreatedPostName;
            vm.LoadMonth(vm.CurrentYear, vm.CurrentMonth);
            vm.StatusMessage = $"Смена «{dialog.CreatedPostName}» успешно создана!";

            // Предлагаем сразу настроить штат этой смены
            var staffDialog = new ManageEmployeesDialog();
            staffDialog.Initialize(dialog.CreatedPostName, vm.CurrentYear, vm.CurrentMonth);
            await staffDialog.ShowDialog<bool>(this);

            vm.RefreshPosts();
            vm.LoadMonth(vm.CurrentYear, vm.CurrentMonth);
        }
    }

    private async void OnDeletePostClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrWhiteSpace(vm.CurrentPostName))
        {
            vm.StatusMessage = "Не выбрано подразделение для удаления!";
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Удаление подразделения",
            Content = $"Вы действительно хотите безвозвратно удалить смену «{vm.CurrentPostName}» и все ее графики?",
            PrimaryButtonText = "Удалить смену",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };

        var res = await dialog.ShowAsync();
        if (res == ContentDialogResult.Primary)
        {
            vm.DeleteCurrentPost();
        }
    }

    private async void OnManageEmployeesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var dialog = new ManageEmployeesDialog();
        dialog.Initialize(vm.SelectedPost, vm.CurrentYear, vm.CurrentMonth);

        bool result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            vm.RefreshPosts();
            if (vm.AvailablePosts.Contains(dialog.CurrentSelectedPost))
            {
                vm.SelectedPost = dialog.CurrentSelectedPost;
            }

            if (dialog.ShouldRecalculateSchedule)
            {
                vm.AutoFillCurrentSchedule();
            }
            else
            {
                vm.LoadMonth(vm.CurrentYear, vm.CurrentMonth);
            }

            vm.StatusMessage = "Данные сотрудников обновлены, график пересчитан.";
        }
    }

    private async void OnAssignAbsencePeriodClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var employeeNames = vm.Rows
            .Where(r => !r.IsSubstitute)
            .Select(r => r.EmployeeName)
            .Distinct()
            .ToList();

        if (employeeNames.Count == 0)
        {
            vm.StatusMessage = "В текущем подразделении нет постоянных сотрудников!";
            return;
        }

        int maxDays = DateTime.DaysInMonth(vm.CurrentYear, vm.CurrentMonth);
        string? preselected = vm.SelectedCell?.Model.EmployeeName;
        int initialDay = vm.SelectedCell?.DayOfMonth ?? 1;

        var dialog = new AssignAbsencePeriodDialog();
        dialog.Initialize(employeeNames, maxDays, preselected, initialDay);

        bool result = await dialog.ShowDialog<bool>(this);
        if (result)
        {
            vm.ApplyRangeForEmployee(
                dialog.SelectedEmployee,
                dialog.SelectedStatusTag,
                dialog.StartDay,
                dialog.EndDay);

            // Если назначен отпуск или больничный — сразу открываем окно назначения подменного с возможностью закрыть/пропустить!
            if (dialog.SelectedStatusTag is "О" or "Б")
            {
                var existing = vm.GetExistingAbsencesInMonth();
                var subDialog = new AssignSubstituteDialog();
                subDialog.Initialize(
                    employeeNames,
                    maxDays,
                    existing,
                    dialog.SelectedEmployee,
                    dialog.StartDay,
                    dialog.EndDay,
                    dialog.SelectedStatusTag);

                bool subResult = await subDialog.ShowDialog<bool>(this);
                if (subResult && !string.IsNullOrWhiteSpace(subDialog.EnteredSubstituteFullName))
                {
                    vm.AssignSubstitute(
                        subDialog.SelectedMainEmployeeName,
                        subDialog.EnteredSubstituteFullName,
                        subDialog.StartDay,
                        subDialog.EndDay,
                        subDialog.ReasonCode);
                }
            }
        }
    }

    private async void OnAssignSubstituteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var employeeNames = vm.Rows
            .Where(r => !r.IsSubstitute)
            .Select(r => r.EmployeeName)
            .Distinct()
            .ToList();

        if (employeeNames.Count == 0)
        {
            vm.StatusMessage = "В текущем графике нет постоянных сотрудников!";
            return;
        }

        int maxDays = DateTime.DaysInMonth(vm.CurrentYear, vm.CurrentMonth);
        var existing = vm.GetExistingAbsencesInMonth();

        var dialog = new AssignSubstituteDialog();
        dialog.Initialize(employeeNames, maxDays, existing);

        bool result = await dialog.ShowDialog<bool>(this);
        if (result && !string.IsNullOrWhiteSpace(dialog.EnteredSubstituteFullName))
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
