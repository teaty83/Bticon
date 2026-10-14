[CmdletBinding()]
param(
    [string]$PackageName = "Bticon.LocalTest",
    [string]$Publisher = "CN=Bticon Local Test",
    [string]$Version = "1.0.0.0"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $projectRoot ".artifacts\msix"
$workDirectory = Join-Path $outputDirectory ("work-" + [Guid]::NewGuid().ToString("N"))
$publishDirectory = Join-Path $workDirectory "publish"
$layoutDirectory = Join-Path $workDirectory "layout"
$assetDirectory = Join-Path $layoutDirectory "Assets"
$manifestPath = Join-Path $layoutDirectory "AppxManifest.xml"
$templatePath = Join-Path $PSScriptRoot "Package.appxmanifest"
$packagePath = Join-Path $outputDirectory "Bticon-$Version-x64.msix"

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
            $graphics.Clear([System.Drawing.Color]::FromArgb(16, 40, 64))
            $scale = $size / 44.0
            $outline = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [Math]::Max(1.0, 2.0 * $scale))
            $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(46, 204, 113))
            $terminal = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
            try {
                $bodyX = [int][Math]::Round(8 * $scale)
                $bodyY = [int][Math]::Round(13 * $scale)
                $bodyWidth = [int][Math]::Round(26 * $scale)
                $bodyHeight = [int][Math]::Round(18 * $scale)
                $graphics.DrawRectangle($outline, $bodyX, $bodyY, $bodyWidth, $bodyHeight)
                $graphics.FillRectangle($terminal, $bodyX + $bodyWidth, $bodyY + [int][Math]::Round(5 * $scale), [int][Math]::Round(3 * $scale), [int][Math]::Round(8 * $scale))
                $graphics.FillRectangle($fill, $bodyX + [int][Math]::Round(3 * $scale), $bodyY + [int][Math]::Round(3 * $scale), [int][Math]::Round(17 * $scale), [int][Math]::Round(12 * $scale))
            }
            finally {
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
    if ($PackageName -eq "Bticon.LocalTest") {
        Write-Warning "El paquete usa una identidad de prueba local; reserva el producto en Partner Center y reemplaza la identidad antes de enviarlo a Microsoft Store."
    }
}
finally {
    if (Test-Path $workDirectory) {
        Remove-Item $workDirectory -Recurse -Force
    }
}
