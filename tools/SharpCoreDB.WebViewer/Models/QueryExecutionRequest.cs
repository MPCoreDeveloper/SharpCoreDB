using System.ComponentModel.DataAnnotations;

namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents bindable input for the SQL editor.
/// </summary>
public sealed class QueryExecutionRequest
{
    [Display(Name = "SQL")]
    public string Sql { get; set; } = string.Empty;

    [Display(Name = "Parameters (JSON object)")]
    public string ParametersJson { get; set; } = string.Empty;
}
