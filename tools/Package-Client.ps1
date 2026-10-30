param(
    [string]$Version='2026.10.31.1',
    [string]$DotnetPath='dotnet',
    [string]$BuildOutput='',
    [string]$ReleaseDirectory='',
    [string]$ArchiveDirectory='',
    [switch]$NoRestore
)
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'Use a four-part version.'}
$clientRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$BuildOutput){$BuildOutput=Join-Path $clientRoot 'artifacts/client-build'}
if(!$ReleaseDirectory){$ReleaseDirectory=Join-Path $clientRoot 'release'}
$BuildOutput=[IO.Path]::GetFullPath($BuildOutput)
$ReleaseDirectory=[IO.Path]::GetFullPath($ReleaseDirectory)
if($ReleaseDirectory -eq $clientRoot -or $ReleaseDirectory -eq $BuildOutput){throw 'The release directory must be separate from the source and build directories.'}
New-Item -ItemType Directory -Path $BuildOutput,$ReleaseDirectory -Force | Out-Null
$buildArguments=@('build',(Join-Path $clientRoot 'SmartHunter/SmartHunter.csproj'),'-c','Release',"-p:OutputPath=$BuildOutput/",'-p:Platform=x64',"-p:AssemblyVersion=$Version","-p:FileVersion=$Version")
if($NoRestore){$buildArguments+='--no-restore'}
& $DotnetPath @buildArguments
if($LASTEXITCODE -ne 0){throw 'Client build failed.'}
$payload=@('SmartHunter.exe','SmartHunter.exe.config','Themes/Default.xaml','Newtonsoft.Json.dll','Microsoft.Xaml.Behaviors.dll','OxyPlot.dll','OxyPlot.Wpf.dll')
$digests=[ordered]@{};$sizes=[ordered]@{}
foreach($name in $payload){$path=Join-Path $BuildOutput $name;$digests[$name]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant();$sizes[$name]=(Get-Item -LiteralPath $path).Length}
if([Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $BuildOutput 'SmartHunter.exe')).FileVersion -ne $Version){throw 'Executable version does not match the requested release.'}
$manifest=[ordered]@{Product='SmartHunter-Unified';Version=$Version;Files=$digests;Sizes=$sizes}|ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $BuildOutput 'UpdateManifest.json'),$manifest,(New-Object Text.UTF8Encoding($false)))
foreach($name in $payload+@('UpdateManifest.json')){
    $target=Join-Path $ReleaseDirectory $name
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $BuildOutput $name) -Destination $target -Force
}
Write-Output $ReleaseDirectory
# Archives are optional local delivery/legacy-bootstrap artifacts. The GitHub
# update source is always the uncompressed release/ directory.
if(!$ArchiveDirectory){return}
$ArchiveDirectory=[IO.Path]::GetFullPath($ArchiveDirectory)
New-Item -ItemType Directory -Path $ArchiveDirectory -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath=Join-Path $ArchiveDirectory "SmartHunter-Unified-$Version.zip"
$archive=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($name in $payload+@('UpdateManifest.json')){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $BuildOutput $name),$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
Write-Output $archivePath
