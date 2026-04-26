# SharpCoreDB.CrudApp (v1.7.1)

`SharpCoreDB.CrudApp` is an ASP.NET Core MVC (.NET 10) showcase for:

- Encrypted single-file storage (`.scdb`, AES-256-GCM)
- Lightweight auth via `SharpCoreDB.Identity`
- Full Product CRUD with categories
- Development-only reset workflow

## Features

- Automatic startup schema initialization
  - Identity tables (`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `RoleClaims`)
  - Demo tables (`Products`, `Categories`)
- Cookie authentication with custom account pages
- Full CRUD product management protected by authorization
- Development-only `/Admin/ResetDatabase` with destructive confirmation
- Optional admin seed account after reset: `admin / Admin123!`

## Configuration

Settings are in `appsettings.json` under `SharpCoreDb` and `SharpCoreIdentity`.

> Warning: never keep production secrets in `appsettings.json`.

Use development user-secrets:

```bash
dotnet user-secrets set "SharpCoreDb:EncryptionPassword" "your-strong-password"
dotnet user-secrets set "SharpCoreDb:MasterPassword" "your-master-password"
```

## Run

```bash
dotnet restore
cd Examples/Web/SharpCoreDB.CrudApp
dotnet run
```

Browse to `https://localhost:7101`.

## Startup Flow

`Program.cs` performs:

1. DI registration for SharpCoreDB core + Identity
2. Encrypted database factory setup
3. Startup `EnsureInitializedAsync(...)`
4. EF provider `EnsureCreatedAsync()` for model mapping compatibility

## Development Reset

- Login as `admin`
- Open `/Admin/ResetDatabase`
- Confirm reset prompt

The app deletes the existing `.scdb` file, recreates schema, and reseeds the admin user.

## Main Components

- `Services/SharpCoreCrudDatabaseService.cs`
  - encrypted database construction
  - schema initialization
  - reset + seed logic
- `Services/ProductCrudService.cs`
  - async CRUD abstraction over SharpCoreDB SQL
- `Controllers/ProductsController.cs`
  - authorized MVC CRUD endpoints
- `Controllers/AccountController.cs`
  - register/login/logout

## Notes

- This sample is optimized for local demo and developer onboarding.
- For production, harden secret handling, add CSRF/security auditing policies, and implement richer authorization policies/roles.

