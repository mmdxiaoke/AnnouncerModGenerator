param([string]$CecilPath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskDist = Join-Path $taskRoot 'dist'
$taskDependencies = Join-Path $taskRoot 'dependencies'
New-Item -ItemType Directory -Force -Path $taskDist,$taskDependencies | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not $CecilPath) {
    $taskPackage = Join-Path $taskDependencies 'mono.cecil.0.10.4.nupkg'
    $taskHash = '3451a4a112a81be0bf76b8dc2bca396acdf0455d80c8961e7903ce1fb35c2a9a'
    if (-not (Test-Path -LiteralPath $taskPackage)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $taskDownloadUrl = 'https://api.nuget.org/v3-flatcontainer/mono.cecil/0.10.4/mono.cecil.0.10.4.nupkg'
        if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
            & curl.exe --fail --location --silent --show-error --max-time 120 --output $taskPackage $taskDownloadUrl
            if ($LASTEXITCODE -ne 0) { throw 'Mono.Cecil download failed. Try -CecilPath for offline compilation.' }
        } else {
            Invoke-WebRequest -UseBasicParsing -Uri $taskDownloadUrl -OutFile $taskPackage
        }
    }
    if ((Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskHash) { throw 'Mono.Cecil package checksum mismatch.' }
    $taskArchive = [IO.Compression.ZipFile]::OpenRead($taskPackage)
    try {
        $taskEntry = $taskArchive.GetEntry('lib/net40/Mono.Cecil.dll')
        $taskDestination = Join-Path $taskDist 'Mono.Cecil.dll'
        $taskInput = $taskEntry.Open()
        try {
            $taskOutput = [IO.File]::Create($taskDestination)
            try { $taskInput.CopyTo($taskOutput) } finally { $taskOutput.Dispose() }
        } finally { $taskInput.Dispose() }
    } finally { $taskArchive.Dispose() }
} else {
    Copy-Item -LiteralPath $CecilPath -Destination (Join-Path $taskDist 'Mono.Cecil.dll') -Force
}
Unblock-File -LiteralPath (Join-Path $taskDist 'Mono.Cecil.dll')
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Windows .NET Framework compiler not found.' }
Push-Location $taskRoot
try {
    # Compile only original API declarations; no game files are bundled or needed.
    & $taskCompiler /nologo /target:library /out:dependencies\FNA.dll tests\stubs\FNA.cs
    if ($LASTEXITCODE -ne 0) { throw 'API declaration compilation failed.' }
    & $taskCompiler /nologo /target:library /out:dependencies\Celeste.dll /reference:dependencies\FNA.dll tests\stubs\Celeste.cs
    if ($LASTEXITCODE -ne 0) { throw 'API declaration compilation failed.' }
    & $taskCompiler /nologo /target:library /out:dependencies\MMHOOK_Celeste.dll /reference:dependencies\Celeste.dll /reference:dependencies\FNA.dll tests\stubs\MMHOOK_Celeste.cs
    if ($LASTEXITCODE -ne 0) { throw 'Hook declaration compilation failed.' }
    & $taskCompiler /nologo /target:library /optimize+ /out:dependencies\AnnouncerRuntime.dll /reference:dependencies\Celeste.dll /reference:dependencies\FNA.dll /reference:dependencies\MMHOOK_Celeste.dll src\IndependentAnnouncer.cs
    if ($LASTEXITCODE -ne 0) { throw 'Runtime compilation failed.' }
    & $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ '/out:dist\AnnouncerMod生成器.exe' /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Web.Extensions.dll '/reference:dist\Mono.Cecil.dll' /resource:dependencies\AnnouncerRuntime.dll,runtime.support 'src\AnnouncerBuilder.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
} finally { Pop-Location }
Write-Output "Built successfully: $taskDist"
