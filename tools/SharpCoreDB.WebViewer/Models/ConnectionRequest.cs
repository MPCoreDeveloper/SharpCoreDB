using System.ComponentModel.DataAnnotations;

namespace SharpCoreDB.WebViewer.Models;

/// <summary>
/// Represents bindable input for starting a web viewer session.
/// </summary>
public sealed class ConnectionRequest
{
    [Display(Name = "Connection name")]
    public string? Name { get; set; }

    [Display(Name = "Connection mode")]
    public ViewerConnectionMode ConnectionMode { get; set; } = ViewerConnectionMode.Local;

    [Display(Name = "Database path")]
    public string LocalDatabasePath { get; set; } = string.Empty;

    [Display(Name = "Storage mode")]
    public DatabaseStorageMode LocalStorageMode { get; set; } = DatabaseStorageMode.Directory;

    [Display(Name = "Read-only")]
    public bool LocalReadOnly { get; set; }

    [Display(Name = "Server host")]
    public string ServerHost { get; set; } = "localhost";

    [Range(1, 65535)]
    [Display(Name = "Server port")]
    public int ServerPort { get; set; } = 5001;

    [Display(Name = "Use TLS")]
    public bool ServerUseSsl { get; set; } = true;

    [Display(Name = "Prefer HTTP/3")]
    public bool ServerPreferHttp3 { get; set; } = true;

    [Display(Name = "Server database")]
    public string ServerDatabase { get; set; } = "master";

    [Display(Name = "Server username")]
    public string ServerUsername { get; set; } = "anonymous";

    [System.ComponentModel.DataAnnotations.DataType(System.ComponentModel.DataAnnotations.DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;
}
