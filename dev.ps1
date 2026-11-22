param(
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$clientAppDir = Join-Path $repoRoot "src\ConversorLibros.Web\ClientApp"
$webProject = Join-Path $repoRoot "src\ConversorLibros.Web\ConversorLibros.Web.csproj"

if (-not (Test-Path (Join-Path $clientAppDir "node_modules"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Yellow
    Push-Location $clientAppDir
    try {
        npm install
    }
    finally {
        Pop-Location
    }
}

Write-Host "Starting backend (http://localhost:5180)..." -ForegroundColor Cyan
$backend = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", $webProject) `
    -WorkingDirectory $repoRoot `
    -PassThru `
    -NoNewWindow

try {
    $ready = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($backend.HasExited) {
            throw "Backend exited unexpectedly with code $($backend.ExitCode)."
        }
        try {
            $null = Invoke-WebRequest -Uri "http://localhost:5180/api/settings" -UseBasicParsing -TimeoutSec 1
            $ready = $true
            break
        }
        catch {
            Start-Sleep -Milliseconds 300
        }
    }

    if ($ready) {
        Write-Host "Backend ready on http://localhost:5180" -ForegroundColor Green
    }
    else {
        Write-Host "Backend is still starting; launching Vite dev server anyway..." -ForegroundColor Yellow
    }

    Write-Host "Starting React frontend (http://localhost:5173)..." -ForegroundColor Cyan
    Write-Host "Press Ctrl+C to stop both backend and frontend." -ForegroundColor DarkGray

    $viteArgs = @("run", "dev")
    if (-not $NoBrowser) {
        $viteArgs += @("--", "--open")
    }

    Push-Location $clientAppDir
    try {
        & npm @viteArgs
    }
    finally {
        Pop-Location
    }
}
finally {
    if ($backend -and -not $backend.HasExited) {
        Write-Host "`nStopping backend..." -ForegroundColor Yellow
        Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue
    }
}
