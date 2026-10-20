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

        if (DataContext is not MainViewModel vm) return;

        // Если зажат Ctrl или Alt — не перехватываем (оставляем системные команды)
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            return;

        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or AutoCompleteBox)
            return;

        if (vm.SelectedCells.Count == 0 && vm.SelectedCell == null)
            return;

        bool handled = false;

        switch (e.Key)
        {
            // Больничный лист (Б): русская клавиша 'Б' (OemComma) или английская 'B'
            case Key.OemComma:
            case Key.B:
                vm.ApplyShift("Б");
                handled = true;
                break;

            // Отпуск (О): русская клавиша 'О' (Key.J) или английская 'O'
            case Key.J:
            case Key.O:
                vm.ApplyShift("О");
                handled = true;
                break;

            // Выходной день (В): русская 'В' (Key.D), английская 'V'/'W', либо Delete/Backspace
            case Key.D:
            case Key.V:
            case Key.W:
            case Key.Delete:
            case Key.Back:
                vm.ApplyShift("В");
                handled = true;
                break;

            // Смены по цифрам 1 и 8 (для 1/3) или 1 и 2 (для 2/2)
            case Key.D1:
            case Key.NumPad1:
                if (vm.CurrentPostPattern == "2/2")
                    vm.ApplyShift("Д12");
                else
                    vm.ApplyShift("16");
                handled = true;
                break;

            case Key.D8:
            case Key.NumPad8:
                if (vm.CurrentPostPattern == "1/3")
                {
                    vm.ApplyShift("8");
                    handled = true;
                }
                break;

            case Key.D2:
            case Key.NumPad2:
                if (vm.CurrentPostPattern == "2/2")
                {
                    vm.ApplyShift("Н12");
                    handled = true;
                }
                else
                {
                    vm.ApplyShift("8");
                    handled = true;
                }
                break;

            // Русская 'Д' (Key.L) для режима 2/2 (Дневная 12ч)
            case Key.L:
                if (vm.CurrentPostPattern == "2/2")
                {
                    vm.ApplyShift("Д12");
                    handled = true;
                }
                break;

            // Русская 'Н' (Key.Y) для режима 2/2 (Ночная 12ч)
            case Key.Y:
                if (vm.CurrentPostPattern == "2/2")
                {
                    vm.ApplyShift("Н12");
                    handled = true;
                }
                break;
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (DataContext is not MainViewModel vm) return;

        var focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox or AutoCompleteBox)
            return;

        if (vm.SelectedCells.Count == 0 && vm.SelectedCell == null)
            return;

        if (string.IsNullOrEmpty(e.Text)) return;

        string charTyped = e.Text.Trim();
        bool handled = false;

        if (string.Equals(charTyped, "б", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(charTyped, "b", StringComparison.OrdinalIgnoreCase))
        {
            vm.ApplyShift("Б");
            handled = true;
        }
        else if (string.Equals(charTyped, "о", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(charTyped, "o", StringComparison.OrdinalIgnoreCase))
        {
            vm.ApplyShift("О");
            handled = true;
        }
        else if (string.Equals(charTyped, "в", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(charTyped, "v", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(charTyped, "w", StringComparison.OrdinalIgnoreCase))
        {
            vm.ApplyShift("В");
            handled = true;
        }
        else if (string.Equals(charTyped, "д", StringComparison.OrdinalIgnoreCase) && vm.CurrentPostPattern == "2/2")
        {
            vm.ApplyShift("Д12");
            handled = true;
        }
        else if (string.Equals(charTyped, "н", StringComparison.OrdinalIgnoreCase) && vm.CurrentPostPattern == "2/2")
        {
            vm.ApplyShift("Н12");
            handled = true;
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl) _isCtrlPressed = false;
        if (e.Key is Key.LeftShift or Key.RightShift) _isShiftPressed = false;
    }

    private bool _isShowingSubstituteDialog;

    private async void OnMainWindowLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.AbsenceAssigned -= OnAbsenceAssigned;
            vm.AbsenceAssigned += OnAbsenceAssigned;

            if (vm.ShouldRunWizardOnStartup)
            {
                await OpenSetupWizardAsync(vm);
            }
        }
    }

    private async void OnAbsenceAssigned(AbsenceAssignedEventArgs args)
    {
        if (_isShowingSubstituteDialog || DataContext is not MainViewModel vm) return;

        var employeeNames = vm.Rows
            .Where(r => !r.IsSubstitute)
            .Select(r => r.EmployeeName)
            .Distinct()
            .ToList();

        if (employeeNames.Count == 0) return;

        _isShowingSubstituteDialog = true;
        try
        {
            int maxDays = DateTime.DaysInMonth(vm.CurrentYear, vm.CurrentMonth);
            var existing = vm.GetExistingAbsencesInMonth();

            var subDialog = new AssignSubstituteDialog();
            subDialog.Initialize(
                employeeNames,
                maxDays,
                existing,
                preselectedEmployee: args.EmployeeName,
                preselectedStartDay: args.StartDay,
                preselectedEndDay: args.EndDay,
                preselectedReason: args.AbsenceCode);

            bool result = await subDialog.ShowDialog<bool>(this);
            if (result && !string.IsNullOrWhiteSpace(subDialog.EnteredSubstituteFullName))
            {
                vm.AssignSubstitute(
                    subDialog.SelectedMainEmployeeName,
                    subDialog.EnteredSubstituteFullName,
                    subDialog.EnteredSubstitutePosition,
                    subDialog.StartDay,
                    subDialog.EndDay,
                    subDialog.ReasonCode);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка в диалоге назначения подменного: {ex.Message}");
        }
        finally
        {
            _isShowingSubstituteDialog = false;
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

    public void OnCellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not RosterCellViewModel cellVm || DataContext is not MainViewModel vm)
            return;

        var point = e.GetCurrentPoint(btn);
        if (point.Properties.IsRightButtonPressed)
        {
            // Если правый клик по ячейке, которая еще не в списке выделенных:
            // сбрасываем предыдущие выделения и выделяем только ее.
            // Если ячейка уже входит в диапазон выделения — сохраняем весь выделенный диапазон!
            if (!vm.SelectedCells.Contains(cellVm))
            {
                vm.SelectCell(cellVm, isCtrlPressed: false, isShiftPressed: false);
            }
        }
    }

    public void OnCellContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not RosterCellViewModel cellVm || DataContext is not MainViewModel vm)
            return;

        // Если ячейка еще не выделена, гарантируем ее выделение
        if (!vm.SelectedCells.Contains(cellVm))
        {
            vm.SelectCell(cellVm, isCtrlPressed: false, isShiftPressed: false);
        }

        var menu = new ContextMenu();
        var items = new System.Collections.Generic.List<object>();

        // 1. Фиксированные статусы отсутствия и отдыха
        var itemSick = new MenuItem 
        { 
            Header = "🏥 Больничный лист (Б)",
            InputGesture = new KeyGesture(Key.B)
        };
        itemSick.Click += (_, _) => vm.ApplyShift("Б");
        items.Add(itemSick);

        var itemVacation = new MenuItem 
        { 
            Header = "🏖️ Отпуск (О)",
            InputGesture = new KeyGesture(Key.O)
        };
        itemVacation.Click += (_, _) => vm.ApplyShift("О");
        items.Add(itemVacation);

        var itemOff = new MenuItem 
        { 
            Header = "⚪ Выходной день (В)",
            InputGesture = new KeyGesture(Key.Delete)
        };
        itemOff.Click += (_, _) => vm.ApplyShift("В");
        items.Add(itemOff);

        items.Add(new Separator());

        // 2. Рабочие смены согласно выбранному режиму подразделения
        if (vm.CurrentPostPattern == "2/2")
        {
            var itemDay12 = new MenuItem 
            { 
                Header = "☀️ 12ч Дневная смена (Д12)",
                InputGesture = new KeyGesture(Key.D1)
            };
            itemDay12.Click += (_, _) => vm.ApplyShift("Д12");
            items.Add(itemDay12);

            var itemNight12 = new MenuItem 
            { 
                Header = "🌙 12ч Ночная смена (Н12)",
                InputGesture = new KeyGesture(Key.D2)
            };
            itemNight12.Click += (_, _) => vm.ApplyShift("Н12");
            items.Add(itemNight12);
        }
        else
        {
            var item16 = new MenuItem 
            { 
                Header = "⏱️ 16ч Заступление на смену",
                InputGesture = new KeyGesture(Key.D1)
            };
            item16.Click += (_, _) => vm.ApplyShift("16");
            items.Add(item16);

            var item8 = new MenuItem 
            { 
                Header = "⏱️ 8ч Сдача смены утром",
                InputGesture = new KeyGesture(Key.D8)
            };
            item8.Click += (_, _) => vm.ApplyShift("8");
            items.Add(item8);
        }

        menu.ItemsSource = items;
        btn.ContextMenu = menu;
        menu.Open(btn);
        e.Handled = true;
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
                        subDialog.EnteredSubstitutePosition,
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
                dialog.EnteredSubstitutePosition,
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
