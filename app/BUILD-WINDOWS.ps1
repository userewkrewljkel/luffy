$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'The .NET 8 SDK is required. Install with: winget install Microsoft.DotNet.SDK.8' -ForegroundColor Yellow
    Write-Host 'Then reopen PowerShell and run this script again.'
    Read-Host 'Press Enter to close'
    exit 1
}
dotnet publish .\LufsTaskbarMeter.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { throw 'Build failed; check the messages above.' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net8.0-windows\win-x64\publish\LUFFY-v3.14.exe'
if (-not (Test-Path $exe)) { throw "Build completed but the executable is missing: $exe" }
Write-Host "Finished: $exe" -ForegroundColor Green
Write-Host 'Launching LUFFY v3.14...' -ForegroundColor Cyan
$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe) -PassThru
Start-Sleep -Seconds 2
$proc.Refresh()
if ($proc.HasExited) {
    Write-Warning "LUFFY v3.14 exited shortly after launch (exit code $($proc.ExitCode))."
    $errorLog = Join-Path $env:LOCALAPPDATA 'LufsTaskbarMeter\errors.log'
    if(Test-Path $errorLog) { Write-Host "Error log: $errorLog" -ForegroundColor Yellow }
    Write-Host 'If an old LUFFY is still running, close it using right-click -> Exit.'
} else {
    Write-Host "LUFFY v3.14 is running (PID $($proc.Id))." -ForegroundColor Green
}
