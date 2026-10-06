param(
  [string]$ApiBaseUrl = "https://localhost:44333"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $root "src\PayOrch.Api\appsettings.Development.json"
if (-not (Test-Path $configPath)) { $configPath = Join-Path $root "src\PayOrch.Api\appsettings.json" }
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$admin = $config.BootstrapSuperAdmin

if (-not $admin.Enabled) { throw "BootstrapSuperAdmin is disabled in $configPath." }
if ([string]::IsNullOrWhiteSpace($admin.Email) -or [string]::IsNullOrWhiteSpace($admin.Password)) { throw "BootstrapSuperAdmin Email/Password is missing in appsettings." }

Write-Host "PayOrchestrator Super Admin" -ForegroundColor Cyan
Write-Host "Configured email : $($admin.Email)"
Write-Host "Configured name  : $($admin.DisplayName)"
Write-Host "Credentials are read from appsettings; the API hashes the password before storage." -ForegroundColor DarkGray

$loginBody = @{ email = $admin.Email; password = $admin.Password } | ConvertTo-Json
try {
  $result = Invoke-RestMethod -Method Post -Uri "$ApiBaseUrl/api/auth/login" -ContentType "application/json" -Body $loginBody
  Write-Host "Super Admin login verified successfully." -ForegroundColor Green
  $result.user | Format-List
} catch {
  Write-Error "Super Admin login verification failed. Start the API and check BootstrapSuperAdmin in appsettings. $($_.Exception.Message)"
  exit 1
}
