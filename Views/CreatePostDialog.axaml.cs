using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class CreatePostDialog : Window
{
    public string CreatedPostName { get; private set; } = string.Empty;

    public CreatePostDialog()
    {
        InitializeComponent();

        CreateButton.Click += OnCreateClicked;
        CancelButton.Click += (_, _) => Close(false);

        PatternComboBox.SelectionChanged += (_, _) =>
        {
            if (PatternComboBox.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            {
                EmployeeCountNumeric.Value = tag == "1/2" ? 3 : 4;
            }
        };
    }

    private void OnCreateClicked(object? sender, RoutedEventArgs e)
    {
        string postName = PostNameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(postName))
        {
            PostNameTextBox.Focus();
            return;
        }

        string pattern = "1/3";
        if (PatternComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            pattern = tag;
        }

        int count = (int)(EmployeeCountNumeric.Value ?? (pattern == "1/2" ? 3 : 4));
        if (count < 1) count = pattern == "1/2" ? 3 : 4;

        // Создаем заготовки сотрудников для нового подразделения
        for (int i = 1; i <= count; i++)
        {
            int startDay = (pattern == "1/3" || pattern == "1/2") ? i : ((i <= count / 2) ? 1 : 3);
            string role = (pattern == "1/3" || pattern == "1/2") ? (i == 1 ? "Старший смены" : "Дежурный оператор")
                                             : (i % 2 != 0 ? "Оператор (День)" : "Оператор (Ночь)");

            var emp = new Employee
            {
                FullName = $"Работник {i}",
                Position = role,
                PostName = postName,
                InitialShiftDay = startDay,
                ShiftPattern = pattern,
                SortOrder = i,
                IsPermanent = true,
                IsActive = true
            };
            DatabaseService.Instance.SaveEmployee(emp);
        }

        DatabaseService.Instance.SetPostBaseMonth(postName, DateTime.Now.Year, DateTime.Now.Month);

        CreatedPostName = postName;
        Close(true);
    }
}
