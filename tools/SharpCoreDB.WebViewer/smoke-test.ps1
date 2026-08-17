$ErrorActionPreference = 'Stop'

Write-Host 'Starting WebViewer for smoke test...'
$p = Start-Process dotnet -ArgumentList 'run --project tools/SharpCoreDB.WebViewer/SharpCoreDB.WebViewer.csproj --no-build' -PassThru -WindowStyle Hidden

try {
    Start-Sleep -Seconds 10

    $portOpen = $false
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        try {
            $tcp = New-Object System.Net.Sockets.TcpClient
            $result = $tcp.BeginConnect('localhost', 5443, $null, $null)
            $connected = $result.AsyncWaitHandle.WaitOne(2000)
            if ($connected) {
                $tcp.EndConnect($result)
                $portOpen = $true
                $tcp.Close()
                break
            }
            $tcp.Close()
        } catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if ($portOpen) {
        Write-Host "PORT 5443 OPEN - WebViewer is listening (smoke test PASS)"

        # Layout check: SSMS-style left sidebar should be present; right panel removed.
        # Self-signed dev certificate handling that works on both PowerShell editions:
        # - Windows PowerShell 5.1: Invoke-WebRequest honors ServicePointManager, but a scriptblock
        #   validation callback FAILS ("no Runspace available" on the TLS handshake thread), so a
        #   compiled C# callback is required. TLS 1.2 is enabled explicitly for older .NET defaults.
        # - PowerShell 7+: Invoke-WebRequest uses HttpClient and requires -SkipCertificateCheck.
        try {
            $requestParams = @{
                Uri             = 'https://localhost:5443/'
                TimeoutSec      = 15
                UseBasicParsing = $true
            }

            $certCallbackInstalled = $false
            if ($PSVersionTable.PSEdition -eq 'Core') {
                $requestParams['SkipCertificateCheck'] = $true
            } else {
                if (-not ('SmokeTestCertPolicy' -as [type])) {
                    Add-Type -TypeDefinition @'
using System.Net;
public static class SmokeTestCertPolicy
{
    public static void TrustAllCertificates()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback =
            (sender, certificate, chain, sslPolicyErrors) => true;
    }
}
'@
                }
                [SmokeTestCertPolicy]::TrustAllCertificates()
                $certCallbackInstalled = $true
            }

            try {
                $page = Invoke-WebRequest @requestParams -SessionVariable scdbSession

                $html = $page.Content
                if ($html -match 'scdb-group-connect' -and $html -notmatch 'scdb-right-panel') {
                    Write-Host 'PASS: Left sidebar (Connections/Saved/History) + full-width workspace confirmed.'
                } else {
                    Write-Host 'WARN: Page loaded but SSMS layout markers missing.'
                }

                # The first GET auto-connects to the default "scdb" database.
                if ($html -match 'scdb-toolbar__connection--connected') {
                    Write-Host 'PASS: First GET auto-connected to the default database.'
                } else {
                    Write-Host 'WARN: Page did not render in connected state.'
                }

                # Feature markers: create-db dialog (CreateDatabase handler), busy overlay, DDL panel, context-menu DDL item.
                if ($html -match 'handler=CreateDatabase') {
                    Write-Host 'PASS: Create Database dialog posts to the CreateDatabase handler.'
                } else {
                    Write-Host 'WARN: CreateDatabase handler marker missing.'
                }
                if ($html -match 'scdb-busy-overlay') {
                    Write-Host 'PASS: Busy overlay markup present (database creation progress feedback).'
                } else {
                    Write-Host 'WARN: Busy overlay markup missing.'
                }
                if ($html -match 'scdb-sidebar__meta-ddl-pre' -and $html -match 'CREATE TABLE') {
                    Write-Host 'PASS: Table DDL panel renders a CREATE TABLE statement.'
                } else {
                    Write-Host 'WARN: Table DDL panel missing or empty.'
                }
                if ($html -match 'ctx-ddl') {
                    Write-Host 'PASS: Context-menu "Script Table (CREATE)" item present.'
                } else {
                    Write-Host 'WARN: Context-menu DDL item missing.'
                }
                # The password hint is only rendered while the well-known default password is in use;
                # a customized DefaultDatabasePassword must never be rendered into the page.
                $appsettings = Get-Content (Join-Path $PSScriptRoot 'appsettings.json') -Raw | ConvertFrom-Json
                $configuredPassword = $appsettings.WebViewer.DefaultDatabasePassword
                if ($configuredPassword -eq 'scdb') {
                    if ($html -match 'default password') {
                        Write-Host 'PASS: Built-in database password hint rendered in Database Actions group.'
                    } else {
                        Write-Host 'WARN: Built-in database password hint missing (default password in use).'
                    }
                } else {
                    if ($html -match 'default password') {
                        Write-Host 'WARN: Password hint rendered while a custom password is configured.'
                    } else {
                        Write-Host 'PASS: Password hint correctly hidden (custom DefaultDatabasePassword configured).'
                    }
                }

                # End-to-end POST: execute a query in the same session. This guards against
                # regressions of the "Connect to a database before executing SQL." bug, where
                # POST handlers evaluated IsConnected before the session state was loaded.
                $tokenMatch = [regex]::Match($html, 'name="__RequestVerificationToken" type="hidden" value="([^"]+)"')
                if ($tokenMatch.Success) {
                    $postParams = @{
                        Uri             = 'https://localhost:5443/?handler=ExecuteQuery'
                        Method          = 'Post'
                        WebSession      = $scdbSession
                        TimeoutSec      = 30
                        UseBasicParsing = $true
                        Body            = @{
                            '__RequestVerificationToken' = $tokenMatch.Groups[1].Value
                            'Query.Sql'                  = 'SELECT COUNT(*) AS TotalRows FROM welcome;'
                            'Query.ParametersJson'       = ''
                            'SelectedTable'              = 'welcome'
                            'ActiveQueryTabId'           = ''
                            'QueryTabsStateJson'         = ''
                        }
                    }
                    if ($PSVersionTable.PSEdition -eq 'Core') {
                        $postParams['SkipCertificateCheck'] = $true
                    }

                    try {
                        $postResponse = Invoke-WebRequest @postParams
                        $postHtml = $postResponse.Content
                        if ($postHtml -match 'Connect to a database before executing SQL') {
                            Write-Host 'FAIL: ExecuteQuery POST returned the not-connected error while connected.'
                        } elseif ($postHtml -match 'TotalRows') {
                            Write-Host 'PASS: ExecuteQuery POST ran against the connected session and returned a result grid.'
                        } else {
                            Write-Host 'WARN: ExecuteQuery POST returned without an error but no result grid was detected.'
                        }
                    } catch {
                        Write-Host "FAIL: ExecuteQuery POST failed: $($_.Exception.Message)"
                    }
                } else {
                    Write-Host 'WARN: No antiforgery token found; ExecuteQuery POST check skipped.'
                }
            } finally {
                if ($certCallbackInstalled) {
                    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $null
                }
            }
        } catch {
            Write-Host "FAIL: HTTPS layout check could not run: $($_.Exception.Message)"
        }
    } else {
        Write-Host 'Port 5443 did not open - WebViewer failed to start (smoke test FAIL)'
    }
} finally {
    if (-not $p.HasExited) {
        # Kill the whole process tree: 'dotnet run' hosts the app in a child process,
        # and killing only the parent can leave the app listening on port 5443,
        # which would make the next smoke test run pass against a stale server.
        & taskkill /PID $p.Id /T /F 2>$null | Out-Null
        if (-not $p.HasExited) {
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 800
    Write-Host 'Smoke test complete.'
}
