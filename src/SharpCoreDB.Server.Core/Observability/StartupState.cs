// <copyright file="StartupState.cs" company="MPCoreDeveloper">
// Copyright (c) 2026 MPCoreDeveloper and GitHub Copilot. All rights reserved.
// Licensed under the MIT License.
// </copyright>

namespace SharpCoreDB.Server.Core.Observability;

/// <summary>
/// Tracks server startup readiness for health and smoke test probes.
/// </summary>
public sealed class StartupState
{
    private readonly Lock _stateLock = new();
    private bool _isReady;
    private string? _errorMessage;

    /// <summary>
    /// Gets a value indicating whether startup completed successfully.
    /// </summary>
    public bool IsReady
    {
        get
        {
            lock (_stateLock)
            {
                return _isReady;
            }
        }
    }

    /// <summary>
    /// Gets the startup failure message when initialization does not complete.
    /// </summary>
    public string? ErrorMessage
    {
        get
        {
            lock (_stateLock)
            {
                return _errorMessage;
            }
        }
    }

    /// <summary>
    /// Marks the server as ready.
    /// </summary>
    public void MarkReady()
    {
        lock (_stateLock)
        {
            _isReady = true;
            _errorMessage = null;
        }
    }

    /// <summary>
    /// Marks the server startup as failed.
    /// </summary>
    /// <param name="errorMessage">The startup failure message.</param>
    public void MarkFailed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        lock (_stateLock)
        {
            _isReady = false;
            _errorMessage = errorMessage;
        }
    }
}
