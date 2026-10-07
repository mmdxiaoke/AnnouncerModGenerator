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
