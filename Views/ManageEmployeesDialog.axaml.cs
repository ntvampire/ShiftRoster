using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class ManageEmployeesDialog : Window
{
    private readonly List<Employee> _allEmployees = new();
    private Employee? _selectedEmployee;
    private int _activeYear = DateTime.Now.Year;
    private int _activeMonth = DateTime.Now.Month;

    public string CurrentSelectedPost { get; private set; } = string.Empty;
    public bool ShouldRecalculateSchedule => RecalculateScheduleCheckBox.IsChecked == true;

    public ManageEmployeesDialog()
    {
        InitializeComponent();

        PostFilterComboBox.SelectionChanged += OnPostFilterChanged;
        EmployeesListBox.SelectionChanged += OnEmployeeSelected;
        MoveUpButton.Click += OnMoveUpClicked;
        MoveDownButton.Click += OnMoveDownClicked;
        SaveButton.Click += OnSaveClicked;
        AddClearButton.Click += OnAddClearClicked;
        DeleteButton.Click += OnDeleteClicked;
        CloseButton.Click += (_, _) => Close(true);
    }

    public void Initialize(string initialPost, int activeYear = 0, int activeMonth = 0)
    {
        CurrentSelectedPost = initialPost;
        _activeYear = activeYear > 0 ? activeYear : DateTime.Now.Year;
        _activeMonth = activeMonth > 0 ? activeMonth : DateTime.Now.Month;

        RefreshPosts();
        RefreshEmployees();
    }

    private void RefreshPosts()
    {
        var posts = DatabaseService.Instance.GetAllPostNames();
        PostFilterComboBox.ItemsSource = posts;

        if (posts.Contains(CurrentSelectedPost))
        {
            PostFilterComboBox.SelectedItem = CurrentSelectedPost;
        }
        else if (posts.Count > 0)
        {
            PostFilterComboBox.SelectedIndex = 0;
            CurrentSelectedPost = posts[0];
        }
        else
        {
            CurrentSelectedPost = string.Empty;
        }

        UpdatePatternBadge();
    }

    private void UpdatePatternBadge()
    {
        if (string.IsNullOrWhiteSpace(CurrentSelectedPost))
        {
            PostPatternBadgeText.Text = "Подразделение не выбрано";
            return;
        }

        string pat = DatabaseService.Instance.GetPostPattern(CurrentSelectedPost);
        PostPatternBadgeText.Text = pat == "1/3" 
            ? "Схема: Сутки через трое (1/3)" 
            : "Схема: 2 через 2 (по 12ч)";
    }

    private void RefreshEmployees()
    {
        _allEmployees.Clear();
        if (!string.IsNullOrWhiteSpace(CurrentSelectedPost))
        {
            _allEmployees.AddRange(DatabaseService.Instance.GetEmployees(CurrentSelectedPost));
        }

        // Гарантируем правильную нумерацию SortOrder
        for (int i = 0; i < _allEmployees.Count; i++)
        {
            _allEmployees[i].SortOrder = i + 1;
        }

        EmployeesListBox.ItemsSource = null;
        EmployeesListBox.ItemsSource = _allEmployees;

        if (_allEmployees.Count > 0)
        {
            EmployeesListBox.SelectedIndex = 0;
        }
        else
        {
            ClearEditor();
        }
    }

    private void OnPostFilterChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PostFilterComboBox.SelectedItem is string post && !string.IsNullOrWhiteSpace(post))
        {
            CurrentSelectedPost = post;
            UpdatePatternBadge();
            RefreshEmployees();
        }
    }

    private void OnEmployeeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (EmployeesListBox.SelectedItem is Employee emp)
        {
            _selectedEmployee = emp;
            EditorTitleText.Text = $"Редактирование: {emp.FullName}";
            FullNameTextBox.Text = emp.FullName;
            PositionTextBox.Text = emp.Position;
            InitialDayNumeric.Value = emp.InitialShiftDay;
        }
        else
        {
            ClearEditor();
        }
    }

    private void ClearEditor()
    {
        _selectedEmployee = null;
        EditorTitleText.Text = "Новый сотрудник";
        FullNameTextBox.Text = string.Empty;
        PositionTextBox.Text = "Дежурный оператор";

        // Предлагаем следующий свободный день смены (1..4)
        int nextDay = (_allEmployees.Count % 4) + 1;
        InitialDayNumeric.Value = nextDay;
    }

    private void OnMoveUpClicked(object? sender, RoutedEventArgs e)
    {
        int index = EmployeesListBox.SelectedIndex;
        if (index > 0 && index < _allEmployees.Count)
        {
            var item = _allEmployees[index];
            _allEmployees.RemoveAt(index);
            _allEmployees.Insert(index - 1, item);

            DatabaseService.Instance.UpdateEmployeesOrder(_allEmployees);
            DatabaseService.Instance.SetPostBaseMonth(CurrentSelectedPost, _activeYear, _activeMonth);
            RefreshEmployees();
            EmployeesListBox.SelectedIndex = index - 1;
        }
    }

    private void OnMoveDownClicked(object? sender, RoutedEventArgs e)
    {
        int index = EmployeesListBox.SelectedIndex;
        if (index >= 0 && index < _allEmployees.Count - 1)
        {
            var item = _allEmployees[index];
            _allEmployees.RemoveAt(index);
            _allEmployees.Insert(index + 1, item);

            DatabaseService.Instance.UpdateEmployeesOrder(_allEmployees);
            DatabaseService.Instance.SetPostBaseMonth(CurrentSelectedPost, _activeYear, _activeMonth);
            RefreshEmployees();
            EmployeesListBox.SelectedIndex = index + 1;
        }
    }

    private void OnAddClearClicked(object? sender, RoutedEventArgs e)
    {
        ClearEditor();
        FullNameTextBox.Focus();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        string name = FullNameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            FullNameTextBox.Focus();
            return;
        }

        string pos = PositionTextBox.Text?.Trim() ?? "Оператор";
        string post = CurrentSelectedPost;
        if (string.IsNullOrWhiteSpace(post)) post = "Смена №1";

        int startDay = (int)(InitialDayNumeric.Value ?? 1);
        string pattern = DatabaseService.Instance.GetPostPattern(post);

        var emp = _selectedEmployee ?? new Employee();
        emp.FullName = name;
        emp.Position = pos;
        emp.PostName = post;
        emp.InitialShiftDay = startDay;
        emp.ShiftPattern = pattern;
        if (emp.Id == 0)
        {
            emp.SortOrder = _allEmployees.Count + 1;
        }
        emp.IsActive = true;
        emp.IsPermanent = true;

        DatabaseService.Instance.SaveEmployee(emp);
        DatabaseService.Instance.SetPostBaseMonth(post, _activeYear, _activeMonth);

        RefreshEmployees();

        var match = _allEmployees.FirstOrDefault(x => x.FullName == name);
        if (match != null)
        {
            EmployeesListBox.SelectedItem = match;
        }
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedEmployee != null && _selectedEmployee.Id > 0)
        {
            int delId = _selectedEmployee.Id;
            DatabaseService.Instance.DeleteEmployee(delId);
            _selectedEmployee = null;
            RefreshEmployees();
        }
    }
}
