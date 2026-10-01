param([string]$BasePackage,[switch]$Release)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
if([IO.File]::ReadAllText((Join-Path $root 'src/Plugin.cs')).Contains('Gateway.Register')){throw 'This source needs the new Gateway asset bundle. Use tools/BuildGatewayPreview.ps1; the old 0.8.1 bundle is incompatible.'}
$paths=Import-PowerShellDataFile (Join-Path $root '.local/BuildPaths.psd1')
$work=Join-Path $root 'artifacts/api-preview'
$dist=Join-Path $root 'dist'
New-Item -ItemType Directory -Force $work,$dist | Out-Null
& (Join-Path $PSScriptRoot 'Compile.ps1') -Output (Join-Path $work 'bin')
& (Join-Path $PSScriptRoot 'Compile.ps1') -Tests -Output (Join-Path $work 'tests') *> (Join-Path $work 'tests.log')

# Check the installed packet framing without executing game code.
Add-Type -Path (Join-Path $paths.EditorData 'Managed/Unity.Cecil.dll')
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $paths.ValheimManaged 'assembly_valheim.dll'))
try {
 $type=$game.MainModule.Types | Where-Object Name -eq 'ZDOMan'
 $method=$type.Methods | Where-Object Name -eq 'RPC_ZDOData'
 $calls=@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
 $reads=@($method.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.Name -eq 'ZPackage' -and $_.Operand.Name.StartsWith('Read')} | ForEach-Object {$_.Operand.Name})
 $sequence='ReadInt,ReadZDOID,ReadZDOID,ReadUShort,ReadUInt,ReadLong,ReadVector3,ReadPackage'
 if(($reads -join ',') -cne $sequence -or $calls -notcontains 'Deserialize'){throw 'Installed ZDO protocol needs review before building this API'}
 $package=$game.MainModule.Types | Where-Object Name -eq 'ZPackage'
 $read=$package.Methods | Where-Object {$_.Name -eq 'ReadPackage' -and $_.Parameters.Count -eq 1}
 $readCalls=@($read.Body.Instructions | Where-Object {$_.Operand -is [Mono.Cecil.MethodReference]} | ForEach-Object {$_.Operand.Name})
 if(($readCalls -join ',') -notmatch 'ReadInt32,ReadBytes'){throw 'Installed nested ZDO package framing changed'}
}finally{$game.Dispose()}

# Build the standalone consumer using the exact same compile-only references.
$rsp=Get-Content -LiteralPath (Join-Path $work 'bin/RunicStorageNetwork.dll.rsp')
$probe=Join-Path $work 'RSN.ApiTest.dll'
$probeLines=@('/nologo','/nostdlib+','/langversion:9','/deterministic+','/optimize+','/target:library',('/out:"'+$probe+'"'))
$probeLines+=@($rsp | Where-Object {$_ -like '/reference:*'})
$probeLines+=('/reference:"'+(Join-Path $work 'bin/RunicStorageNetwork.dll')+'"')
$probeLines+=('"'+(Join-Path $root 'examples/ApiTestMod/ApiTestMod.cs')+'"')
$probeRsp=Join-Path $work 'ApiTest.rsp'
[IO.File]::WriteAllLines($probeRsp,$probeLines)
& (Join-Path $paths.EditorData 'NetCoreRuntime/dotnet.exe') (Join-Path $paths.EditorData 'DotNetSdkRoslyn/csc.dll') "@$probeRsp"
if($LASTEXITCODE -ne 0){throw 'API test mod compilation failed'}

if(!$BasePackage){$BasePackage=Join-Path $root 'dist/RunicStorageNetwork-0.8.1.zip'}
if((Get-FileHash -LiteralPath $BasePackage -Algorithm SHA256).Hash -ne '90DBA4E2C9E36D2BA387527EF41BEF6006558311942C05FBB52F03967721E270'){throw 'Expected original released 0.8.1 package as the asset source'}
$version=(Get-Content -LiteralPath (Join-Path $root 'manifest.json') -Raw | ConvertFrom-Json).version_number
$suffix=if($Release){''}else{'-api-preview.1'}
$package=Join-Path $dist "RunicStorageNetwork-$version$suffix.zip"
$testPackage=Join-Path $dist 'RunicStorageNetwork-ApiTest-0.1.0.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Add-File($zip,[string]$source,[string]$entry){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$source,$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null}
# Only replace these two explicit generated artifacts, never a game profile.
foreach($file in @($package,$testPackage)){if(Test-Path -LiteralPath $file){Remove-Item -LiteralPath $file}}
$source=[IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $BasePackage).Path)
try {
 $zip=[IO.Compression.ZipFile]::Open($package,[IO.Compression.ZipArchiveMode]::Create)
 try {
  foreach($name in @('icon.png','plugins/RunicStorageNetwork/Assets/rsn_core_windows')){
   $entry=$source.GetEntry($name);if(!$entry){throw "Missing approved asset $name"}
   $inputStream=$entry.Open();$outputStream=$zip.CreateEntry($name).Open()
   try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
  }
  foreach($pair in @(@('manifest.json','manifest.json'),@('README.md','README.md'),@('CHANGELOG_EN.md','CHANGELOG.md'),@('artifacts/api-preview/bin/RunicStorageNetwork.dll','plugins/RunicStorageNetwork/RunicStorageNetwork.dll'))){Add-File $zip (Join-Path $root $pair[0]) $pair[1]}
 }finally{$zip.Dispose()}
}finally{$source.Dispose()}
& (Join-Path $PSScriptRoot 'ValidateRelease.ps1') -Package $package -Tag "v$version"
$zip=[IO.Compression.ZipFile]::Open($testPackage,[IO.Compression.ZipArchiveMode]::Create)
try{Add-File $zip $probe 'plugins/RSN.ApiTest/RSN.ApiTest.dll';Add-File $zip (Join-Path $root 'examples/ApiTestMod/README.md') 'README.md'}finally{$zip.Dispose()}
Write-Output "Mod package: $package"
Write-Output "Local consumer: $testPackage"
Get-FileHash -LiteralPath $package,$testPackage -Algorithm SHA256 | Format-Table -AutoSize
