param([string]$ModulePath,[string]$GamePath,[string]$AudioDirectory)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $taskRoot 'dist\Mono.Cecil.dll')
$taskRuntime=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModulePath)
$taskAssemblies=@{}
foreach($taskName in @('Celeste','FNA','MMHOOK_Celeste')) {
 $taskAssemblies[$taskName]=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath ($taskName+'.dll')))
}
function Find-Type($reference) {
 $taskScope=$reference.Scope.Name
 if($taskAssemblies.ContainsKey($taskScope)){return $taskAssemblies[$taskScope].MainModule.GetType($reference.FullName)}
 return $null
}
foreach($taskType in $taskRuntime.MainModule.GetTypeReferences()) {
 if($taskAssemblies.ContainsKey($taskType.Scope.Name)) {
  $taskActual=Find-Type $taskType
  if(!$taskActual){throw "Missing type: $taskType"}

 }
}
foreach($taskName in @('Celeste','FNA')) {
 $taskStub=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $taskRoot ('dependencies\'+$taskName+'.dll')))
 foreach($taskType in $taskStub.MainModule.GetTypes()) {
  $taskActual=$taskAssemblies[$taskName].MainModule.GetType($taskType.FullName)
  if($taskActual -and $taskActual.IsValueType -ne $taskType.IsValueType){throw "Stub value type mismatch: $taskType"}
 }
}
# Validate contracts accessed through reflection as well as direct member refs.
$taskDuck=$taskAssemblies['Celeste'].MainModule.GetType('Celeste.Player').Properties | Where-Object Name -eq 'Ducking'
if (!$taskDuck -or !$taskDuck.SetMethod.IsPublic) { throw 'Public Player.Ducking setter unavailable.' }
$taskEverest=$taskAssemblies['Celeste'].MainModule.GetType('Celeste.Mod.EverestModule')
if (!@($taskEverest.Methods | Where-Object { $_.Name -eq 'Initialize' -and $_.IsVirtual }).Count) { throw 'EverestModule.Initialize unavailable.' }
$taskDetour=[Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GamePath 'MonoMod.RuntimeDetour.dll'))
$taskHook=$taskDetour.MainModule.GetType('MonoMod.RuntimeDetour.Hook')
if (!@($taskHook.Methods | Where-Object { $_.Name -eq '.ctor' -and $_.IsPublic -and $_.Parameters.Count -eq 2 -and $_.Parameters[0].ParameterType.FullName -eq 'System.Reflection.MethodBase' -and $_.Parameters[1].ParameterType.FullName -eq 'System.Delegate' }).Count) { throw 'RuntimeDetour reflection constructor unavailable.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskFrostZip=[IO.Compression.ZipFile]::OpenRead((Join-Path $GamePath 'Mods\FrostHelper.zip'))
try {
 $taskBuffer=[IO.MemoryStream]::new();$taskInput=$taskFrostZip.GetEntry('Code/FrostHelper/bin/FrostTempleHelper.dll').Open()
 try { $taskInput.CopyTo($taskBuffer) } finally { $taskInput.Dispose() }
 $taskBuffer.Position=0;$taskFrost=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskBuffer)
} finally { $taskFrostZip.Dispose() }
$taskBooster=$taskFrost.MainModule.GetType('FrostHelper.Entities.Boosters.GenericCustomBooster')
$taskUpdate=$taskBooster.Methods | Where-Object { $_.Name -eq 'BoostUpdate' -and $_.IsStatic -and $_.IsPublic -and $_.ReturnType.FullName -eq 'System.Int32' -and $_.Parameters.Count -eq 1 -and $_.Parameters[0].ParameterType.FullName -eq 'Monocle.Entity' }
if (!$taskUpdate) { throw 'FrostHelper update contract mismatch.' }
foreach($taskStateName in @('CustomBoostState','CustomRedBoostState')) {
 if(!@($taskBooster.Fields | Where-Object { $_.Name -eq $taskStateName -and $_.IsStatic -and $_.IsPublic -and $_.FieldType.FullName -eq 'System.Int32' }).Count){throw 'FrostHelper state contract mismatch.'}
}
$taskBegin=$taskBooster.Events | Where-Object Name -eq 'OnBoostBegin'
if(!$taskBegin -or $taskBegin.EventType.FullName -ne 'System.Action`2<Celeste.Player,Monocle.Entity>'){throw 'FrostHelper begin event contract mismatch.'}
foreach($taskColor in @('BlueBooster','YellowBooster')) {
 $taskColorType=$taskFrost.MainModule.GetType('FrostHelper.Entities.Boosters.'+$taskColor)
 if(!$taskColorType -or $taskColorType.BaseType.FullName -ne $taskBooster.FullName){throw 'FrostHelper colored booster contract mismatch.'}
}
Write-Output 'Installed optional FrostHelper and RuntimeDetour API contracts verified.'
$taskChecks=0
foreach($taskMember in $taskRuntime.MainModule.GetMemberReferences()) {
 $taskType=Find-Type $taskMember.DeclaringType
 if(!$taskType){continue}
 $taskFound=$false
 while($taskType -and !$taskFound) {
  if($taskMember -is [Mono.Cecil.MethodReference]) {
   $taskMatches=@($taskType.Methods | Where-Object { $_.Name -eq $taskMember.Name -and $_.Parameters.Count -eq $taskMember.Parameters.Count -and $_.GenericParameters.Count -eq $taskMember.GenericParameters.Count })
   foreach($taskMatch in $taskMatches) {
    $taskSame=$taskMatch.ReturnType.FullName -eq $taskMember.ReturnType.FullName
    for($taskIndex=0;$taskIndex -lt $taskMember.Parameters.Count;$taskIndex++) {
     if($taskMatch.Parameters[$taskIndex].ParameterType.FullName -ne $taskMember.Parameters[$taskIndex].ParameterType.FullName){$taskSame=$false}
    }
    if($taskSame){$taskFound=$true;break}
   }
  } else {
   $taskFound=@($taskType.Fields | Where-Object { $_.Name -eq $taskMember.Name -and $_.FieldType.FullName -eq $taskMember.FieldType.FullName }).Count -gt 0
  }
  if(!$taskFound){$taskType=Find-Type $taskType.BaseType}
 }
 if(!$taskFound){throw "Missing or incompatible game member: $taskMember"}
 $taskChecks++
}
New-Item -ItemType Directory -Force -Path $AudioDirectory | Out-Null
foreach($taskResource in $taskRuntime.MainModule.Resources) {
 if($taskResource.Name.EndsWith('.wav')){[IO.File]::WriteAllBytes((Join-Path $AudioDirectory $taskResource.Name),$taskResource.GetResourceData())}
}
if(@($taskRuntime.MainModule.AssemblyReferences | Where-Object Name -match 'TechAnnouncer').Count){throw 'Unexpected third-party module reference'}
Write-Output "Installed game API verified: $taskChecks members; independent WAV resources exported."
