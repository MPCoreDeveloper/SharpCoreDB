namespace SharpCoreDB.CrudApp.Configuration;

/// <summary>
/// Represents SharpCoreDB storage settings for the MVC demo application.
/// </summary>
public sealed class SharpCoreDbAppOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "SharpCoreDb";

    /// <summary>Gets or sets single-file database path.</summary>
    public string DatabaseFilePath { get; set; } = "App_Data/sharpcore-crud.scdb";

    /// <summary>Gets or sets encryption password used to derive AES-256-GCM key.</summary>
    public string EncryptionPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets SharpCoreDB master password.</summary>
    public string MasterPassword { get; set; } = string.Empty;
}
