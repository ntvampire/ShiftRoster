using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShiftRoster.Models;
using ShiftRoster.Services;

namespace ShiftRoster.Views;

public partial class ManageSubstitutesDialog : Window
{
    private readonly List<SubstituteRecord> _allSubstitutes = new();
    private SubstituteRecord? _selectedSubstitute;
    public bool HasChanges { get; private set; }

    public ManageSubstitutesDialog()
    {
        InitializeComponent();

        SubstitutesListBox.SelectionChanged += OnSubstituteSelected;
        SearchTextBox.KeyUp += (_, _) => ApplyFilter();
        SaveButton.Click += OnSaveClicked;
        AddClearButton.Click += OnAddClearClicked;
        DeleteButton.Click += OnDeleteClicked;
        CloseButton.Click += (_, _) => Close(HasChanges);

        PosChipOperator.Click += (_, _) => SetPosition("Оператор (подмена)");
        PosChipDuty.Click += (_, _) => SetPosition("Дежурный оператор");
        PosChipSenior.Click += (_, _) => SetPosition("Старший смены");
        PosChipSecurity.Click += (_, _) => SetPosition("Охранник");
    }

    public void Initialize()
    {
        RefreshSubstitutes();
    }

    private void SetPosition(string pos)
    {
        PositionTextBox.Text = pos;
        PositionTextBox.Focus();
    }

    private void RefreshSubstitutes()
    {
        _allSubstitutes.Clear();
        _allSubstitutes.AddRange(DatabaseService.Instance.GetSubstitutes());

        TotalSubstitutesBadgeText.Text = $"Всего: {_allSubstitutes.Count} чел.";

        ApplyFilter();

        if (_selectedSubstitute != null)
        {
            var match = _allSubstitutes.FirstOrDefault(s => s.Id == _selectedSubstitute.Id);
            if (match != null)
            {
                SubstitutesListBox.SelectedItem = match;
            }
            else
            {
                ClearEditor();
            }
        }
        else if (_allSubstitutes.Count > 0 && SubstitutesListBox.SelectedItem == null)
        {
            SubstitutesListBox.SelectedIndex = 0;
        }
        else if (_allSubstitutes.Count == 0)
        {
            ClearEditor();
        }
    }

    private void ApplyFilter()
    {
        string query = SearchTextBox.Text?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allSubstitutes
            : _allSubstitutes.Where(s => s.FullName.Contains(query, StringComparison.OrdinalIgnoreCase)
                                     || s.Position.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        SubstitutesListBox.ItemsSource = null;
        SubstitutesListBox.ItemsSource = filtered;
    }

    private void OnSubstituteSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SubstitutesListBox.SelectedItem is SubstituteRecord sub)
        {
            _selectedSubstitute = sub;
            EditorTitleText.Text = $"Редактирование: {sub.FullName}";
            FullNameTextBox.Text = sub.FullName;
            PositionTextBox.Text = sub.Position;
            ShowValidation(null);
            DeleteButton.IsEnabled = true;
        }
        else
        {
            ClearEditor();
        }
    }

    private void ClearEditor()
    {
        _selectedSubstitute = null;
        EditorTitleText.Text = "Новый подменный";
        FullNameTextBox.Text = string.Empty;
        PositionTextBox.Text = "Оператор (подмена)";
        ShowValidation(null);
        DeleteButton.IsEnabled = false;
    }

    private void ShowValidation(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            ValidationMessageText.IsVisible = false;
            ValidationMessageText.Text = string.Empty;
        }
        else
        {
            ValidationMessageText.IsVisible = true;
            ValidationMessageText.Text = $"⚠️ {message}";
        }
    }

    private void OnAddClearClicked(object? sender, RoutedEventArgs e)
    {
        SubstitutesListBox.SelectedItem = null;
        ClearEditor();
        FullNameTextBox.Focus();
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        string name = FullNameTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Укажите ФИО подменного сотрудника.");
            FullNameTextBox.Focus();
            return;
        }

        string pos = PositionTextBox.Text?.Trim() ?? "Оператор (подмена)";
        if (string.IsNullOrWhiteSpace(pos)) pos = "Оператор (подмена)";

        if (_selectedSubstitute != null)
        {
            // Проверяем уникальность ФИО при переименовании
            if (DatabaseService.Instance.SubstituteExists(name, _selectedSubstitute.Id))
            {
                ShowValidation($"Сотрудник «{name}» уже зарегистрирован в базе!");
                return;
            }

            DatabaseService.Instance.UpdateSubstitute(_selectedSubstitute.Id, _selectedSubstitute.FullName, name, pos);
            StatusMessageText.Text = $"Данные подменного «{name}» успешно обновлены.";
            HasChanges = true;
        }
        else
        {
            // Проверяем уникальность ФИО при добавлении
            if (DatabaseService.Instance.SubstituteExists(name))
            {
                ShowValidation($"Сотрудник «{name}» уже есть в базе подменных!");
                return;
            }

            DatabaseService.Instance.SaveSubstitute(name, pos);
            StatusMessageText.Text = $"Подменный работник «{name}» добавлен в общую базу.";
            HasChanges = true;
        }

        ShowValidation(null);
        RefreshSubstitutes();

        var match = _allSubstitutes.FirstOrDefault(s => s.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            SubstitutesListBox.SelectedItem = match;
        }
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_selectedSubstitute == null) return;

        string deletedName = _selectedSubstitute.FullName;
        DatabaseService.Instance.DeleteSubstitute(_selectedSubstitute.Id);
        StatusMessageText.Text = $"Подменный «{deletedName}» удален из базы.";
        HasChanges = true;

        ClearEditor();
        RefreshSubstitutes();
    }
}
