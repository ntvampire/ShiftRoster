using CommunityToolkit.Mvvm.ComponentModel;

namespace ShiftRoster.Models;

/// <summary>
/// Элемент сотрудника для Мастера первичной настройки
/// </summary>
public partial class WizardEmployeeItem : ObservableObject
{
    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _position = "Дежурный оператор";

    [ObservableProperty]
    private int _initialShiftDay = 1;

    [ObservableProperty]
    private string _shiftPattern = "1/3";

    public WizardEmployeeItem() { }

    public WizardEmployeeItem(string fullName, string position, int initialShiftDay, string shiftPattern = "1/3")
    {
        FullName = fullName;
        Position = position;
        InitialShiftDay = initialShiftDay;
        ShiftPattern = shiftPattern;
    }
}
