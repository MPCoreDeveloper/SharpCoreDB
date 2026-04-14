namespace SharpCoreDB.Identity.Security;

using System.Security.Cryptography;
using System.Text;
using SharpCoreDB.Identity.Entities;
using SharpCoreDB.Identity.Options;

internal sealed class SharpCoreDbTokenProvider(SharpCoreIdentityOptions options)
{
    private readonly SharpCoreIdentityOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public string GenerateEmailConfirmationToken(SharpCoreUser user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        return CreateToken("email-confirmation", user, now + _options.Tokens.EmailConfirmationTokenLifespan);
    }

    public string GeneratePasswordResetToken(SharpCoreUser user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        return CreateToken("password-reset", user, now + _options.Tokens.PasswordResetTokenLifespan);
    }

    public bool ValidateToken(string token, string purpose, SharpCoreUser user, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentNullException.ThrowIfNull(user);

        var tokenParts = token.Split('.');
        if (tokenParts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] providedSignature;

        try
        {
            payloadBytes = FromBase64Url(tokenParts[0]);
            providedSignature = FromBase64Url(tokenParts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var expectedSignature = ComputeSignature(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature))
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        var payloadParts = payload.Split('|');
        if (payloadParts.Length != 4)
        {
            return false;
        }

        if (!string.Equals(payloadParts[0], purpose, StringComparison.Ordinal))
        {
            return false;
        }

        if (!Guid.TryParse(payloadParts[1], out var userId) || userId != user.Id)
        {
            return false;
        }

        if (!long.TryParse(payloadParts[2], out var expiresUnixSeconds))
        {
            return false;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(expiresUnixSeconds);
        if (expires < now)
        {
            return false;
        }

        return string.Equals(payloadParts[3], user.SecurityStamp, StringComparison.Ordinal);
    }

    private string CreateToken(string purpose, SharpCoreUser user, DateTimeOffset expires)
    {
        ValidateOptions();

        var payload = $"{purpose}|{user.Id:D}|{expires.ToUnixTimeSeconds()}|{user.SecurityStamp}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var signature = ComputeSignature(payloadBytes);

        return $"{ToBase64Url(payloadBytes)}.{ToBase64Url(signature)}";
    }

    private byte[] ComputeSignature(ReadOnlySpan<byte> payloadBytes)
    {
        using var hmac = new HMACSHA256(_options.Tokens.TokenSigningKey);
        return hmac.ComputeHash(payloadBytes.ToArray());
    }

    private void ValidateOptions()
    {
        if (_options.Tokens.TokenSigningKey.Length < 32)
        {
            throw new InvalidOperationException("TokenSigningKey must be at least 32 bytes.");
        }
    }

    private static string ToBase64Url(ReadOnlySpan<byte> input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] FromBase64Url(string value)
    {
        var incoming = value
            .Replace('-', '+')
            .Replace('_', '/');

        return (incoming.Length % 4) switch
        {
            0 => Convert.FromBase64String(incoming),
            2 => Convert.FromBase64String(incoming + "=="),
            3 => Convert.FromBase64String(incoming + "="),
            _ => throw new FormatException("Invalid base64url encoding.")
        };
    }
}
