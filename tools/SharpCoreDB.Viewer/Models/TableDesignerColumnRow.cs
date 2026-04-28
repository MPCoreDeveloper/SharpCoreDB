using CommunityToolkit.Mvvm.ComponentModel;

namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Represents one editable column row in the table designer dialog.
/// </summary>
public sealed partial class TableDesignerColumnRow : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _type = "TEXT";

    [ObservableProperty]
    private bool _isNullable = true;

    [ObservableProperty]
    private bool _isPrimaryKey;
}
