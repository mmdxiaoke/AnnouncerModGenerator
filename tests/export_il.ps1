param([string]$PackagePath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\Mono.Cecil.dll')
$taskZip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $taskEntry = $taskZip.Entries | Where-Object FullName -match '^bin/.*\.dll$' | Select-Object -First 1
    $taskMemory = [IO.MemoryStream]::new()
    $taskStream = $taskEntry.Open()
    $taskStream.CopyTo($taskMemory)
    $taskStream.Dispose()
    $taskMemory.Position = 0
    $taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskMemory)
    $taskType = $taskAssembly.MainModule.Types | Where-Object Name -eq TechAnnouncerModule
    $taskResult = @{}
    foreach ($taskMethod in $taskType.Methods | Where-Object Name -in @('Player_CallDashEvents','Player_CorrectDashPrecision','Player_WallJump')) {
        $taskResult[$taskMethod.Name] = @($taskMethod.Body.Instructions | ForEach-Object {
            $taskOperand = $_.Operand
            if ($taskOperand -is [Mono.Cecil.Cil.Instruction]) { $taskOperand = $taskMethod.Body.Instructions.IndexOf($taskOperand) }
            elseif ($taskOperand -is [Mono.Cecil.MemberReference]) { $taskOperand = $taskOperand.FullName }
            [PSCustomObject]@{ op = $_.OpCode.Name; operand = $taskOperand }
        })
    }
    $taskResult['assembly'] = $taskAssembly.Name.Name
    $taskResult['old_namespace_references'] = @($taskAssembly.MainModule.GetTypeReferences() | Where-Object Namespace -eq 'Celeste.Mod.TechAnnouncer').Count
    $taskResult | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    $taskMemory.Dispose()
} finally { $taskZip.Dispose() }
