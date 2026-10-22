#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installRoot = Join-Path $env:ProgramFiles 'OzgurAuth'
$serviceDir = Join-Path $installRoot 'Service'
$verifierDir = Join-Path $installRoot 'Verifier'
$stage = Join-Path $env:TEMP 'OzgurAuth-publish'
$serviceName = 'OzgurAuth'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK bulunamadi. Sunucuda .NET 8 SDK kurulu olmali veya bu betigi gelistirme makinesinde calistirin.'
}

Write-Host 'OzgurAuth yayinlaniyor (win-x64, klasor ciktisi)...' -ForegroundColor Cyan
dotnet publish (Join-Path $repoRoot 'OzgurAuth.Service\OzgurAuth.Service.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'Service')
if ($LASTEXITCODE -ne 0) { throw 'Servis yayini basarisiz.' }

dotnet publish (Join-Path $repoRoot 'OzgurAuth.VerifierUI\OzgurAuth.VerifierUI.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'Verifier')
if ($LASTEXITCODE -ne 0) { throw 'Arayuz yayini basarisiz.' }

# ÖNCE çalışan servisi ve açık arayüzü durdur (Dosya kilidini önlemek için)
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    Get-Process -Name 'OzgurAuth.VerifierUI' -ErrorAction SilentlyContinue | Stop-Process -Force
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# SONRA yeni dosyaları kopyala
New-Item -ItemType Directory -Force -Path $serviceDir, $verifierDir | Out-Null
Copy-Item -Path (Join-Path $stage 'Service\*') -Destination $serviceDir -Recurse -Force
Copy-Item -Path (Join-Path $stage 'Verifier\*') -Destination $verifierDir -Recurse -Force

if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

$binary = Join-Path $serviceDir 'OzgurAuth.Service.exe'
New-Service `
    -Name $serviceName `
    -BinaryPathName "`"$binary`"" `
    -DisplayName 'OzgurAuth RDP Oturum Dogrulama' `
    -StartupType Automatic `
    -Description 'RDP ve konsol oturumlarinda ikinci parola dogrulamasi. IIS ve HTTP servislerine dokunmaz.'

$settingsPath = Join-Path $serviceDir 'appsettings.json'
$settings = Get-Content -Raw -Path $settingsPath
if ($settings -match '"TestMode"\s*:\s*true') {
    Write-Warning 'TestMode acik. WTSDisconnectSession cagrilmaz; yalnizca olay gunlugune yazilir. Canli kesme icin TestMode degerini false yapip Restart-Service OzgurAuth calistirin.'
}

Start-Service -Name $serviceName
Write-Host "Hizmet kaydedildi ve baslatildi: $serviceName" -ForegroundColor Green
Write-Host "Kurulum dizini: $installRoot" -ForegroundColor Green