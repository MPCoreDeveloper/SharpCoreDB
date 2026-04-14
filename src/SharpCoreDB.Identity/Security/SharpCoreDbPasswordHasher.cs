namespace SharpCoreDB.Identity.Security;

using System.Security.Cryptography;
using SharpCoreDB.Identity.Options;

/// <summary>
/// Provides PBKDF2 password hashing and verification for SharpCoreDB identity.
/// </summary>
public sealed class SharpCoreDbPasswordHasher(SharpCoreIdentityOptions options)
{
    private readonly SharpCoreIdentityOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <summary>
    /// Hashes a plain text password using PBKDF2.
    /// </summary>
    /// <param name="password">The plain text password.</param>
    /// <returns>Encoded hash in `pbkdf2-algorithm$iterations$salt$hash` format.</returns>
    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        ValidateHashingOptions();

        var salt = RandomNumberGenerator.GetBytes(_options.Password.SaltSize);
        var algorithm = ResolveAlgorithm(_options.Password.Algorithm);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            _options.Password.IterationCount,
            algorithm,
            _options.Password.HashSize);

        var algorithmToken = _options.Password.Algorithm switch
        {
            SharpCorePbkdf2Algorithm.Sha256 => "pbkdf2-sha256",
            SharpCorePbkdf2Algorithm.Sha512 => "pbkdf2-sha512",
            _ => throw new InvalidOperationException("Unsupported PBKDF2 algorithm.")
        };

        return string.Join('$', algorithmToken, _options.Password.IterationCount, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>
    /// Verifies a provided password against a stored hash.
    /// </summary>
    /// <param name="hashedPassword">The stored hash value.</param>
    /// <param name="providedPassword">The plain text password to verify.</param>
    /// <returns><c>true</c> when the password is valid; otherwise <c>false</c>.</returns>
    public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(providedPassword);

        var parts = hashedPassword.Split('$');
        if (parts.Length != 4)
        {
            return false;
        }

        var algorithm = parts[0] switch
        {
            "pbkdf2-sha256" => HashAlgorithmName.SHA256,
            "pbkdf2-sha512" => HashAlgorithmName.SHA512,
            _ => default
        };

        if (algorithm == default || !int.TryParse(parts[1], out var iterations) || iterations < 100_000)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expectedHash = Convert.FromBase64String(parts[3]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(providedPassword, salt, iterations, algorithm, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HashAlgorithmName ResolveAlgorithm(SharpCorePbkdf2Algorithm algorithm)
    {
        return algorithm switch
        {
            SharpCorePbkdf2Algorithm.Sha256 => HashAlgorithmName.SHA256,
            SharpCorePbkdf2Algorithm.Sha512 => HashAlgorithmName.SHA512,
            _ => throw new InvalidOperationException("Unsupported PBKDF2 algorithm.")
        };
    }

    private void ValidateHashingOptions()
    {
        if (_options.Password.IterationCount < 100_000)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.Password.IterationCount), "Iteration count must be at least 100000.");
        }

        if (_options.Password.SaltSize < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.Password.SaltSize), "Salt size must be at least 16 bytes.");
        }

        if (_options.Password.HashSize < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(_options.Password.HashSize), "Hash size must be at least 32 bytes.");
        }
    }
}
