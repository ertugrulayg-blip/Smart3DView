# Autodesk mağaza paketi: out\store\Smart3DView.bundle + Smart3DView-Help.html → out\Smart3DView-<sürüm>-store.zip
# Mağaza kuralı: eklenti %AppData%\Autodesk\ApplicationPlugins\<Ad>.bundle altına kurulur; Revit PackageContents.xml'i
# okuyup her <Components> bloğundaki .addin'i yükler. Revit YALNIZ SeriesMin'e bakar → her sürüme ayrı blok + ayrı
# derleme (Contents\<yıl>\, o yılın RevitAPI.dll'ine karşı). Mağaza kendi kurulum dosyasını üretir; bu klasör yine de
# birebir o yapıda, yerelde ApplicationPlugins'e kopyalanıp denenebilir (önce install.ps1 -Uninstall: aynı AddInId).
# Kullanım:  .\tools\make-bundle.ps1                      (2025 + 2026)
#            .\tools\make-bundle.ps1 -Years 2025,2026,2027  (2027: .NET 10 SDK + Revit 2027 kurulu olmalı)
param([string[]]$Years = @("2025", "2026", "2027"))

$ErrorActionPreference = "Stop"
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$root    = Split-Path $PSScriptRoot -Parent
$proj    = Join-Path $root "src"
$csproj  = Join-Path $proj "Smart3DView.csproj"
$ver     = [regex]::Match((Get-Content $csproj -Raw), '<Version>([^<]+)</Version>').Groups[1].Value
$stage   = Join-Path $root "out\store"
$bundle  = Join-Path $stage "Smart3DView.bundle"
$zip     = Join-Path $root "out\Smart3DView-$ver-store.zip"
$appName = "Smart3DView"
$addinId = "e9df1ea9-a971-47dc-9114-30a8eae5f929"       # Smart3DView.addin.template ile AYNI kalmalı
$upgrade = "{8C2D5A71-3E94-4B6F-A0D2-7F1E6C93B458}"      # sabit: tüm sürümler aynı ürün
$utf8    = New-Object Text.UTF8Encoding $false
$help    = Join-Path $root "help\index.html"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$comps = ""
foreach ($y in $Years) {
    $rd = "C:\Program Files\Autodesk\Revit $y"
    if (-not (Test-Path (Join-Path $rd "RevitAPI.dll"))) { throw "Revit $y kurulu değil ($rd) — API referansı için gerekli" }
    # Önceki yılın derlemesi yeniden kullanılmasın (başka RevitAPI.dll'e karşı derlenmiş olur)
    Remove-Item (Join-Path $proj "obj"), (Join-Path $proj "bin") -Recurse -Force -ErrorAction SilentlyContinue
    $dst = Join-Path $bundle "Contents\$y"
    Write-Host "Revit $y derleniyor..."
    $pa = @("publish", $csproj, "-c", "Release", "-o", $dst, "-p:RevitDir=$rd")
    & $dotnet @pa | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "publish başarısız ($y)" }
    Get-ChildItem $dst -Recurse -Include *.pdb, *.template | Remove-Item -Force
    # Assembly yolu .addin dosyasına göre göreli (paket her yere kurulabilsin)
    $addin = @"
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>$appName</Name>
    <Assembly>.\Smart3DView.dll</Assembly>
    <AddInId>$addinId</AddInId>
    <FullClassName>Smart3DView.App</FullClassName>
    <VendorId>SCHM</VendorId>
    <VendorDescription>SchemaTools, https://schema-tools.net</VendorDescription>
  </AddIn>
</RevitAddIns>
"@
    [IO.File]::WriteAllText((Join-Path $dst "Smart3DView.addin"), $addin, $utf8)
    $comps += @"
  <Components Description="Revit $y">
    <RuntimeRequirements OS="Win64" Platform="Revit" SeriesMin="R$y" SeriesMax="R$y" />
    <ComponentEntry AppName="$appName" Version="$ver" AppType="ManagedPlugin" ModuleName="./Contents/$y/Smart3DView.addin" />
  </Components>

"@
}

# Paketle gelen yardım (Autodesk: "HTML quick-start page included with the download")
New-Item -ItemType Directory -Force (Join-Path $bundle "Contents\Help") | Out-Null
Copy-Item $help (Join-Path $bundle "Contents\Help\index.html")

# ProductCode sürüme göre belirlenimci (aynı sürüm = aynı kod), UpgradeCode sabit
$md5 = [Security.Cryptography.MD5]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes("Smart3DView-$ver"))
$product = "{" + (New-Object Guid (, [byte[]]$md5)).ToString().ToUpper() + "}"
$pkg = @"
<?xml version="1.0" encoding="utf-8"?>
<ApplicationPackage SchemaVersion="1.0" AutodeskProduct="Revit" ProductType="Application" Name="$appName"
    AppVersion="$ver" ProductCode="$product" UpgradeCode="$upgrade" Author="SchemaTools"
    Description="Fast 3D section box viewer for Revit in its own window: grayscale tones, colored MEP systems, clash detection and pictures to Revit"
    HelpFile="./Contents/Help/index.html" OnlineDocumentation="https://schema-tools.net/smart3dview/help/">
  <CompanyDetails Name="SchemaTools" Url="https://schema-tools.net" Email="schematoolssupp@outlook.com" />
$comps</ApplicationPackage>
"@
[IO.File]::WriteAllText((Join-Path $bundle "PackageContents.xml"), $pkg, $utf8)
Copy-Item $help (Join-Path $stage "Smart3DView-Help.html")

if (Test-Path $zip) { Remove-Item $zip -Force }
# Compress-Archive (PowerShell 5.1) zip içindeki yolları "\" ile yazar — standart dışı. Windows'un tar.exe'si "/" yazar.
& (Join-Path $env:SystemRoot "System32\tar.exe") -a -c -f $zip -C $stage "Smart3DView.bundle" "Smart3DView-Help.html"
if ($LASTEXITCODE -ne 0) { throw "zip oluşturulamadı" }
Write-Host ("Paket hazır: {0} ({1:N1} MB) — Revit {2}" -f $zip, ((Get-Item $zip).Length / 1MB), ($Years -join ", "))
