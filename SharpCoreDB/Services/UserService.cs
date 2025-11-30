using System.Text.Json;
using SharpCoreDB.Interfaces;

namespace SharpCoreDB.Services;

/// <summary>
/// Implementation of IUserService.
/// </summary>
public class UserService : IUserService
{
    private readonly ICryptoService _crypto;
    private readonly IStorage _storage;
    private readonly string _dbPath;
    private Dictionary<string, string> _users = new();

    /// <summary>
    /// Initializes a new instance of the UserService class.
    /// </summary>
    /// <param name="crypto">The crypto service.</param>
    /// <param name="storage">The storage service.</param>
    /// <param name="dbPath">The database path.</param>
    public UserService(ICryptoService crypto, IStorage storage, string dbPath)
    {
        _crypto = crypto;
        _storage = storage;
        _dbPath = dbPath;
        LoadUsers();
    }

    private void LoadUsers()
    {
        var path = Path.Combine(_dbPath, "users.json");
        var data = _storage.Read(path);
        if (data != null)
        {
            try
            {
                _users = JsonSerializer.Deserialize<Dictionary<string, string>>(data) ?? new();
            }
            catch
            {
                _users = new();
            }
        }
    }

    private void SaveUsers()
    {
        var path = Path.Combine(_dbPath, "users.json");
        _storage.Write(path, JsonSerializer.Serialize(_users));
    }

    /// <inheritdoc />
    public void CreateUser(string username, string password)
    {
        var hash = Convert.ToBase64String(_crypto.DeriveKey(password, username));
        _users[username] = hash;
        SaveUsers();
    }

    /// <inheritdoc />
    public bool Login(string username, string password)
    {
        if (!_users.ContainsKey(username)) return false;
        var hash = Convert.ToBase64String(_crypto.DeriveKey(password, username));
        if (_users[username] == hash)
        {
            CurrentUser = username;
            return true;
        }
        return false;
    }

    /// <inheritdoc />
    public string? CurrentUser { get; private set; }
}
