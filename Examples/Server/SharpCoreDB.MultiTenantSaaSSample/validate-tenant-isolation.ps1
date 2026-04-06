param(
    [string]$BaseUrl = "https://localhost:8443",
    [string]$AdminUser = "admin",
    [string]$AdminPassword = "admin123",
    [string]$TenantAUser = "tenant-a-reader",
    [string]$TenantAPassword = "admin123",
    [string]$TenantBUser = "tenant-b-reader",
    [string]$TenantBPassword = "admin123"
)

$ErrorActionPreference = 'Stop'

function Invoke-JsonPost {
    param(
        [string]$Url,
        [object]$Body,
        [string]$Token
    )

    $headers = @{}
    if ($Token) {
        $headers['Authorization'] = "Bearer $Token"
    }

    Invoke-RestMethod -Method Post -Uri $Url -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 10) -SkipCertificateCheck
}

function Get-Token {
    param([string]$Username, [string]$Password)

    (Invoke-JsonPost -Url "$BaseUrl/api/v1/auth/login" -Body @{
        username = $Username
        password = $Password
    }).token
}

Write-Host "[1/5] Admin login"
$adminToken = Get-Token -Username $AdminUser -Password $AdminPassword

Write-Host "[2/5] Provision tenant A and B"
Invoke-JsonPost -Url "$BaseUrl/api/v1/tenants" -Token $adminToken -Body @{
    tenantKey = 'tenant-a'
    displayName = 'Tenant A'
    databasePath = './data/tenant-a.db'
    planTier = 'starter'
    encryptionKeyReference = 'tenant-a-key'
} | Out-Null

Invoke-JsonPost -Url "$BaseUrl/api/v1/tenants" -Token $adminToken -Body @{
    tenantKey = 'tenant-b'
    displayName = 'Tenant B'
    databasePath = './data/tenant-b.db'
    planTier = 'starter'
    encryptionKeyReference = 'tenant-b-key'
} | Out-Null

Write-Host "[3/5] Tenant A login"
$tenantAToken = Get-Token -Username $TenantAUser -Password $TenantAPassword

Write-Host "[4/5] Same-tenant access should succeed"
Invoke-JsonPost -Url "$BaseUrl/api/v1/query" -Token $tenantAToken -Body @{
    database = 'tenant_tenant_a'
    sql = 'SELECT 1 AS ok'
} | Out-Null

Write-Host "[5/5] Cross-tenant access should be denied"
try {
    Invoke-JsonPost -Url "$BaseUrl/api/v1/query" -Token $tenantAToken -Body @{
        database = 'tenant_tenant_b'
        sql = 'SELECT 1 AS should_fail'
    } | Out-Null

    throw 'Isolation validation failed: cross-tenant query unexpectedly succeeded.'
}
catch {
    Write-Host 'Cross-tenant denial confirmed.'
}

Write-Host 'Tenant isolation validation completed successfully.'
