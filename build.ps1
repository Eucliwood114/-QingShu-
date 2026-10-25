param([switch]$WithTests)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework compiler was not found.' }
$assetsDir = Join-Path $projectDir 'assets'
[void](New-Item -ItemType Directory -Path $assetsDir -Force)
Add-Type -AssemblyName System.Drawing
if (-not ('QingShuBuildNative' -as [type])) {
    Add-Type 'public static class QingShuBuildNative { [System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool DestroyIcon(System.IntPtr icon); }'
}
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$accentBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(79,70,229))
$whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$graphics.FillEllipse($accentBrush, 1, 1, 62, 62)
$graphics.FillRectangle($whiteBrush, 14, 23, 36, 24)
$graphics.FillRectangle($accentBrush, 18, 27, 6, 5)
$graphics.FillRectangle($accentBrush, 29, 27, 6, 5)
$graphics.FillRectangle($accentBrush, 40, 27, 6, 5)
$graphics.FillRectangle($accentBrush, 18, 37, 28, 5)
$iconHandle = $bitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$iconPath = Join-Path $assetsDir 'qingshu.ico'
$iconStream = [System.IO.File]::Create($iconPath)
try { $icon.Save($iconStream) } finally {
    $iconStream.Dispose()
    $icon.Dispose()
    [void][QingShuBuildNative]::DestroyIcon($iconHandle)
    $graphics.Dispose()
    $bitmap.Dispose()
    $accentBrush.Dispose()
    $whiteBrush.Dispose()
}
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$executableName = [string]::Concat([char]0x8F7B, [char]0x8F93) + '.exe'
$executable = Join-Path $projectDir $executableName
$compilerArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/langversion:5',
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Core.dll',
    "/win32manifest:$(Join-Path $projectDir 'src\app.manifest')", "/win32icon:$iconPath", "/out:$executable")
& $compiler @compilerArgs @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Write-Output "Built: $executable"
if ($WithTests) {
    $artifactsDir = Join-Path $projectDir 'artifacts'
    [void](New-Item -ItemType Directory -Path $artifactsDir -Force)
    $testExe = Join-Path $artifactsDir 'QingShu.Tests.exe'
    $testArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/codepage:65001', '/langversion:5',
        '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll', '/reference:System.Core.dll',
        '/main:QingShu.Tests.SmokeProgram', "/win32manifest:$(Join-Path $projectDir 'src\app.manifest')", "/win32icon:$iconPath", "/out:$testExe")
    & $compiler @testArgs @sourceFiles (Join-Path $projectDir 'tests\SmokeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    Write-Output "Built: $testExe"
}
