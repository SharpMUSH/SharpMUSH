# Launches SharpMUSH on Fly.io from a checkout of this repository, on Windows:
#
#   fly auth login
#   powershell -ExecutionPolicy Bypass -File deploy\fly\launch.ps1 my-game iad [-DedicatedIPv4]
#
# The same steps as launch.sh. Safe to run again: it creates only what is missing, then deploys. The
# app gets IPv6 and a shared IPv4 address, both free. The shared IPv4 carries only the web portal, so
# telnet is reachable over IPv6 alone; -DedicatedIPv4 adds an IPv4 address of the app's own ($2/month
# at Fly) for telnet.
param(
	[Parameter(Mandatory = $true)][string]$App,
	[string]$Region = 'iad',
	[switch]$DedicatedIPv4
)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$found = Get-Command fly, flyctl -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $found) {
	Write-Host 'Install flyctl first: https://fly.io/docs/flyctl/install/'
	exit 1
}
$fly = $found.Source

function Invoke-Fly {
	& $fly @args
	if ($LASTEXITCODE -ne 0) { throw "fly $args failed (exit code $LASTEXITCODE)" }
}

& $fly status --app $App *> $null
if ($LASTEXITCODE -ne 0) {
	Invoke-Fly apps create $App
}

$volumes = Invoke-Fly volumes list --app $App | Out-String
if ($volumes -notmatch 'sharpmush_data') {
	Invoke-Fly volumes create sharpmush_data --app $App --region $Region --size 10 --yes
}

function Test-DedicatedIPv4([string[]]$Lines) {
	[bool]($Lines | Where-Object { $_ -match 'v4' -and $_ -notmatch 'shared' })
}

$ips = @(Invoke-Fly ips list --app $App)
if ($DedicatedIPv4) {
	if (-not (Test-DedicatedIPv4 $ips)) { Invoke-Fly ips allocate-v4 --app $App --yes }
}
elseif (-not ($ips | Where-Object { $_ -match 'v4' })) {
	Invoke-Fly ips allocate-v4 --shared --app $App
}
if (-not ($ips | Where-Object { $_ -match 'v6' })) {
	Invoke-Fly ips allocate-v6 --app $App
}

# One machine: the world's volume can be attached to only one.
Push-Location $root
try {
	Invoke-Fly deploy . --app $App --primary-region $Region --ha=false `
		--config deploy/fly/fly.toml
}
finally {
	Pop-Location
}

Write-Host ''
$ips = @(Invoke-Fly ips list --app $App)
if (Test-DedicatedIPv4 $ips) {
	Write-Host "Telnet: $App.fly.dev port 4201, over IPv4 or IPv6"
}
else {
	Write-Host "Telnet: $App.fly.dev port 4201, over IPv6 only (run again with -DedicatedIPv4 for IPv4)"
}
$ips | Write-Host
Write-Host 'The setup link is in the log (look for setup_code):'
Write-Host "  fly logs --app $App --no-tail | Select-String setup_code"
