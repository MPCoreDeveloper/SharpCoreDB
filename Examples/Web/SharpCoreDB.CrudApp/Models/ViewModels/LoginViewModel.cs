using System.ComponentModel.DataAnnotations;

namespace SharpCoreDB.CrudApp.Models.ViewModels;

/// <summary>
/// Represents login form values.
/// </summary>
public sealed record class LoginViewModel
{
    /// <summary>Gets or sets user name.</summary>
    [Required]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets password.</summary>
    [Required]
    [DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets remember-me flag.</summary>
    public bool RememberMe { get; set; }
}
