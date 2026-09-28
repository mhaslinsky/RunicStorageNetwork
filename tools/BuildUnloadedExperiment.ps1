param([string]$BasePackage)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
if(!$BasePackage){$BasePackage=Join-Path $root 'dist/RunicStorageNetwork-0.8.1.zip'}
# Reuse the released models and icons. This code-only experiment does not run
# Unity or touch a game profile; compile references remain outside the archive.
if((Get-FileHash -LiteralPath $BasePackage -Algorithm SHA256).Hash -ne '90DBA4E2C9E36D2BA387527EF41BEF6006558311942C05FBB52F03967721E270'){
 throw 'Expected the original RunicStorageNetwork 0.8.1 package as the asset source'
}
$work=Join-Path $root 'artifacts/unloaded-networks'
New-Item -ItemType Directory -Force $work | Out-Null
& (Join-Path $PSScriptRoot 'Compile.ps1') -Output (Join-Path $work 'bin')
& (Join-Path $PSScriptRoot 'Compile.ps1') -Output (Join-Path $work 'tests') -Tests *> (Join-Path $work 'tests.log')
$destination=Join-Path $root 'dist'
New-Item -ItemType Directory -Force $destination | Out-Null
$package=Join-Path $destination 'RunicStorageNetwork-0.8.2-unloaded-experiment.zip'
$temporary=Join-Path $destination ('unloaded-experiment-'+[guid]::NewGuid().ToString('N')+'.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$source=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BasePackage).Path)
try {
 $archive=[IO.Compression.ZipFile]::Open($temporary,[IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach($name in @('icon.png','plugins/RunicStorageNetwork/Assets/rsn_core_windows')){
   $entry=$source.GetEntry($name);if(!$entry){throw "Missing released asset: $name"}
   $inputStream=$entry.Open();$outputStream=$archive.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal).Open()
   try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
  }
  foreach($pair in @(@('manifest.json','manifest.json'),@('README.md','README.md'),@('CHANGELOG_EN.md','CHANGELOG.md'),@('artifacts/unloaded-networks/bin/RunicStorageNetwork.dll','plugins/RunicStorageNetwork/RunicStorageNetwork.dll'))){
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $root $pair[0]),$pair[1],[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
 }finally{$archive.Dispose()}
 & (Join-Path $PSScriptRoot 'ValidateRelease.ps1') -Package $temporary -Tag v0.8.2
 Move-Item -LiteralPath $temporary -Destination $package -Force
 Write-Output "Local experimental package: $package"
}finally{$source.Dispose();if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary}}
