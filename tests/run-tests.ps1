$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifacts=Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force|Out-Null
$compiler=Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources=@(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
$sources+=Join-Path $PSScriptRoot 'TestProgram.cs'
$arguments=@('/nologo','/target:exe','/main:TestProgram','/platform:x64','/optimize+','/warn:4','/warnaserror+',('/out:'+(Join-Path $artifacts 'WinProfileRepair.Tests.exe')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:System.Management.dll')
$arguments+=('/win32icon:'+(Join-Path $root 'assets\app.ico'))
& $compiler @arguments @sources
if($LASTEXITCODE -ne 0){throw 'Test build failed.'}
& (Join-Path $artifacts 'WinProfileRepair.Tests.exe') $artifacts
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
