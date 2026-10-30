param(
    [Parameter(Mandatory=$true)][string]$UnifiedRoot,
    [string]$DotnetPath='dotnet',
    [string]$OutputPath='',
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
$UnifiedRoot=[IO.Path]::GetFullPath($UnifiedRoot)
$release=Join-Path $UnifiedRoot 'release'
$project=Join-Path $UnifiedRoot 'SmartHunter.LegacyBootstrap/SmartHunter.LegacyBootstrap.csproj'
$manifestPath=Join-Path $release 'UpdateManifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version=[string]$manifest.Version
$payload=@('SmartHunter.exe','SmartHunter.exe.config','Themes/Default.xaml','Newtonsoft.Json.dll','Microsoft.Xaml.Behaviors.dll','OxyPlot.dll','OxyPlot.Wpf.dll')
if($manifest.Product -ne 'SmartHunter-Unified' -or $version -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'Invalid Unified release manifest.'}
foreach($field in @('Files','Sizes')){
    $names=@($manifest.$field.PSObject.Properties.Name | Sort-Object)
    if(($names -join '|') -cne (($payload | Sort-Object) -join '|')){throw 'Unexpected Unified payload file list.'}
}
foreach($name in $payload){
    $file=Join-Path $release $name
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $manifest.Files.$name -or (Get-Item -LiteralPath $file).Length -ne $manifest.Sizes.$name){throw "Release file verification failed: $name"}
}
if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $release 'SmartHunter.exe')).FileVersion -ne $version){throw 'Release EXE version mismatch.'}
if(!(Test-Path -LiteralPath $project)){throw 'The Unified bootstrap source project is missing.'}
if(!$OutputPath){$OutputPath=Join-Path $PSScriptRoot '../SmartHunter/bin/Debug/SmartHunter.exe'}
$OutputPath=[IO.Path]::GetFullPath($OutputPath)
if([IO.Path]::GetFileName($OutputPath) -cne 'SmartHunter.exe'){throw 'The old updater endpoint must be named SmartHunter.exe.'}
if($OutputPath -eq [IO.Path]::GetFullPath((Join-Path $release 'SmartHunter.exe'))){throw 'Do not replace the full Unified client with the bootstrap.'}
$work=Join-Path ([IO.Path]::GetTempPath()) ('SmartHunter-LegacyBuild/'+[Guid]::NewGuid().ToString('N'))
$build=Join-Path $work 'build'
New-Item -ItemType Directory -Path $build -Force | Out-Null
$archivePath=Join-Path $work "SmartHunter-Unified-$version.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($name in $payload+@('UpdateManifest.json')){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $release $name),$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
$arguments=@('build',$project,'-c','Release',"-p:UpdatePackage=$archivePath","-p:OutputPath=$build/","-p:AssemblyVersion=$version","-p:FileVersion=$version")
if($NoRestore){$arguments+='--no-restore'}
& $DotnetPath @arguments
if($LASTEXITCODE -ne 0){throw 'Legacy bootstrap build failed.'}
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($OutputPath)) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'SmartHunter.LegacyBootstrap.exe') -Destination $OutputPath -Force
Write-Output "Prepared the standalone $version upgrade entry point: $OutputPath"
Write-Output 'No Git commit or push was performed.'
