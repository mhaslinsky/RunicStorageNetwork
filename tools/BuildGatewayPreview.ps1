param(
 [string]$UnityEditor,
 [string]$UnityProject,
 [string]$Output
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
$paths=Import-PowerShellDataFile (Join-Path $root '.local/BuildPaths.psd1')
if(!$UnityEditor){$UnityEditor=Join-Path (Split-Path $paths.EditorData -Parent) 'Unity.exe'}
if(!$UnityProject){$UnityProject=Join-Path $root 'UnityBuild'}
if(!$Output){$Output=Join-Path $root ('artifacts/gateway-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))}
$UnityProject=[IO.Path]::GetFullPath($UnityProject)
$Output=[IO.Path]::GetFullPath($Output)
$gameAssets=Join-Path $UnityProject 'Assets/RunicStorageGame'
$editorAssets=Join-Path $gameAssets 'Editor'
foreach($file in @($UnityEditor,(Join-Path $editorAssets 'BuildAssets.cs'),(Join-Path $editorAssets 'BuildTerminalAssets.cs'),(Join-Path $gameAssets 'CoreRuntime.shader'))){
 if(!(Test-Path -LiteralPath $file)){throw "Prepared, separate Unity asset project required: $file. See BUILDING.md."}
}
if((Get-Item -LiteralPath $UnityEditor).VersionInfo.ProductVersion -notlike '6000.0.75f1*'){throw 'Expected Unity Editor 6000.0.75f1'}
foreach($process in @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'")){
 if($process.CommandLine -like "*$UnityProject*"){throw 'Close this Unity build project before rebuilding it.'}
}
$source=Join-Path $root 'model-sources/gateway'
$before=@{};foreach($file in Get-ChildItem -LiteralPath $source -File){$before[$file.FullName]=(Get-FileHash -LiteralPath $file.FullName).Hash}
New-Item -ItemType Directory -Force $Output,(Join-Path $Output 'assets'),(Join-Path $gameAssets 'Gateway/Source') | Out-Null
& (Join-Path $PSScriptRoot 'Compile.ps1') -Output (Join-Path $Output 'bin')
& (Join-Path $PSScriptRoot 'Compile.ps1') -Tests -Output (Join-Path $Output 'tests') *> (Join-Path $Output 'tests.log')
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'VerifyApi.ps1')){& (Join-Path $PSScriptRoot 'VerifyApi.ps1') -Output $Output *> (Join-Path $Output 'api-check.log')}
foreach($name in @('GatewayAssetBuilder.cs','GatewayStoneFinish.cs','BuildGatewayAssets.cs','BuildIcons.cs')){
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $editorAssets $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'src/GatewayMaterials.cs') -Destination (Join-Path $editorAssets 'GatewayMaterials.cs') -Force
foreach($name in @('RunicGateway.fbx','materials.json','RG_Core_EmissionMask_128.png')){
 Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $gameAssets ('Gateway/Source/'+$name)) -Force
}
$unityLog=Join-Path $Output 'UnityEditor.log'
$assets=Join-Path $Output 'assets'
$arguments=@('-batchmode','-projectPath',('"'+$UnityProject+'"'),'-executeMethod','RunicStorage.Build.BuildAssets.GatewayBatch','-rsnOutput',('"'+$assets+'"'),'-logFile',('"'+$unityLog+'"'))
$editor=Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
$editor.WaitForExit();$editor.Refresh()
if($editor.ExitCode -ne 0 -or !(Select-String -LiteralPath $unityLog -SimpleMatch 'RSN_GATEWAY_BUILD_SUCCESS' -Quiet)){throw "Gateway Editor build failed: $unityLog"}
foreach($file in $before.Keys){if((Get-FileHash -LiteralPath $file).Hash -ne $before[$file]){throw "Source snapshot changed: $file"}}
$version=(Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$dist=Join-Path $root 'dist';New-Item -ItemType Directory -Force $dist | Out-Null
$package=Join-Path $dist "RunicStorageNetwork-$version-gateway-preview.zip"
$candidate=Join-Path $Output "RunicStorageNetwork-$version-gateway-preview.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::Open($candidate,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($pair in @(@((Join-Path $root 'manifest.json'),'manifest.json'),@((Join-Path $root 'README.md'),'README.md'),@((Join-Path $root 'CHANGELOG_EN.md'),'CHANGELOG.md'),@((Join-Path $assets 'icon.png'),'icon.png'),@((Join-Path $Output 'bin/RunicStorageNetwork.dll'),'plugins/RunicStorageNetwork/RunicStorageNetwork.dll'),@((Join-Path $assets 'rsn_core_windows'),'plugins/RunicStorageNetwork/Assets/rsn_core_windows'))){
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$pair[0],$pair[1],[IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }
}finally{$zip.Dispose()}
& (Join-Path $PSScriptRoot 'ValidateRelease.ps1') -Package $candidate -Tag "v$version"
Copy-Item -LiteralPath $candidate -Destination $package -Force
Write-Output "Local test package: $package"
Write-Output "Editor preview, checks and logs: $Output"
