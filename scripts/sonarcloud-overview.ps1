<#
.SYNOPSIS
  Reads an overview of all SonarCloud organizations and projects visible to the account
  that owns the provided token, using the SonarCloud REST API.

.DESCRIPTION
  Lists every organization the account is a member of, every project inside those
  organizations, and per project the quality gate status and key measures:
  bugs, vulnerabilities, code smells, coverage, duplication, security hotspots reviewed
  and lines of code. Optionally also fetches open issue counts per type.

  Use a SonarCloud USER token (https://sonarcloud.io/account/security) - it can read
  every project the account can see, across all organizations. A "Project Analysis
  Token" is limited to a single project and is NOT suitable for this script.

.PARAMETER Token
  SonarCloud user token. When omitted, falls back to $env:SONAR_TOKEN.
  Never hard-code or commit a token.

.PARAMETER Organization
  Optional filter: only read this one organization (key). Useful for public orgs
  without a token.

.PARAMETER ProjectKey
  Optional filter: only read this one project (key).

.PARAMETER IncludeIssues
  Also fetch open issue counts per type (BUG / VULNERABILITY / CODE_SMELL).
  Adds 3 API calls per project.

.PARAMETER BaseUrl
  Defaults to https://sonarcloud.io/api

.EXAMPLE
  # Read everything the SONAR_TOKEN account can see
  .\scripts\sonarcloud-overview.ps1

.EXAMPLE
  # Only one organization, including open issue counts
  .\scripts\sonarcloud-overview.ps1 -Organization mycompany -IncludeIssues

.EXAMPLE
  # One specific project
  .\scripts\sonarcloud-overview.ps1 -ProjectKey MPCoreDeveloper_SharpCoreDB

.NOTES
  Requires the token to be set as $env:SONAR_TOKEN (user level) or passed via -Token.
  Public data (public orgs/projects) can be read without any token.
#>
[CmdletBinding()]
param(
    [string]$Token,
    [string]$Organization,
    [string]$ProjectKey,
    [switch]$IncludeIssues,
    [string]$BaseUrl = "https://sonarcloud.io/api"
)

$ErrorActionPreference = "Stop"
# Suppress Invoke-RestMethod progress noise ("Reading web response stream...").
$ProgressPreference = "SilentlyContinue"

# Token resolution order: -Token -> $env:SONAR_TOKEN -> ~\.sonarcloud\token (machine-wide, git-external).
if (-not $Token) { $Token = $env:SONAR_TOKEN }
$tokenFile = Join-Path $HOME ".sonarcloud\token"
if (-not $Token -and (Test-Path $tokenFile)) {
    $Token = (Get-Content $tokenFile -Raw).Trim()
}

function Invoke-SonarApi {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Query
    )
    $uri = "$BaseUrl/$Path"
    if ($Query) { $uri += "?$Query" }
    $headers = @{}
    if ($Token) {
        # SonarCloud accepts HTTP Basic auth with the token as username and empty password.
        $auth = "Basic " + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("$Token`:"))
        $headers["Authorization"] = $auth
    }
    return Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
}

function Get-AllPages {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Query,
        [Parameter(Mandatory)][string]$ArrayProperty
    )
    $all = @()
    $page = 1
    do {
        $resp = Invoke-SonarApi -Path $Path -Query "$Query&ps=500&p=$page"
        $batch = @($resp.$ArrayProperty)
        $all += $batch
        $page++
    } while ($batch.Count -gt 0 -and $batch.Count -eq 500)
    return $all
}

function Get-Organizations {
    if ($Organization) {
        # Validate readability cheaply via components/search (works without auth for public orgs).
        try {
            $null = Invoke-SonarApi -Path "components/search" -Query "qualifiers=TRK&organization=$Organization&ps=1"
        } catch {
            throw "Organization '$Organization' not found or not readable: $($_.Exception.Message)"
        }
        return @([pscustomobject]@{ key = $Organization; name = $Organization })
    }
    $orgs = Get-AllPages -Path "organizations/search" -Query "member=true" -ArrayProperty "organizations"
    if ($orgs.Count -eq 0) { throw "No organizations found for this token." }
    return $orgs
}

function Get-ProjectsForOrg {
    param([string]$OrgKey)
    $projects = Get-AllPages -Path "components/search" -Query "qualifiers=TRK&organization=$OrgKey" -ArrayProperty "components"
    if ($ProjectKey) {
        $projects = @($projects | Where-Object { $_.key -eq $ProjectKey })
    }
    return $projects
}

function Get-MeasureValue {
    param($Measures, [string]$Metric)
    $m = $Measures | Where-Object { $_.metric -eq $Metric } | Select-Object -First 1
    if ($m -and $null -ne $m.value) { return $m.value }
    return $null
}

function Get-OpenIssueCount {
    param([string]$ProjectKeyValue, [string]$Type)
    try {
        $resp = Invoke-SonarApi -Path "issues/search" -Query "componentKeys=$ProjectKeyValue&types=$Type&resolved=false&ps=1"
        return $resp.total
    } catch {
        return $null
    }
}

function Get-ProjectRow {
    param($OrgKey, $Project)
    $key = $Project.key
    $gateStatus = "n/a"
    $measures = @()
    try {
        $gate = Invoke-SonarApi -Path "qualitygates/project_status" -Query "projectKey=$key"
        $gateStatus = $gate.projectStatus.status
    } catch { $gateStatus = "no-data" }

    try {
        $metricKeys = "alert_status,reliability_rating,security_rating,sqale_rating,coverage," +
                      "duplicated_lines_density,bugs,vulnerabilities,code_smells," +
                      "security_hotspots_reviewed,ncloc"
        $comp = Invoke-SonarApi -Path "measures/component" -Query "component=$key&metricKeys=$metricKeys"
        $measures = @($comp.component.measures)
    } catch { $measures = @() }

    $row = [pscustomobject]@{
        Organization    = $OrgKey
        ProjectKey      = $key
        Project         = $Project.name
        QualityGate     = $gateStatus
        Bugs            = Get-MeasureValue $measures "bugs"
        Vulnerabilities = Get-MeasureValue $measures "vulnerabilities"
        CodeSmells      = Get-MeasureValue $measures "code_smells"
        Coverage        = Get-MeasureValue $measures "coverage"
        Duplication     = Get-MeasureValue $measures "duplicated_lines_density"
        HotspotsReviewed = Get-MeasureValue $measures "security_hotspots_reviewed"
        LinesOfCode     = Get-MeasureValue $measures "lines_of_code"
    }

    if ($IncludeIssues) {
        $row | Add-Member -NotePropertyName OpenBugs -NotePropertyValue (Get-OpenIssueCount $key "BUG")
        $row | Add-Member -NotePropertyName OpenVulns -NotePropertyValue (Get-OpenIssueCount $key "VULNERABILITY")
        $row | Add-Member -NotePropertyName OpenSmells -NotePropertyValue (Get-OpenIssueCount $key "CODE_SMELL")
    }

    return $row
}

# ---------------------------------------------------------------------------

Write-Host "SonarCloud overview - BaseUrl: $BaseUrl" -ForegroundColor Cyan
if ($Token) {
    Write-Host "Authenticated with SONAR_TOKEN (account token)." -ForegroundColor Green
} else {
    Write-Host "No token set - only public organizations/projects will be readable." -ForegroundColor Yellow
}

$orgs = Get-Organizations
if ($orgs.Count -eq 0) {
    Write-Warning "No organizations found. Check that the token is a user token with member access."
    exit 1
}

Write-Host "Organizations: $($orgs.Count)" -ForegroundColor Cyan
$rows = @()
foreach ($org in $orgs) {
    $projects = Get-ProjectsForOrg -OrgKey $org.key
    Write-Host "  $($org.key) ($($org.name)) - $($projects.Count) projects" -ForegroundColor Cyan
    foreach ($proj in $projects) {
        $rows += Get-ProjectRow -OrgKey $org.key -Project $proj
    }
}

Write-Host ""
Write-Host "=== SonarCloud overview ($($rows.Count) projects) ===" -ForegroundColor Cyan
$rows | Format-Table -AutoSize

# Summary per organization
Write-Host "=== Summary per organization ===" -ForegroundColor Cyan
$rows |
    Group-Object Organization |
    ForEach-Object {
        $bad = @($_.Group | Where-Object { $_.QualityGate -ne "OK" })
        $red = @($_.Group | Where-Object { $_.Bugs -and [int]$_.Bugs -gt 0 })
        $vul = @($_.Group | Where-Object { $_.Vulnerabilities -and [int]$_.Vulnerabilities -gt 0 })
        [pscustomobject]@{
            Organization      = $_.Name
            Projects          = $_.Count
            GateNotOK         = $bad.Count
            WithBugs          = $red.Count
            WithVulnerabilities = $vul.Count
        }
    } |
    Format-Table -AutoSize
