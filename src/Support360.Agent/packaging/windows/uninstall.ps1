param([switch]$RemoveIdentity)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this uninstaller from an elevated PowerShell session.' }
$serviceName = 'Support360Agent'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
  if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
  & sc.exe delete $serviceName | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Could not remove the Windows service.' }
  for ($i = 0; $i -lt 30 -and (Get-Service -Name $serviceName -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
}
Remove-Item -LiteralPath (Join-Path $env:ProgramFiles 'Support360\Agent') -Recurse -Force -ErrorAction SilentlyContinue
$dataDir = Join-Path $env:ProgramData 'Support360\Agent'
if ($RemoveIdentity) {
  Remove-Item -LiteralPath $dataDir -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath (Join-Path $env:ProgramData 'Support360') -Force -ErrorAction SilentlyContinue
} else {
  Write-Output "Service and binaries removed. Identity and queues remain at $dataDir. Reinstall or use -RemoveIdentity to remove credentials and queued events."
}
