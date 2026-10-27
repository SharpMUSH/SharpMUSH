# Launches SharpMUSH on Fly.io from the published image, on Windows, with no checkout needed:
#
#   fly auth login
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/main/deploy/fly/launch.ps1))) my-game iad [-DedicatedIPv4] [-Single] [-UpdateEdge]
#
# The same steps as launch.sh. The game runs as three apps on a private network of their own:
# <game>, the edge that holds every player's socket and the only one with a public address;
# <game>-server, with the world on its volume; and <game>-renderer. Their settings live in
# <game>.edge.fly.toml, <game>.server.fly.toml and <game>.renderer.fly.toml in the current
# directory, downloaded the first time.
#
# Safe to run again: it creates only what is missing, then deploys the newest image to the server
# and the renderer, which drops nobody. The edge is deployed the first time, and again only with
# -UpdateEdge, which drops everyone connected.
#
# -Single runs the game as one app, <game>, with its settings in <game>.fly.toml: cheaper, but every
# update drops everyone connected. A game launched that way stays that way when run again.
#
# The edge gets IPv6 and a shared IPv4 address, both free. The shared IPv4 carries only the web
# portal, so telnet is reachable over IPv6 alone; -DedicatedIPv4 adds an IPv4 address of the app's
# own ($2/month at Fly) for telnet.
#
# It runs in the caller's session, so it stops with throw, never exit, which would close the window.
param(
	[Parameter(Mandatory = $true)][string]$App,
	[string]$Region = 'iad',
	[switch]$DedicatedIPv4,
	[switch]$Single,
	[switch]$UpdateEdge
)

if ($App -cnotmatch '^[a-z0-9][a-z0-9-]*$') {
	throw "The game's name may hold only a-z, 0-9 and -."
}
$ref = if ($env:SHARPMUSH_SOURCE_REF) { $env:SHARPMUSH_SOURCE_REF } else { 'main' }
$found = Get-Command fly, flyctl -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $found) {
	throw 'Install flyctl first: https://fly.io/docs/flyctl/install/'
}
$fly = $found.Source

function Invoke-Fly {
	& $fly @args
	if ($LASTEXITCODE -ne 0) { throw "fly $args failed (exit code $LASTEXITCODE)" }
}

# Once, with {game} replaced by the game's name.
function Get-Config([string]$Name, [string]$Path) {
	if (Test-Path $Path) { return }
	$text = (Invoke-WebRequest -UseBasicParsing "https://raw.githubusercontent.com/SharpMUSH/SharpMUSH/$ref/deploy/fly/$Name").Content
	[IO.File]::WriteAllText((Join-Path $PWD.Path "$Path.tmp"), $text.Replace('{game}', $App))
	Move-Item "$Path.tmp" $Path
	Write-Host "Wrote $Path; edit it and run this again to change that part's settings."
}

function New-App([string]$Name, [string]$Network) {
	& $fly status --app $Name *> $null
	if ($LASTEXITCODE -ne 0) {
		if ($Network) { Invoke-Fly apps create $Name --network $Network } else { Invoke-Fly apps create $Name }
	}
}

function New-Volume([string]$Name, [string]$Volume, [int]$Size) {
	$volumes = Invoke-Fly volumes list --app $Name | Out-String
	if ($volumes -notmatch $Volume) {
		Invoke-Fly volumes create $Volume --app $Name --region $Region --size $Size --yes
	}
}

function Test-DedicatedIPv4([string[]]$Lines) {
	[bool]($Lines | Where-Object { $_ -match 'v4' -and $_ -notmatch 'shared' })
}

function New-PublicAddresses {
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
}

# One machine, since a volume can be attached to only one.
function Invoke-Deploy([string]$Name, [string]$Config, [string[]]$Flags = @()) {
	Invoke-Fly deploy --app $Name --config $Config --primary-region $Region --ha=false @Flags
}

# A game already launched keeps its layout.
$singleConfig = "$App.fly.toml"
$edgeConfig = "$App.edge.fly.toml"
if ((Test-Path $singleConfig) -and (Test-Path $edgeConfig)) {
	throw "Both $singleConfig and $edgeConfig are here; keep the one for the game's layout."
}
if ((Test-Path $edgeConfig) -and $Single) {
	throw "$App runs split ($edgeConfig); moving it to one app means restoring an @backup there."
}
if (Test-Path $singleConfig) { $Single = $true }

if ($Single) {
	Get-Config fly.toml $singleConfig
	New-App $App
	New-Volume $App sharpmush_data 10
	New-PublicAddresses
	Invoke-Deploy $App $singleConfig
}
else {
	foreach ($part in 'edge', 'server', 'renderer') { Get-Config "$part.fly.toml" "$App.$part.fly.toml" }
	# Their own network: NATS, the server and the renderer ask for no credentials, so no other app in
	# the organization may reach them.
	New-App $App $App
	New-App "$App-server" $App
	New-App "$App-renderer" $App
	New-Volume $App sharpmush_edge 4
	New-Volume "$App-server" sharpmush_data 10
	New-PublicAddresses

	# A new game's edge comes first: the server waits for its NATS before it answers. The edge's own
	# health check asks the server, so it is not waited for.
	$fresh = -not (Invoke-Fly machines list --app $App --quiet | Out-String).Trim()
	if ($fresh) { Invoke-Deploy $App $edgeConfig @('--detach') }
	Invoke-Deploy "$App-server" "$App.server.fly.toml"
	Invoke-Deploy "$App-renderer" "$App.renderer.fly.toml"
	if (-not $fresh -and $UpdateEdge) { Invoke-Deploy $App $edgeConfig }
	elseif (-not $fresh) {
		Write-Host 'The edge was left running, so nobody was dropped. -UpdateEdge updates it too, dropping everyone.'
	}
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
