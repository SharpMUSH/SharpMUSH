# Launches SharpMUSH on Fly.io from a checkout of this repository, on Windows:
#
#   fly auth login
#   powershell -ExecutionPolicy Bypass -File deploy\fly\launch.ps1 my-game iad
#
# The same steps as launch.sh. Safe to run again: it creates only what is missing, then deploys. A
# dedicated IPv4 address is allocated for telnet (Fly charges $2/month for it); IPv6 is free.
param(
	[Parameter(Mandatory = $true)][string]$App,
	[string]$Region = 'iad'
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

$ips = @(Invoke-Fly ips list --app $App)
if (-not ($ips | Where-Object { $_ -match 'v4' -and $_ -notmatch 'shared' })) {
	Invoke-Fly ips allocate-v4 --app $App --yes
}
if (-not ($ips | Where-Object { $_ -match 'v6' })) {
	Invoke-Fly ips allocate-v6 --app $App
}

# One machine: the world's volume can be attached to only one.
Push-Location $root
try {
	Invoke-Fly deploy . --app $App --primary-region $Region --ha=false `
		--config deploy/fly/fly.toml --dockerfile deploy/fly/Dockerfile `
		--ignorefile deploy/fly/fly.dockerignore
}
finally {
	Pop-Location
}

Write-Host ''
Write-Host 'Telnet: port 4201 on the dedicated v4 address below'
Invoke-Fly ips list --app $App
Write-Host 'The setup link is in the log (look for setup_code):'
Write-Host "  fly logs --app $App --no-tail | Select-String setup_code"
