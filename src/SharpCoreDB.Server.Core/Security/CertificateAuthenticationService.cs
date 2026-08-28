// <copyright file="CertificateAuthenticationService.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpCoreDB.Server.Core.Security;

/// <summary>
/// Validates client certificates for mutual TLS authentication.
/// Maps certificate thumbprints to <see cref="DatabaseRole"/> via configuration,
/// falling back to the certificate Subject CN for identity.
/// C# 14: Primary constructor, pattern matching, collection expressions.
/// </summary>
public sealed class CertificateAuthenticationService(
    IOptions<ServerConfiguration> configuration,
    ILogger<CertificateAuthenticationService> logger)
{
    private readonly ServerConfiguration _config = configuration.Value;
    private readonly ILogger<CertificateAuthenticationService> _logger = logger;

    /// <summary>
    /// Validates a client certificate and maps it to a <see cref="ClaimsPrincipal"/> with role claims.
    /// </summary>
    /// <param name="certificate">The client certificate presented during TLS handshake.</param>
    /// <returns>Authentication result containing the principal or an error message.</returns>
    public CertificateAuthResult ValidateAndMapCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (!_config.Security.EnableMutualTls)
        {
            return CertificateAuthResult.Failed("Mutual TLS is not enabled");
        }

        // Validate certificate is not expired
        var now = DateTimeOffset.UtcNow;
        if (certificate.NotAfter.ToUniversalTime() < now)
        {
            _logger.LogWarning("Client certificate expired: Subject={Subject}, Expired={Expiry}",
                certificate.Subject, certificate.NotAfter);
            return CertificateAuthResult.Failed("Client certificate has expired");
        }

        if (certificate.NotBefore.ToUniversalTime() > now)
        {
            _logger.LogWarning("Client certificate not yet valid: Subject={Subject}, NotBefore={NotBefore}",
                certificate.Subject, certificate.NotBefore);
            return CertificateAuthResult.Failed("Client certificate is not yet valid");
        }

        // Validate against CA certificate if configured
        if (!string.IsNullOrWhiteSpace(_config.Security.ClientCaCertificatePath))
        {
            if (!ValidateCertificateChain(certificate))
            {
                return CertificateAuthResult.Failed("Client certificate chain validation failed");
            }
        }

        // Extract identity from certificate
        var username = ExtractUsername(certificate);
        var thumbprint = certificate.Thumbprint;

        // Map thumbprint to role via configuration
        var role = ResolveRole(thumbprint);

        _logger.LogInformation(
            "Client certificate authenticated: User={Username}, Thumbprint={Thumbprint}, Role={Role}",
            username, thumbprint, role);

        var principal = BuildPrincipal(username, thumbprint, role);
        return CertificateAuthResult.Succeeded(principal, role);
    }

    /// <summary>
    /// Resolves the role for a certificate thumbprint.
    /// Returns the configured role for a matching thumbprint, or <see cref="DatabaseRole.Reader"/> as default.
    /// </summary>
    /// <param name="thumbprint">Certificate SHA-1 thumbprint (hex).</param>
    /// <returns>The mapped role.</returns>
    public DatabaseRole ResolveRole(string thumbprint)
    {
        ArgumentNullException.ThrowIfNull(thumbprint);

        var mapping = _config.Security.CertificateRoleMappings
            .FirstOrDefault(m => m.Thumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase));

        if (mapping is null)
        {
            return DatabaseRole.Reader;
        }

        return mapping.Role.ToLowerInvariant() switch
        {
            "admin" => DatabaseRole.Admin,
            "writer" => DatabaseRole.Writer,
            "reader" => DatabaseRole.Reader,
            _ => DatabaseRole.Reader,
        };
    }

    private bool ValidateCertificateChain(X509Certificate2 certificate)
    {
        try
        {
            var caPath = _config.Security.ClientCaCertificatePath;
            if (!File.Exists(caPath))
            {
                _logger.LogError("Client CA certificate not found at '{Path}'", caPath);
                return false;
            }

            using var caCert = X509CertificateLoader.LoadCertificateFromFile(caPath);
            using var chain = new X509Chain();

            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(caCert);

            var isValid = chain.Build(certificate);

            if (!isValid)
            {
                foreach (var status in chain.ChainStatus)
                {
                    _logger.LogWarning("Certificate chain error: {Status} — {Info}",
                        status.Status, status.StatusInformation);
                }
            }

            // Verify the root/issuer is actually our trusted CA
            if (isValid)
            {
                var rootThumbprint = chain.ChainElements[^1].Certificate.Thumbprint;
                if (!rootThumbprint.Equals(caCert.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Certificate chain root {RootThumbprint} does not match configured CA {CaThumbprint}",
                        rootThumbprint, caCert.Thumbprint);
                    return false;
                }
            }

            return isValid;
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Certificate chain validation failed due to cryptographic error");
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Certificate chain validation failed due to I/O error");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate chain validation failed");
            return false;
        }
    }

    private static string ExtractUsername(X509Certificate2 certificate)
    {
        // Try to extract identity from Subject Alternative Name (SAN)
        var san = certificate.Extensions["2.5.29.17"];
        if (san is not null)
        {
            var sanText = san.Format(multiLine: true);
            // Parse SAN formatted string for email or DNS entries
            foreach (var line in sanText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("RFC822 Name=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["RFC822 Name=".Length..];
                }

                if (trimmed.StartsWith("DNS Name=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["DNS Name=".Length..];
                }
            }
        }

        // Fall back to CN from Subject
        var subject = certificate.Subject;
        var cnStart = subject.IndexOf("CN=", StringComparison.OrdinalIgnoreCase);
        if (cnStart >= 0)
        {
            var cn = subject[(cnStart + 3)..];
            var commaIdx = cn.IndexOf(',');
            return commaIdx >= 0 ? cn[..commaIdx].Trim() : cn.Trim();
        }

        return certificate.Thumbprint;
    }

    private static ClaimsPrincipal BuildPrincipal(string username, string thumbprint, DatabaseRole role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, username),
            new(ClaimTypes.AuthenticationMethod, "X509"),
            new("certificate_thumbprint", thumbprint),
            new(ClaimTypes.Role, role.ToString().ToLowerInvariant()),
        };

        var identity = new ClaimsIdentity(claims, "Certificate");
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>
/// Result of client certificate authentication.
/// </summary>
public sealed class CertificateAuthResult
{
    /// <summary>Whether authentication succeeded.</summary>
    public bool IsAuthenticated { get; private init; }

    /// <summary>Authenticated principal (only set on success).</summary>
    public ClaimsPrincipal? Principal { get; private init; }

    /// <summary>Authenticated user's role (only set on success).</summary>
    public DatabaseRole Role { get; private init; }

    /// <summary>Error message (only set on failure).</summary>
    public string? ErrorMessage { get; private init; }

    /// <summary>Creates a successful result.</summary>
    public static CertificateAuthResult Succeeded(ClaimsPrincipal principal, DatabaseRole role) => new()
    {
        IsAuthenticated = true,
        Principal = principal,
        Role = role,
    };

    /// <summary>Creates a failed result.</summary>
    public static CertificateAuthResult Failed(string errorMessage) => new()
    {
        IsAuthenticated = false,
        ErrorMessage = errorMessage,
    };
}
