<#
.SYNOPSIS
  Do'kon uchun o'rnatuvchi yig'adi: Buildix-win-Setup.exe

.DESCRIPTION
  Uch bosqich: interfeysni qurish, ikkala .NET loyihasini nashr qilish va
  Velopack bilan paketlash.

  Nega skript: bu qadamlar avval faqat qo'lda bajarilgan edi va tartibi
  yodda saqlanardi. Bitta qadam o'tkazib yuborilsa (masalan interfeys qayta
  qurilmasa) to'plam eski interfeys bilan chiqib ketardi - buni esa faqat
  do'konda, o'rnatilgandan keyin bilishardi.

  DIQQAT: bu fayl UTF-8 BOM bilan saqlanishi shart. PowerShell 5.1 BOM'siz
  .ps1 faylni tizim kodlashi deb o'qiydi va lotin bo'lmagan har qanday belgi
  skriptni ochilmaydigan qilib qo'yadi.

.PARAMETER Version
  Chiqarish versiyasi. Berilmasa Buildix.Desktop.csproj dagi <Version>
  o'qiladi - ikkita joyda yozib, ularni bir-biriga moslashtirishga urinish
  xatoning eng oson yo'li.

.PARAMETER FeedUrl
  Yangilanish serveri manzili. Berilsa oxirida eslatma chiqaradi.

.EXAMPLE
  ./deploy/build-desktop.ps1
  ./deploy/build-desktop.ps1 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$FeedUrl,
    [string]$OutputDir = "$PSScriptRoot\..\artifacts"
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
$stage = Join-Path $env:TEMP "buildix-desktop-stage"

# PowerShell 5.1 tashqi buyruq stderr ga yozgan har bir qatorni xato deb
# hisoblaydi. $ErrorActionPreference='Stop' bilan birga bu npm ning oddiy
# "deprecated" ogohlantirishini ham to'liq qurishni to'xtatadigan xatoga
# aylantiradi. Shuning uchun tashqi buyruqlar shu yordamchi orqali
# chaqiriladi: muvaffaqiyat faqat chiqish kodi bilan o'lchanadi.
function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$File,
        [string[]]$Arguments = @(),
        [Parameter(Mandatory)][string]$FailMessage
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $File @Arguments } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw $FailMessage }
}

# --- Versiya: yagona manba - csproj ------------------------------------------
if (-not $Version) {
    $csproj = [xml](Get-Content "$root\Buildix.Desktop\Buildix.Desktop.csproj")
    $Version = ($csproj.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
    if (-not $Version) { throw "Buildix.Desktop.csproj da <Version> topilmadi." }
}
Write-Host "Versiya: $Version" -ForegroundColor Cyan

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk topilmadi. O'rnatish: dotnet tool install -g vpk"
}

# --- 1. Interfeys ------------------------------------------------------------
Write-Host "`n[1/3] Interfeys qurilmoqda..." -ForegroundColor Cyan
Push-Location "$root\Buildix.Web"
try {
    Invoke-Native npm.cmd @('ci') 'Interfeys bogliqliklari ornatilmadi.'
    Invoke-Native npm.cmd @('run', 'build') 'Interfeys qurilmadi.'
} finally { Pop-Location }

# --- 2. Nashr ----------------------------------------------------------------
Write-Host "`n[2/3] Ilova nashr qilinmoqda..." -ForegroundColor Cyan
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

Invoke-Native dotnet @(
    'publish', "$root\Buildix.Desktop\Buildix.Desktop.csproj",
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    "-p:Version=$Version", '-o', $stage, '--nologo'
) 'Qobiq nashr qilinmadi.'

# API qobiq yonidagi `api` papkasiga - ApiHost aynan shu yerdan qidiradi.
Invoke-Native dotnet @(
    'publish', "$root\Buildix.API\Buildix.API.csproj",
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:IncludeSpa=true', '-o', "$stage\api", '--nologo'
) 'API nashr qilinmadi.'

# Nashr natijasini TEKSHIRAMIZ. Bu uchta tekshiruvning har biri ilgari
# haqiqatan sodir bo'lgan xatoga qarshi turadi va ularning hammasi faqat
# do'konda, o'rnatilgandan keyin sezilardi.
if (-not (Test-Path "$stage\api\appsettings.Desktop.json")) {
    throw "appsettings.Desktop.json nashrga tushmadi - desktop rejimi yoqilmaydi, ilova bosh oyna korsatadi."
}
if (-not (Test-Path "$stage\api\spa\index.html")) {
    throw "Interfeys nashrga tushmadi."
}
if (Test-Path "$stage\api\appsettings.Development.json") {
    throw "appsettings.Development.json nashrga tushib qolgan - ichida sirlar bor!"
}

# --- 3. Paketlash ------------------------------------------------------------
Write-Host "`n[3/3] O'rnatuvchi yig'ilmoqda..." -ForegroundColor Cyan
Invoke-Native vpk @(
    'pack',
    '--packId', 'Buildix',
    '--packVersion', $Version,
    '--packDir', $stage,
    '--mainExe', 'Buildix.Desktop.exe',
    '--packTitle', 'Buildix',
    '--packAuthors', 'Strotech',
    '--outputDir', $OutputDir
) 'Paketlash muvaffaqiyatsiz.'

$setup = Join-Path $OutputDir 'Buildix-win-Setup.exe'
$size = [math]::Round((Get-Item $setup).Length / 1MB)
Write-Host "`nTayyor: $setup ($size MB)" -ForegroundColor Green

if ($FeedUrl) {
    Write-Host "Yangilanish manzili: $FeedUrl" -ForegroundColor Green
    Write-Host "Uni har do'konda %ProgramData%\Buildix\desktop.json ichidagi UpdateFeedUrl ga yozing." -ForegroundColor Yellow
}
