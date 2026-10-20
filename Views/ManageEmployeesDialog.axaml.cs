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

    public string CurrentSelectedPost { get; private set; } = string.Empty;

    public ManageEmployeesDialog()
    {
        InitializeComponent();

        PostFilterComboBox.SelectionChanged += OnPostFilterChanged;
        EmployeesListBox.SelectionChanged += OnEmployeeSelected;
        SaveButton.Click += OnSaveClicked;
        AddClearButton.Click += OnAddClearClicked;
        DeleteButton.Click += OnDeleteClicked;
        AddNewPostButton.Click += OnAddNewPostClicked;
        CloseButton.Click += (_, _) => Close(true);
    }

    public void Initialize(string initialPost)
    {
        CurrentSelectedPost = initialPost;
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
    }

    private void RefreshEmployees()
    {
        _allEmployees.Clear();
        _allEmployees.AddRange(DatabaseService.Instance.GetEmployees(CurrentSelectedPost));

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
            PostNameTextBox.Text = post;
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
            PostNameTextBox.Text = emp.PostName;
            InitialDayNumeric.Value = emp.InitialShiftDay;

            // Выбор схемы
            for (int i = 0; i < PatternComboBox.Items.Count; i++)
            {
                if (PatternComboBox.Items[i] is ComboBoxItem item && item.Tag?.ToString() == emp.ShiftPattern)
                {
                    PatternComboBox.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void ClearEditor()
    {
        _selectedEmployee = null;
        EditorTitleText.Text = "Новый сотрудник";
        FullNameTextBox.Text = string.Empty;
        PositionTextBox.Text = "Дежурный оператор";
        PostNameTextBox.Text = CurrentSelectedPost;

        // Предлагаем следующий свободный день смены (1..4)
        int nextDay = (_allEmployees.Count % 4) + 1;
        InitialDayNumeric.Value = nextDay;
        PatternComboBox.SelectedIndex = 0;
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
        string post = PostNameTextBox.Text?.Trim() ?? CurrentSelectedPost;
        if (string.IsNullOrWhiteSpace(post)) post = "Смена №1";

        int startDay = (int)(InitialDayNumeric.Value ?? 1);
        string pattern = "1/3";
        if (PatternComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            pattern = tag;
        }

        var emp = _selectedEmployee ?? new Employee();
        emp.FullName = name;
        emp.Position = pos;
        emp.PostName = post;
        emp.InitialShiftDay = startDay;
        emp.ShiftPattern = pattern;
        emp.IsActive = true;
        emp.IsPermanent = true;

        DatabaseService.Instance.SaveEmployee(emp);

        CurrentSelectedPost = post;
        RefreshPosts();
        RefreshEmployees();

        // Выделяем сохраненного сотрудника
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
            DatabaseService.Instance.DeleteEmployee(_selectedEmployee.Id);
            RefreshEmployees();
        }
    }

    private void OnAddNewPostClicked(object? sender, RoutedEventArgs e)
    {
        int count = DatabaseService.Instance.GetAllPostNames().Count + 1;
        string newPostName = $"Смена №{count}";

        CurrentSelectedPost = newPostName;
        PostNameTextBox.Text = newPostName;
        ClearEditor();

        // Сохраним временного сотрудника для новой смены
        var dummyEmp = new Employee
        {
            FullName = $"Оператор {count}.1",
            Position = "Дежурный оператор",
            PostName = newPostName,
            InitialShiftDay = 1,
            ShiftPattern = "1/3",
            IsActive = true,
            IsPermanent = true
        };
        DatabaseService.Instance.SaveEmployee(dummyEmp);

        RefreshPosts();
        PostFilterComboBox.SelectedItem = newPostName;
    }
}
