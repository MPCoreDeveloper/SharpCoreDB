using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace SharpCoreDB.Viewer.Models;

/// <summary>
/// Represents one SQL editor tab and its latest result state.
/// </summary>
public sealed partial class QueryEditorTab(string title) : ObservableObject
{
    [ObservableProperty]
    private string _title = title;

    [ObservableProperty]
    private string _queryText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<QueryResultRow> _queryResults = [];

    [ObservableProperty]
    private List<string> _resultColumns = [];

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private string _statusMessage = string.Empty;
}
