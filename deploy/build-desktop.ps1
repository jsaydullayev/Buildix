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

# --- Vaqtinchalik papka CHIQISH diskida --------------------------------------
# Ilgari tizimning %TEMP% i ishlatilardi, ya'ni yig'ish har doim C: ga
# tayanardi. Chiqish esa boshqa diskda bo'lishi mumkin va odatda shunday
# bo'ladi. Yig'ish paytida bir necha gigabayt kerak: nashr papkasi (~410 MB),
# ustiga Velopack delta uchun IKKITA to'liq paketni (har biri ~170 MB) ochadi.
#
# C: to'lganda bu tushunarsiz yiqilardi — vpk ning xatosi «diskda joy yo'q»
# desa ham, u qaysi disk ekanini aytmasdi va chiqish papkasida joy
# yetarli bo'lgani uchun sabab boshqa yerdan qidirilardi.
$OutputDir = if (Test-Path $OutputDir) { (Resolve-Path $OutputDir).Path }
             else { (New-Item -ItemType Directory -Force $OutputDir).FullName }
$build = Join-Path $OutputDir '.build'
$stage = Join-Path $build 'stage'
New-Item -ItemType Directory -Force $build | Out-Null

# Velopack o'z oraliq fayllarini %TEMP% ga yozadi va uni sozlash imkoni yo'q —
# shuning uchun o'zgaruvchining o'zi shu seans uchun ko'chiriladi.
$env:TEMP = $build
$env:TMP = $build

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

# Velopack CLI. `dotnet tool install -g` uni ~\.dotnet\tools ga qo'yadi va
# o'sha papkani PATH ga qo'shadi — lekin FAQAT yangi seanslarda. O'rnatgan
# oynada yoki skript avtomatik chaqirilganda PATH eski holicha qoladi va
# skript "o'rnatilmagan" deb to'xtardi, aslida o'rnatilgan bo'lsa ham.
$toolsDir = Join-Path $env:USERPROFILE '.dotnet\tools'
if (-not (Get-Command vpk -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $toolsDir 'vpk.exe'))) {
    $env:PATH = "$env:PATH;$toolsDir"
}
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk topilmadi. O'rnatish: dotnet tool install -g vpk"
}

# --- To'plamdagi PostgreSQL --------------------------------------------------
# Do'kon kompyuterida hech narsa alohida o'rnatilmaydi, shu jumladan baza ham.
# Rasmiy "binaries" arxivi olinadi - u o'rnatuvchi emas, oddiy zip: ichidan
# kerakli papkalarni ko'chirib qo'yish yetarli.
#
# Versiya QATTIQ belgilangan. Har yig'ishda eng yangisini olish - do'konlarga
# bir-biridan farq qiladigan baza tarqatish demak, va bunday farq faqat
# ma'lumot buzilganda bilinardi.
$PgVersion = '17.6-1'
# Nazorat summasi 2026-08-25 da yuklab olingan arxivdan hisoblangan. Uni
# tekshirish arxiv yo'lda o'zgarmaganiga ishonch beradi; versiya
# yangilanganda bu qiymat ham yangilanishi SHART, aks holda yig'ish to'xtaydi.
$PgSha256 = 'D378882ABD001A186735ACD6F6BA716BCA6CCD192E800412D4FD15ED25376B3E'
# Kesh ilovaning O'RNATISH papkasidan TASHQARIDA bo'lishi shart. Birinchi
# variant `%LocalAppData%\Buildix\build-cache` edi va u har yig'ishda qaytadan
# 330 MB yuklab olardi: o'sha papka Velopack'niki va u har o'rnatish/yangilash
# paytida tozalanadi.
$cacheDir = Join-Path $env:LOCALAPPDATA 'Buildix-build-cache'
$pgZip = Join-Path $cacheDir "postgresql-$PgVersion-windows-x64-binaries.zip"
$pgDir = Join-Path $cacheDir "pgsql-$PgVersion"

function Get-BundledPostgres {
    if (Test-Path (Join-Path $pgDir 'bin\postgres.exe')) { return $pgDir }

    New-Item -ItemType Directory -Force $cacheDir | Out-Null
    if (-not (Test-Path $pgZip)) {
        Write-Host "PostgreSQL $PgVersion yuklab olinmoqda (~330 MB, bir marta)..." -ForegroundColor Cyan
        $url = "https://get.enterprisedb.com/postgresql/postgresql-$PgVersion-windows-x64-binaries.zip"
        # -C - : uzilgan yerdan davom ettiradi. Bu shart emasdek tuyulardi,
        # lekin 330 MB ni sekin ulanishda yuklash o'n daqiqadan oshadi va
        # bitta uzilish butun yig'ishni boshidan boshlashga majbur qilardi.
        # Invoke-WebRequest bu hajmda sezilarli sekin, shuning uchun curl.
        foreach ($attempt in 1..3) {
            & curl.exe -L --fail -C - --retry 3 --retry-delay 5 `
                --connect-timeout 30 --max-time 3600 -o $pgZip $url
            if ($LASTEXITCODE -eq 0) { break }
            if ($attempt -eq 3) { throw "PostgreSQL arxivini yuklab bolmadi (kod $LASTEXITCODE)." }
            Write-Host "  uzildi, davom ettirilmoqda ($attempt/3)..." -ForegroundColor Yellow
        }
    }

    if ($PgSha256) {
        $actual = (Get-FileHash $pgZip -Algorithm SHA256).Hash
        if ($actual -ne $PgSha256) {
            Remove-Item $pgZip -Force
            throw "PostgreSQL arxivi kutilgan nazorat summasiga mos emas: $actual"
        }
    } else {
        Write-Host ("DIQQAT: arxiv nazorat summasi tekshirilmadi. build-desktop.ps1 dagi " +
                    "`$PgSha256 ga shu qiymatni yozing: " +
                    (Get-FileHash $pgZip -Algorithm SHA256).Hash) -ForegroundColor Yellow
    }

    Write-Host "PostgreSQL ochilmoqda..." -ForegroundColor Cyan
    $tmp = "$pgDir-tmp"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    # Expand-Archive bu hajmdagi arxivda bir necha daqiqa oladi; .NET ning
    # o'z ochuvchisi ancha tez.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($pgZip, $tmp)

    # Arxiv ichida bitta `pgsql` papkasi bo'ladi.
    $inner = Join-Path $tmp 'pgsql'
    if (-not (Test-Path $inner)) { throw "Arxiv ichida pgsql papkasi topilmadi." }

    # Do'konga kerak bo'lmagan narsalar. bin/lib/share qoladi: initdb
    # share/ dan bazani quradi, kengaytmalar esa lib/ da.
    foreach ($drop in 'include', 'doc', 'symbols', 'StackBuilder', 'pgAdmin 4') {
        $path = Join-Path $inner $drop
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }

    # --- Visual C++ ish vaqti ------------------------------------------------
    # PostgreSQL binarlari vcruntime140 / vcruntime140_1 / msvcp140 ni talab
    # qiladi. Ular Windows tarkibiga KIRMAYDI - «Visual C++ Redistributable»
    # bilan keladi. Ko'p kompyuterda u boshqa dasturlar tufayli o'rnatilgan
    # bo'ladi, lekin toza Windows'da yo'q: o'shanda postgres.exe umuman ishga
    # tushmay, «vcruntime140.dll topilmadi» degan tushunarsiz oyna chiqadi.
    #
    # Fayllar exe yoniga qo'yiladi (app-local). Windows qidiruv tartibida
    # exe yonidagi papka System32 dan oldin keladi, ya'ni to'plamdagi nusxa
    # ishlatiladi va tizimda nima borligi ahamiyatsiz bo'ladi.
    #
    # Manba - yig'ish kompyuterining System32 papkasi. Bu Microsoft ruxsat
    # bergan tarqatish usuli va fayllar tarqatma paketdagi bilan aynan bir xil.
    $crt = 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll'
    foreach ($dll in $crt) {
        $src = Join-Path $env:WINDIR "System32\$dll"
        if (-not (Test-Path $src)) {
            throw "$dll topilmadi. Yig'ish kompyuteriga Visual C++ Redistributable o'rnating."
        }
        $version = (Get-Item $src).VersionInfo.FileVersion
        # PostgreSQL 17 MSVC 14.4x bilan qurilgan; undan eski ish vaqti mos kelmaydi.
        if ([version]($version -replace '^(\d+\.\d+).*', '$1.0.0') -lt [version]'14.40.0.0') {
            throw "$dll juda eski ($version). Kamida 14.40 kerak."
        }
        Copy-Item $src (Join-Path $inner 'bin') -Force
        Write-Host "  ish vaqti: $dll $version" -ForegroundColor DarkGray
    }

    Move-Item $inner $pgDir
    Remove-Item $tmp -Recurse -Force
    return $pgDir
}

$pgSource = Get-BundledPostgres

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

# PostgreSQL qobiq yonidagi `pg` papkasiga - PostgresHost aynan shu yerdan
# qidiradi (AppContext.BaseDirectory/pg/bin).
Write-Host "PostgreSQL to'plamga ko'chirilmoqda..." -ForegroundColor Cyan
Copy-Item $pgSource "$stage\pg" -Recurse -Force

# Nashr natijasini TEKSHIRAMIZ. Bu tekshiruvlarning har biri ilgari haqiqatan
# sodir bo'lgan xatoga qarshi turadi va ularning hammasi faqat do'konda,
# o'rnatilgandan keyin sezilardi.
#
# Baza tekshiruvi ayniqsa muhim: u yo'q bo'lganda ilova JIMGINA
# appsettings.json dagi zaxira ulanish satriga (dasturchi bazasi) urinardi va
# dasturchining kompyuterida hammasi ishlayotgandek ko'rinardi.
foreach ($tool in 'postgres', 'initdb', 'createdb', 'pg_isready', 'pg_ctl', 'pg_dump') {
    if (-not (Test-Path "$stage\pg\bin\$tool.exe")) {
        throw "PostgreSQL to'plamga tushmadi: $tool.exe yo'q."
    }
}
if (-not (Test-Path "$stage\pg\share\postgres.bki")) {
    throw "PostgreSQL share papkasi to'liq emas - initdb bazani qura olmaydi."
}
foreach ($dll in 'vcruntime140.dll', 'vcruntime140_1.dll', 'msvcp140.dll') {
    if (-not (Test-Path "$stage\pg\bin\$dll")) {
        throw "$dll to'plamga tushmadi - toza Windows'da baza ishga tushmaydi."
    }
}
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
