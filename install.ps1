#Requires -RunAsAdministrator
<#
.SYNOPSIS
    OzgurGuard - RDP & Konsol Oturum Guvenlik Katmani Kurulum Betigi
.DESCRIPTION
    Bu betik .NET 8 SDK kontrolu yapar (eksikse otomatik indirip kurar),
    OzgurGuard.Service ve OzgurGuard.LockerUI projelerini derler,
    Windows Service olarak kaydeder ve baslatir.
    Sirket: Ozgur Zaman Kontrol Sistemleri
#>

$ErrorActionPreference = 'Stop'

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Ozgur Zaman Kontrol Sistemleri - OzgurGuard Kurulumu   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$installRoot = Join-Path $env:ProgramFiles 'OzgurGuard'
$serviceDir = Join-Path $installRoot 'Service'
$lockerDir = Join-Path $installRoot 'Locker'
$logDir = 'C:\ProgramData\OzgurGuard'
$stage = Join-Path $env:TEMP 'OzgurGuard-publish'
$serviceName = 'OzgurGuard'

# 1. .NET 8 SDK Kontrolu (Runtime yetmez, SDK gereklidir)
Write-Host "[1/7] .NET 8 SDK durumu denetleniyor..." -ForegroundColor Yellow
$hasSdk = $false

if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdks = (& dotnet --list-sdks 2>$null)
    if ($sdks -and ($sdks -match '8\.')) {
        $hasSdk = $true
        Write-Host "  -> .NET 8 SDK tespit edildi: $sdks" -ForegroundColor Green
    }
}

if (-not $hasSdk) {
    Write-Host "  -> DİKKAT: Sunucuda .NET 8 SDK bulunamadı (Yalnızca Runtime var veya SDK eksik)." -ForegroundColor Yellow
    Write-Host "  -> Microsoft resmi sunucusundan .NET 8.0 SDK otomatik indiriliyor..." -ForegroundColor Cyan
    
    $sdkInstallerUrl = "https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
    $installerPath = Join-Path $env:TEMP "dotnet-sdk-8.0-win-x64.exe"
    
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "  -> İndiriliyor: $sdkInstallerUrl..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $sdkInstallerUrl -OutFile $installerPath -UseBasicParsing
    
    Write-Host "  -> .NET 8 SDK sessiz kurulumu yapılıyor (lütfen 30-60 saniye bekleyin)..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $installerPath -ArgumentList "/install /quiet /norestart" -Wait -PassThru
    
    if ($proc.ExitCode -ne 0) {
        Write-Warning ".NET SDK otomatik kurulum kodu: $($proc.ExitCode). Lütfen elle kurun: https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
    }
    
    # Ortam değişkenlerini (PATH) geçerli oturum için yenile
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
    
    # SDK kontrolünü tekrar et
    $sdks = (& dotnet --list-sdks 2>$null)
    if (-not $sdks -or ($sdks -notmatch '8\.')) {
        throw "Lütfen .NET 8.0 SDK'yı resmi linkten elle indirip kurduktan sonra install.ps1 betiğini tekrar çalıştırın:`nhttps://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe"
    }
    Write-Host "  -> .NET 8 SDK başarıyla kuruldu!" -ForegroundColor Green
}

# 2. Projeleri Derleme (Self-Contained win-x64)
Write-Host "[2/7] OzgurGuard.Service derleniyor (Release win-x64)..." -ForegroundColor Yellow
dotnet publish (Join-Path $repoRoot 'OzgurGuard.Service\OzgurGuard.Service.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'Service')
if ($LASTEXITCODE -ne 0) { throw 'Servis derlemesi basarisiz oldu.' }

Write-Host "[3/7] OzgurGuard.LockerUI derleniyor (Release win-x64)..." -ForegroundColor Yellow
dotnet publish (Join-Path $repoRoot 'OzgurGuard.LockerUI\OzgurGuard.LockerUI.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'Locker')
if ($LASTEXITCODE -ne 0) { throw 'Kilit arayuzu derlemesi basarisiz oldu.' }

# 3. Eski Çalışan Servisi ve Süreçleri Temizle
Write-Host "[4/7] Mevcut servis ve acik kilit surecleri kontrol ediliyor..." -ForegroundColor Yellow
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
    }
    Get-Process -Name 'OzgurGuard.LockerUI' -ErrorAction SilentlyContinue | Stop-Process -Force
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# 4. Dosyaları Program Files Altına Kopyala
Write-Host "[5/7] Ikili dosyalar $installRoot dizinine kopyalaniyor..." -ForegroundColor Yellow
New-Item -ItemType Directory -Force -Path $serviceDir, $lockerDir, $logDir | Out-Null
Copy-Item -Path (Join-Path $stage 'Service\*') -Destination $serviceDir -Recurse -Force
Copy-Item -Path (Join-Path $stage 'Locker\*') -Destination $lockerDir -Recurse -Force

# 5. Windows Event Log Kaynağını Oluştur
Write-Host "[6/7] Windows Event Log kaynagi olusturuluyor..." -ForegroundColor Yellow
if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    New-EventLog -LogName Application -Source $serviceName
}

# 6. Windows Servisini Kaydet ve Başlat
Write-Host "[7/7] Windows Servisi kaydediliyor ve baslatiliyor..." -ForegroundColor Yellow
$binary = Join-Path $serviceDir 'OzgurGuard.Service.exe'
New-Service `
    -Name $serviceName `
    -BinaryPathName "`"$binary`"" `
    -DisplayName 'OzgurGuard RDP Oturum Kilit Guvenligi' `
    -StartupType Automatic `
    -Description 'RDP ve konsol oturumlarinda masaustunu kilitleyen ve brute-force saldirilarina karsi oturumu kesen kurumsal guvenlik kapisi. IIS ve Web servislerine dokunmaz.' | Out-Null

# Kurtarma seçeneklerini ayarla (Çökerse servisi hemen yeniden başlat)
& sc.exe failure $serviceName reset= 86400 actions= restart/1000/restart/2000/restart/5000 | Out-Null

Start-Service -Name $serviceName
Start-Sleep -Seconds 1
$svc = Get-Service -Name $serviceName
Write-Host "  -> Servis durumu: $($svc.Status)" -ForegroundColor ($svc.Status -eq 'Running' ? 'Green' : 'Red')

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  TEBRIKLER! OzgurGuard Basariyla Kuruldu ve Baslatildi!  " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Kurulum Dizini  : $installRoot" -ForegroundColor White
Write-Host "Guvenlik Gunlugu: $logDir\security.log" -ForegroundColor White
Write-Host "Varsayilan Sifre: OzurGuvenlik2026!" -ForegroundColor Cyan
Write-Host ""
Write-Host "Sifreyi degistirmek icin:" -ForegroundColor Yellow
Write-Host "  cd `"$serviceDir`"" -ForegroundColor Gray
Write-Host "  .\OzgurGuard.Service.exe set-password `"YeniSifreniz123!`"" -ForegroundColor Gray
Write-Host "  Restart-Service OzgurGuard" -ForegroundColor Gray
Write-Host ""
