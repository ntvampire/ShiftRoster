using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class SetupWizardDialog : Window
{
    private int _currentStep = 1;
    private readonly ObservableCollection<WizardEmployeeItem> _employees = new();
    private readonly List<string> _configuredPosts = new();

    public string FirstCreatedPost { get; private set; } = string.Empty;
    public bool HasCreatedAnyPost => _configuredPosts.Count > 0;

    public SetupWizardDialog()
    {
        InitializeComponent();

        WizardEmployeesItemsControl.ItemsSource = _employees;

        NextButton.Click += OnNextClicked;
        BackButton.Click += OnBackClicked;
        AddEmployeeRowButton.Click += OnAddEmployeeRowClicked;
        AddAnotherPostButton.Click += OnAddAnotherPostClicked;
        FinishWizardButton.Click += OnFinishWizardClicked;

        string existingOrg = DatabaseService.Instance.GetOrganizationName();
        if (!string.IsNullOrWhiteSpace(existingOrg))
        {
            OrganizationNameBox.Text = existingOrg;
        }

        UpdateStepView();
    }

    private void UpdateStepView()
    {
        Step1Panel.IsVisible = (_currentStep == 1);
        Step2Panel.IsVisible = (_currentStep == 2);
        Step3Panel.IsVisible = (_currentStep == 3);

        BackButton.IsVisible = (_currentStep == 2);
        NextButton.IsVisible = (_currentStep < 3);

        if (_currentStep == 1)
        {
            StepSubtitleText.Text = "Шаг 1 из 3: Название подразделения и схема работы";
            NextButton.Content = "Далее (Сотрудники) ➔";
        }
        else if (_currentStep == 2)
        {
            string dep = DepartmentNameBox.Text?.Trim() ?? "Подразделение";
            StepSubtitleText.Text = $"Шаг 2 из 3: Сотрудники для «{dep}»";
            Step2TitleText.Text = $"Работники подразделения «{dep}»:";
            NextButton.Content = "Сохранить подразделение ➔";
        }
        else if (_currentStep == 3)
        {
            StepSubtitleText.Text = "Шаг 3 из 3: Подразделение успешно создано";
            ConfiguredPostsListText.Text = string.Join("  •  ", _configuredPosts);
        }
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentStep == 1)
        {
            string orgName = OrganizationNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(orgName))
            {
                OrganizationNameBox.Focus();
                return;
            }

            string depName = DepartmentNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(depName))
            {
                DepartmentNameBox.Focus();
                return;
            }

            DatabaseService.Instance.SetOrganizationName(orgName);

            // Инициализируем строки сотрудников, если они еще не созданы
            if (_employees.Count == 0)
            {
                string pattern = "1/3";
                if (ScheduleTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
                {
                    pattern = tag;
                }

                if (pattern == "1/3")
                {
                    _employees.Add(new WizardEmployeeItem("", "Старший смены", 1, "1/3"));
                    _employees.Add(new WizardEmployeeItem("", "Дежурный оператор", 2, "1/3"));
                    _employees.Add(new WizardEmployeeItem("", "Дежурный оператор", 3, "1/3"));
                    _employees.Add(new WizardEmployeeItem("", "Дежурный оператор", 4, "1/3"));
                }
                else
                {
                    _employees.Add(new WizardEmployeeItem("", "Оператор (День)", 1, "2/2-День"));
                    _employees.Add(new WizardEmployeeItem("", "Оператор (День)", 3, "2/2-День"));
                    _employees.Add(new WizardEmployeeItem("", "Оператор (Ночь)", 1, "2/2-Ночь"));
                    _employees.Add(new WizardEmployeeItem("", "Оператор (Ночь)", 3, "2/2-Ночь"));
                }
            }

            _currentStep = 2;
            UpdateStepView();
        }
        else if (_currentStep == 2)
        {
            string depName = DepartmentNameBox.Text?.Trim() ?? "Смена 1";
            var validEmps = _employees
                .Where(x => !string.IsNullOrWhiteSpace(x.FullName))
                .ToList();

            if (validEmps.Count == 0)
            {
                // Если пользователь не ввел ни одного имени
                return;
            }

            string pattern = "1/3";
            if (ScheduleTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                pattern = tag;
            }

            // Сохраняем сотрудников в базу данных SQLite
            int sortOrder = 1;
            foreach (var empItem in validEmps)
            {
                var emp = new Employee
                {
                    FullName = empItem.FullName.Trim(),
                    Position = string.IsNullOrWhiteSpace(empItem.Position) ? "Оператор" : empItem.Position.Trim(),
                    PostName = depName,
                    InitialShiftDay = (int)empItem.InitialShiftDay,
                    ShiftPattern = pattern,
                    SortOrder = sortOrder++,
                    IsPermanent = true,
                    IsActive = true
                };
                DatabaseService.Instance.SaveEmployee(emp);
            }

            if (string.IsNullOrEmpty(FirstCreatedPost))
            {
                FirstCreatedPost = depName;
            }

            if (!_configuredPosts.Contains(depName))
            {
                _configuredPosts.Add(depName);
            }

            SuccessMessageText.Text = $"Подразделение «{depName}» ({validEmps.Count} сотр.) успешно настроено!";

            _currentStep = 3;
            UpdateStepView();
        }
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentStep == 2)
        {
            _currentStep = 1;
            UpdateStepView();
        }
    }

    private void OnAddEmployeeRowClicked(object? sender, RoutedEventArgs e)
    {
        int nextDay = (_employees.Count % 4) + 1;
        _employees.Add(new WizardEmployeeItem("", "Дежурный оператор", nextDay, "1/3"));
    }

    public void OnDeleteEmployeeRowClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is WizardEmployeeItem item)
        {
            _employees.Remove(item);
        }
    }

    private void OnAddAnotherPostClicked(object? sender, RoutedEventArgs e)
    {
        DepartmentNameBox.Text = string.Empty;
        _employees.Clear();
        _currentStep = 1;
        UpdateStepView();
        DepartmentNameBox.Focus();
    }

    private void OnFinishWizardClicked(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
