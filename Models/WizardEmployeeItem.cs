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
    private decimal _initialShiftDay = 1m;

    [ObservableProperty]
    private string _shiftPattern = "1/3";

    public WizardEmployeeItem() { }

    public WizardEmployeeItem(string fullName, string position, decimal initialShiftDay, string shiftPattern = "1/3")
    {
        FullName = fullName;
        Position = position;
        InitialShiftDay = initialShiftDay;
        ShiftPattern = shiftPattern;
    }
}
