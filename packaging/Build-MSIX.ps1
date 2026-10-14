[CmdletBinding()]
param(
    [string]$PackageName = "Bticon.LocalTest",
    [string]$Publisher = "CN=Bticon Local Test",
    [string]$PublisherDisplayName = "Bticon",
    [string]$Version = "1.0.0.0",
    [switch]$ForStore
)

$ErrorActionPreference = "Stop"

if ($ForStore) {
    $PackageName = "GSLEARNING.BticonBatteryMonitor"
    $Publisher = "CN=7E22E405-AFEE-4422-9E36-6E74F2F781FE"
    $PublisherDisplayName = "GSLEARNING"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot ".artifacts\msix"
$workDirectory = Join-Path $outputDirectory ("work-" + [Guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $workDirectory "publish"
$layoutDirectory = Join-Path $workDirectory "layout"
$assetDirectory = Join-Path $layoutDirectory "Assets"
$manifestPath = Join-Path $layoutDirectory "AppxManifest.xml"
$templatePath = Join-Path $PSScriptRoot "Package.appxmanifest"
$packageSuffix = if ($ForStore) { "Store-x64" } else { "x64" }
$packagePath = Join-Path $outputDirectory "Bticon-$Version-$packageSuffix.msix"

$windowsKitsDirectory = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
if (-not (Test-Path $windowsKitsDirectory)) {
    throw "No se encontró Windows 10/11 SDK. Instálalo para generar el paquete MSIX."
}

$makeAppxPath = Get-ChildItem $windowsKitsDirectory -Directory |
    Where-Object { $_.Name -match '^\d+(\.\d+){3}$' } |
    Sort-Object { [version]$_.Name } -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\MakeAppx.exe" } |
    Where-Object { Test-Path $_ } |
    Select-Object -First 1

if (-not $makeAppxPath) {
    throw "No se encontró MakeAppx.exe. Instala Windows 10/11 SDK para generar el paquete MSIX."
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null

try {
    & dotnet publish (Join-Path $projectRoot "Bticon.csproj") `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        --output $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "La publicación de Bticon falló con código $LASTEXITCODE."
    }

    Copy-Item (Join-Path $publishDirectory "*") $layoutDirectory -Recurse -Force
    Get-ChildItem $layoutDirectory -Filter "*.pdb" -File | Remove-Item -Force
    [xml]$manifest = Get-Content $templatePath -Raw
    $manifest.Package.Identity.SetAttribute("Name", $PackageName)
    $manifest.Package.Identity.SetAttribute("Publisher", $Publisher)
    $manifest.Package.Identity.SetAttribute("Version", $Version)
    $manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
    $manifest.Save($manifestPath)

    Add-Type -AssemblyName System.Drawing
    foreach ($asset in @(
        @{ Name = "StoreLogo.png"; Size = 50 },
        @{ Name = "Square44x44Logo.png"; Size = 44 },
        @{ Name = "Square150x150Logo.png"; Size = 150 }
    )) {
        $size = $asset.Size
        $bitmap = [System.Drawing.Bitmap]::new($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $scale = $size / 150.0
            $graphics.ScaleTransform($scale, $scale)

            $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(16, 40, 64))
            $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 8)
            $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(46, 204, 113))
            $terminal = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            $backgroundPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            $batteryPath = [System.Drawing.Drawing2D.GraphicsPath]::new()
            try {
                $backgroundPath.AddArc(8, 8, 56, 56, 180, 90)
                $backgroundPath.AddArc(86, 8, 56, 56, 270, 90)
                $backgroundPath.AddArc(86, 86, 56, 56, 0, 90)
                $backgroundPath.AddArc(8, 86, 56, 56, 90, 90)
                $backgroundPath.CloseFigure()

                $batteryPath.AddArc(27, 45, 28, 28, 180, 90)
                $batteryPath.AddArc(91, 45, 28, 28, 270, 90)
                $batteryPath.AddArc(91, 93, 28, 28, 0, 90)
                $batteryPath.AddArc(27, 93, 28, 28, 90, 90)
                $batteryPath.CloseFigure()

                $graphics.FillPath($background, $backgroundPath)
                $graphics.FillRectangle($fill, 36, 54, 50, 58)
                $graphics.FillRectangle($terminal, 119, 70, 11, 26)
                $graphics.DrawPath($outline, $batteryPath)
            }
            finally {
                $backgroundPath.Dispose()
                $batteryPath.Dispose()
                $background.Dispose()
                $outline.Dispose()
                $fill.Dispose()
                $terminal.Dispose()
            }
            $bitmap.Save((Join-Path $assetDirectory $asset.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    & $makeAppxPath pack /d $layoutDirectory /p $packagePath /o
    if ($LASTEXITCODE -ne 0) {
        throw "La creación del paquete MSIX falló con código $LASTEXITCODE."
    }

    Write-Output "Paquete MSIX creado: $packagePath"
    if (-not $ForStore) {
        Write-Warning "El paquete usa una identidad de prueba local; reserva el producto en Partner Center y reemplaza la identidad antes de enviarlo a Microsoft Store."
    }
}
finally {
    if (Test-Path $workDirectory) {
        Remove-Item $workDirectory -Recurse -Force
    }
}
