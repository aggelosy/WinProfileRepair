param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'The 64-bit .NET Framework compiler is required. Target: Windows 10/11 x64 with .NET Framework 4.8.'}
$destination=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $destination -Force|Out-Null
& (Join-Path $PSScriptRoot 'scripts\make-icon.ps1')
$sources=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
$arguments=@('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/warnaserror+',('/out:'+(Join-Path $destination 'WinProfileRepair.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'src\app.manifest')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll')
$arguments+=('/win32icon:'+(Join-Path $PSScriptRoot 'assets\app.ico'))
& $compiler @arguments @sources
if($LASTEXITCODE -ne 0){throw ('Build failed with exit code '+$LASTEXITCODE)}
foreach($file in @('README.md','LICENSE','SECURITY.md','CHANGELOG.md','ROADMAP.md')){
  Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $destination $file) -Force
}
$screenshot=Join-Path $PSScriptRoot 'assets\screenshot.png'
if(Test-Path -LiteralPath $screenshot){
  New-Item -ItemType Directory -Path (Join-Path $destination 'assets') -Force|Out-Null
  Copy-Item -LiteralPath $screenshot -Destination (Join-Path $destination 'assets\screenshot.png') -Force
}
Get-FileHash -LiteralPath (Join-Path $destination 'WinProfileRepair.exe') -Algorithm SHA256 | ForEach-Object {$_.Hash+'  WinProfileRepair.exe'} | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding ASCII
Write-Output ('Built '+(Join-Path $destination 'WinProfileRepair.exe'))
