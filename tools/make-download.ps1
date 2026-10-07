# Site için doğrudan indirme paketi (Autodesk mağazası dışında, kurulum programı YOK — kullanıcı isteği 2026-10-07:
# "Addins altına kopyalayarak iş bitsin"). ZIP içinde her Revit yılı için hazır klasör:
#   2026\Smart3DView.addin + 2026\Smart3DView\ (DLL'ler + help)  →  içerik %AppData%\Autodesk\Revit\Addins\2026\ altına kopyalanır.
# .addin'deki Assembly yolu GÖRELİ (Revit .addin dosyasının bulunduğu klasöre göre çözer) → nereye kopyalanırsa çalışır.
# Çıktı: out\Smart3DView-<sürüm>.zip   Kullanım: .\tools\make-download.ps1 [-Years 2025,2026,2027]
param([string[]]$Years = @("2025", "2026", "2027"))
$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$root   = Split-Path $PSScriptRoot -Parent
$proj   = Join-Path $root "src"
$csproj = Join-Path $proj "Smart3DView.csproj"
$ver    = [regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$name   = "Smart3DView-$ver"
$stage  = Join-Path $root "out\download\$name"
$utf8   = New-Object Text.UTF8Encoding $false
if (Test-Path (Join-Path $root "out\download")) { Remove-Item (Join-Path $root "out\download") -Recurse -Force }

foreach ($y in $Years) {
    $rd = "C:\Program Files\Autodesk\Revit $y"
    if (-not (Test-Path (Join-Path $rd "RevitAPI.dll"))) { throw "Revit $y kurulu değil ($rd) — API referansı için gerekli" }
    Remove-Item (Join-Path $proj "obj"), (Join-Path $proj "bin") -Recurse -Force -ErrorAction SilentlyContinue
    $tmp = Join-Path $root "out\download\_build$y"
    Write-Host "Revit $y derleniyor..."
    & $dotnet publish $csproj -c Release -o $tmp "-p:RevitDir=$rd" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish başarısız ($y)" }
    $dst = Join-Path $stage "$y\Smart3DView"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $tmp "Smart3DView.dll"), (Join-Path $tmp "Smart3DView.deps.json") $dst -ErrorAction SilentlyContinue
    Get-ChildItem $tmp -File | Where-Object { $_.Extension -in ".dll", ".json" -and $_.Name -notlike "Revit*" -and $_.Name -notlike "AdWindows*" -and $_.Name -notlike "UIFramework*" } |
        Copy-Item -Destination $dst -Force
    Copy-Item (Join-Path $root "help") $dst -Recurse -Force
    $addin = (Get-Content (Join-Path $proj "Smart3DView.addin.template") -Raw).Replace("__DLL__", "Smart3DView\Smart3DView.dll")
    [IO.File]::WriteAllText((Join-Path $stage "$y\Smart3DView.addin"), $addin, $utf8)
    Remove-Item $tmp -Recurse -Force
}
$readme = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "download\README.txt")).Replace("{VER}", $ver) -replace "`r?`n", "`r`n"
[IO.File]::WriteAllText((Join-Path $stage "BENIOKU - README.txt"), $readme, $utf8)
Copy-Item (Join-Path $root "help\index.html") (Join-Path $stage "Smart3DView-Help.html")

$zip = Join-Path $root "out\$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
& (Join-Path $env:SystemRoot "System32\tar.exe") -a -c -f $zip -C (Join-Path $root "out\download") $name
if ($LASTEXITCODE -ne 0) { throw "zip oluşturulamadı" }
Write-Host ("İndirme paketi: {0} ({1:N0} KB)" -f $zip, ((Get-Item $zip).Length / 1KB))
