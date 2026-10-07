param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) { $OutputPath = Join-Path $taskRoot 'dist\AnnouncerModGenerator-v1.3.0.zip' }
$taskOutputFull = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $taskOutputFull) { throw 'Output already exists; choose a new path.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$taskFiles = @{
    'AnnouncerModGenerator/AnnouncerMod生成器.exe' = 'dist/AnnouncerMod生成器.exe'
    'AnnouncerModGenerator/Mono.Cecil.dll' = 'dist/Mono.Cecil.dll'
    'AnnouncerModGenerator/README.md' = 'README.md'
    'AnnouncerModGenerator/LICENSE' = 'LICENSE'
    'AnnouncerModGenerator/THIRD-PARTY-NOTICES.txt' = 'THIRD-PARTY-NOTICES.txt'
    'AnnouncerModGenerator/examples/inputs.json' = 'examples/inputs.json'
}
foreach ($taskFile in $taskFiles.Values) { if (-not (Test-Path -LiteralPath (Join-Path $taskRoot $taskFile))) { throw "Missing: $taskFile; run scripts/build.ps1 first." } }
$taskArchive = [IO.Compression.ZipFile]::Open($taskOutputFull, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskName in $taskFiles.Keys) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, (Join-Path $taskRoot $taskFiles[$taskName]), $taskName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskArchive.Dispose() }
Write-Output "Packaged: $taskOutputFull"
