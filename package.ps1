param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputDirectory $OutputDirectory
& (Join-Path $PSScriptRoot 'tests\run-tests.ps1')
$destination=[IO.Path]::GetFullPath($OutputDirectory)
$versionSource=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'src\Models.cs') -Raw
$versionMatch=[regex]::Match($versionSource,'public const string Version = "([0-9]+\.[0-9]+\.[0-9]+[-A-Za-z0-9.]*)";')
if(-not $versionMatch.Success){throw 'Could not read the release version.'}
$releaseVersion=$versionMatch.Groups[1].Value
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-ReleaseZip([string]$name,[object[]]$items){
  $zipPath=Join-Path $destination $name
  if(Test-Path -LiteralPath $zipPath){Remove-Item -LiteralPath $zipPath}
  $stream=[IO.File]::Open($zipPath,[IO.FileMode]::CreateNew)
  $archive=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
  try{foreach($item in $items){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$item.Source,$item.Entry,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose();$stream.Dispose()}
  return $zipPath
}
$binaryEntries=@(foreach($name in @('WinProfileRepair.exe','README.md','LICENSE','SECURITY.md','SHA256SUMS.txt')){@{Source=(Join-Path $destination $name);Entry=$name}})
if(Test-Path -LiteralPath (Join-Path $destination 'assets\screenshot.png')){$binaryEntries+=@{Source=(Join-Path $destination 'assets\screenshot.png');Entry='assets/screenshot.png'}}
$binary=Write-ReleaseZip ('WinProfileRepair-v'+$releaseVersion+'-win-x64.zip') $binaryEntries
$sourceRoot=(Resolve-Path -LiteralPath $PSScriptRoot).Path
$sourceEntries=@(Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force | Where-Object {$relative=$_.FullName.Substring($sourceRoot.Length+1);$relative -match '^(src|tests|assets|scripts|\.github)\\' -or $relative -in @('build.ps1','package.ps1','README.md','LICENSE','SECURITY.md','.gitignore','.gitattributes','CHANGELOG.md')} | ForEach-Object {@{Source=$_.FullName;Entry=('WinProfileRepair/'+$_.FullName.Substring($sourceRoot.Length+1).Replace('\','/'))}})
$source=Write-ReleaseZip ('WinProfileRepair-v'+$releaseVersion+'-source.zip') $sourceEntries
Get-FileHash -LiteralPath (Join-Path $destination 'WinProfileRepair.exe'),$binary,$source -Algorithm SHA256 | ForEach-Object {$_.Hash+'  '+[IO.Path]::GetFileName($_.Path)} | Set-Content -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Encoding ASCII
Write-Output $binary
Write-Output $source
