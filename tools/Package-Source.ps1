param([string]$Version='2026.10.31.1',[string]$ArchiveDirectory='')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+\.\d+$'){throw 'Use a four-part version.'}
$clientRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if(!$ArchiveDirectory){$ArchiveDirectory=Join-Path $clientRoot 'artifacts'}
New-Item -ItemType Directory -Path $ArchiveDirectory -Force | Out-Null
$files=@('README.md','SmartHunter.sln','.gitignore','.gitattributes','tools/Package-Client.ps1','tools/Package-Source.ps1')
foreach($name in @('LICENSE.txt','.editorconfig')){if(Test-Path -LiteralPath (Join-Path $clientRoot $name)){$files+=$name}}
foreach($folder in @('SmartHunter','SmartHunter.Tests','SmartHunter.LegacyBootstrap')){
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $clientRoot $folder) -File -Recurse){
        $relative=$file.FullName.Substring($clientRoot.Length+1).Replace('\','/')
        if($relative -match '/(bin|obj)/' -or $relative -match '\.(user|suo)$'){continue}
        $files+=$relative
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archivePath=Join-Path $ArchiveDirectory "SmartHunter-Unified-Source-$Version.zip"
$archive=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
try{foreach($name in $files){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,(Join-Path $clientRoot $name),$name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}}finally{$archive.Dispose()}
Write-Output $archivePath
