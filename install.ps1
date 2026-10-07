# Smart3DView eklentisini KULLANICI klasörüne kurar (yönetici izni gerekmez).
#   .\install.ps1                  → kurulu tüm Revit 2025/2026 sürümlerine
#   .\install.ps1 -Years 2026      → yalnız 2026
#   .\install.ps1 -Uninstall
param([switch]$Uninstall, [string[]]$Years = @("2025", "2026", "2027"))

$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }
$root = $PSScriptRoot
$version = ([xml](Get-Content (Join-Path $root "src\Smart3DView.csproj"))).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1

foreach ($Year in $Years) {
    $revitDir = "C:\Program Files\Autodesk\Revit $Year"
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
    $target = Join-Path $addins "Smart3DView"
    $manifest = Join-Path $addins "Smart3DView.addin"

    # Eski ad (RevitQuickBox / "Hızlı 3D") kurulumu: manifest silinir → ribbon'da iki düğme olmaz. Klasör kilitliyse kalır.
    Remove-Item (Join-Path $addins "RevitQuickBox.addin") -ErrorAction SilentlyContinue
    try { Remove-Item (Join-Path $addins "RevitQuickBox") -Recurse -Force -ErrorAction Stop } catch { }

    if ($Uninstall) {
        Remove-Item $manifest -ErrorAction SilentlyContinue
        Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Kaldırıldı: $Year"
        continue
    }
    if (-not (Test-Path (Join-Path $revitDir "RevitAPI.dll"))) { Write-Host "Atlandı (Revit $Year yok)"; continue }

    $out = Join-Path $root "out\$Year"
    & $dotnet build (Join-Path $root "src\Smart3DView.csproj") -c Release -o $out "-p:RevitDir=$revitDir" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "derleme başarısız ($Year)" }

    # Her sürüm kendi alt klasörüne kurulur: Revit açıkken eski DLL kilitli olsa bile yeni sürüm kurulabilir;
    # manifest yeni klasörü gösterir → Revit yeniden başlayınca yeni sürüm yüklenir. Eski dosyalar silinebiliyorsa silinir.
    $verDir = Join-Path $target "v$version"
    New-Item -ItemType Directory -Force $verDir | Out-Null
    Copy-Item (Join-Path $out "Smart3DView.*") $verDir -Force
    Copy-Item (Join-Path $out "help") $verDir -Recurse -Force   # hızlı başlangıç / yardım sayfası (F1)
    Get-ChildItem $target | Where-Object { $_.FullName -ne $verDir } | ForEach-Object {
        try { Remove-Item $_.FullName -Recurse -Force -ErrorAction Stop } catch { }
    }
    $dll = Join-Path $verDir "Smart3DView.dll"
    (Get-Content (Join-Path $root "src\Smart3DView.addin.template") -Raw).Replace("__DLL__", $dll) |
        Set-Content $manifest -Encoding UTF8
    Write-Host "Kuruldu: Revit $Year v$version → $verDir"
}
if (-not $Uninstall) { Write-Host "Revit'i (yeniden) başlat → Eklentiler (Add-Ins) sekmesi → 'Smart3DView' paneli." }
