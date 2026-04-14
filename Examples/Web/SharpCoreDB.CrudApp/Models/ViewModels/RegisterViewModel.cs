using System.ComponentModel.DataAnnotations;

namespace SharpCoreDB.CrudApp.Models.ViewModels;

/// <summary>
/// Represents registration form values.
/// </summary>
public sealed record class RegisterViewModel
{
    /// <summary>Gets or sets username.</summary>
    [Required]
    [StringLength(80)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets email.</summary>
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets full name.</summary>
    [Required]
    [StringLength(200)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Gets or sets birth date.</summary>
    [Required]
    [DataType(System.ComponentModel.DataAnnotations.DataType.Date)]
    public DateOnly BirthDate { get; set; }

    /// <summary>Gets or sets password.</summary>
    [Required]
    [DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets password confirmation.</summary>
    [Required]
    [DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
