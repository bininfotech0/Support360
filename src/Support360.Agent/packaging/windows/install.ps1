param(
  [Parameter(Mandatory = $true)][string]$BinaryPath,
  [Parameter(Mandatory = $true)][string]$ServerUrl,
  [switch]$EnableDeployments,
  [string]$ManifestPublicKeyPath,
  [string]$DeploymentRoot
)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this installer from an elevated PowerShell session.' }
if ($ServerUrl -notmatch '^https://') { throw 'The Support360 server URL must use HTTPS.' }
$source = (Resolve-Path -LiteralPath $BinaryPath).Path
$installDir = Join-Path $env:ProgramFiles 'Support360\Agent'
$dataDir = Join-Path $env:ProgramData 'Support360\Agent'
$agentPath = Join-Path $installDir 'support360-agent.exe'
$identityPath = Join-Path $dataDir 'identity.json'
$queuePath = Join-Path $dataDir 'queue.json'
$configPath = Join-Path $dataDir 'service.json'
$serviceName = 'Support360Agent'
$manifestKeyPath = Join-Path $dataDir 'manifest-public.pem'
$deploymentRootPath = if ($DeploymentRoot) { $DeploymentRoot } else { Join-Path $dataDir 'deployments' }
if ($EnableDeployments) {
  if (-not $ManifestPublicKeyPath) { throw 'EnableDeployments requires -ManifestPublicKeyPath pointing to the server manifest public key PEM.' }
  if (-not [IO.Path]::IsPathRooted($deploymentRootPath)) { throw 'DeploymentRoot must be an absolute path.' }
  $manifestKeySource = (Resolve-Path -LiteralPath $ManifestPublicKeyPath).Path
}
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'Support360Agent is already installed. Uninstall it before reinstalling so the agent identity is not duplicated.' }

New-Item -ItemType Directory -Force -Path $installDir, $dataDir | Out-Null
Copy-Item -LiteralPath $source -Destination $agentPath -Force
$serviceSid = "NT SERVICE\$serviceName"
& icacls.exe $installDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' 'NT AUTHORITY\LOCAL SERVICE:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the agent installation directory.' }
& icacls.exe $dataDir /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "${serviceSid}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the agent data directory.' }

if ($EnableDeployments) {
  New-Item -ItemType Directory -Force -Path $deploymentRootPath | Out-Null
  & icacls.exe $deploymentRootPath /grant "${serviceSid}:(OI)(CI)M" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Could not grant the agent service access to the deployment root.' }
  Copy-Item -LiteralPath $manifestKeySource -Destination $manifestKeyPath -Force
  & icacls.exe $manifestKeyPath /inheritance:r /grant:r '*S-1-5-18:R' '*S-1-5-32-544:R' "${serviceSid}:R" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Could not secure the manifest public key.' }
}

$configValues = @{ server = $ServerUrl.TrimEnd('/'); identity = $identityPath; queue = $queuePath }
if ($EnableDeployments) { $configValues.manifestPublicKey = $manifestKeyPath; $configValues.deploymentRoot = $deploymentRootPath }
$config = $configValues | ConvertTo-Json
[IO.File]::WriteAllText($configPath, $config, [Text.UTF8Encoding]::new($false))
& icacls.exe $configPath /inheritance:r /grant:r '*S-1-5-18:F' '*S-1-5-32-544:F' "${serviceSid}:R" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the agent service configuration.' }

$secureToken = Read-Host 'Single-use Support360 enrollment token' -AsSecureString
$tokenPtr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
try {
  $env:SUPPORT360_ENROLLMENT_TOKEN = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPtr)
  $enrollArguments = @('enroll', '--server', $ServerUrl, '--identity', $identityPath)
  if ($EnableDeployments) { $enrollArguments += '--enable-deployments' }
  & $agentPath @enrollArguments
  if ($LASTEXITCODE -ne 0) { throw 'Agent enrollment failed; no service was installed.' }
}
finally {
  Remove-Item Env:\SUPPORT360_ENROLLMENT_TOKEN -ErrorAction SilentlyContinue
  [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPtr)
}

$binaryCommand = '"{0}" service --config "{1}"' -f $agentPath, $configPath
& sc.exe create $serviceName "binPath= $binaryCommand" 'start= auto' 'obj= NT AUTHORITY\LocalService' 'DisplayName=Support360 Monitoring Agent' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not create the Windows service.' }
& sc.exe sidtype $serviceName unrestricted | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
& sc.exe start $serviceName | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Service was installed but did not start. Inspect Event Viewer and the Support360 agent service status.' }
Write-Output 'Support360 agent enrolled and installed. Check it with: Get-Service Support360Agent'
