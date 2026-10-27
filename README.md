# <img src="Solution%20Files/Logo.svg" width="56" height="56" alt="" /> SharpMUSH

**Build and run your own multiplayer text world.**

SharpMUSH is an open-source MUSH server for collaborative stories, roleplay and world building. A game plays out in a MU\* client, a browser and a Discord server at once, and its systems are written in softcode that builders and players can read and change.

**[Try the live example](https://mush.sharpmush.com)** · **[Run a game](https://sharpmush.com/guides/docker-quickstart/)** · **[Read the docs](https://sharpmush.com)**

<p align="center">
  <img src="assets/branding/sharpie/marketing/sharpie-world-builder.png" width="320" alt="Sharpie, the SharpMUSH mascot, joins rooms and conversation into a connected world." />
</p>

## A world you can make your own

- **Build together.** Create rooms, characters and shared stories with familiar MUSH softcode.
- **Play from anywhere.** Connect with a MU\* client or the [web portal](https://sharpmush.com/guides/web-portal/), which has a Play terminal, mail, scenes and the wiki, and works on a phone and with [screen readers](https://sharpmush.com/guides/screen-readers/). MUD clients such as Mudlet get a map and gauges over [GMCP](https://sharpmush.com/guides/mud-clients/).
- **Bridge Discord.** [Link Discord channels](https://sharpmush.com/guides/discord/) to the game, so lines go both ways under each member's chosen character. Players can [sign in with Discord](https://sharpmush.com/guides/sign-in-services/), Google and others.
- **Run roleplay and staff work.** [Scenes](https://sharpmush.com/guides/scenes/) log roleplay as it happens, the wiki holds pages with [facts](https://sharpmush.com/guides/wiki-facts/) the portal and softcode can search, and [jobs](https://sharpmush.com/guides/jobs/) track what players ask staff for.
- **Softcode first.** Game systems ship as [softcode packages](https://sharpmush.com/guides/packages/) you install, upgrade and remove, and [portal pages](https://sharpmush.com/guides/applications/) can be softcode too. [C# plugins](https://sharpmush.com/guides/plugins/) add to the engine without a fork.
- **Let other tools in.** [Apps](https://sharpmush.com/guides/connected-apps/) act as a character with only the rights its player allowed, and editors check softcode through the [language server](https://sharpmush.com/guides/editor-support/).
- **Bring an existing world.** [Import](https://sharpmush.com/guides/pennmush-migration/) a PennMUSH, RhostMUSH or TinyMUX database with its mail. SharpMUSH runs PennMUSH softcode, with the [differences](https://sharpmush.com/reference/compatibility/) listed, and understands much of TinyMUX and RhostMUSH softcode too.

## Join in

[Ask questions on Discord](https://discord.gg/jYErRbqaC9) · [Explore the package library](https://sharpmush.com/guides/package-library/) · [Develop SharpMUSH](https://sharpmush.com/guides/local-install/)

[![Build status](https://github.com/SharpMUSH/SharpMUSH/actions/workflows/dotnet.yml/badge.svg)](https://github.com/SharpMUSH/SharpMUSH/actions/workflows/dotnet.yml)
[![Discord](https://img.shields.io/discord/1216626296642343044?label=Discord)](https://discord.gg/jYErRbqaC9)

## Release Status

The first release, 1.0.0, is being prepared. See [CHANGELOG.md](./CHANGELOG.md) for what each release contains.

## Docker Deployment

SharpMUSH is available as Docker images on DockerHub:
- `sharpmush/sharpmush-server` - Main server
- `sharpmush/sharpmush-socketserver` - Stable telnet/WebSocket socket owner
- `sharpmush/sharpmush-renderer` - Replaceable rendering worker
- `sharpmush/sharpmush-edge` - Caddy, NATS and the socket server in one image, run beside the server and renderer images on hosts that run one container per app (Fly.io, Railway)
- `sharpmush/sharpmush-allinone` - Everything in one image, for a one-app game on those hosts

For information on setting up automatic DockerHub publishing when new versions are released, see [DockerHub Setup Guide](./docs/DOCKERHUB_SETUP.md).

## Quick Contribution Guide

The full guide to running SharpMUSH from source and running its tests is [Develop SharpMUSH](https://sharpmush.com/guides/local-install/) on the documentation site.

### How to Build and Test

- Install [.NET 11](https://dotnet.microsoft.com/en-us/download/dotnet/11.0) — SDK 11.0.100-rc.2 or newer, as pinned in `global.json`
- Install [Docker Desktop](https://www.docker.com/products/docker-desktop/)

Build with:
```bash
dotnet build
```

Run the tests with:
```bash
dotnet test
```

> **Note:** Test telemetry output is disabled by default to reduce console noise. To enable telemetry metrics during test runs, set the environment variable:
> ```bash
> export SHARPMUSH_ENABLE_TEST_TELEMETRY=true
> dotnet test
> ```

The main entrypoint to set as a Startup Project is [`SharpMUSH.Server`](./SharpMUSH.Server).

For full integration, run [`SharpMUSH.SocketServer`](./SharpMUSH.SocketServer), which owns client sockets, and [`SharpMUSH.RenderingWorker`](./SharpMUSH.RenderingWorker), which renders output over a shared Unix socket. The local Compose stack wires all three processes and persistent NATS storage:

```bash
docker compose up --build
```

When launching projects separately on Linux, set `Rendering__SocketPath` to the same writable path (for example `/tmp/sharpmush-render.sock`) for both SocketServer and RenderingWorker, and provide `NATS_URL` to SocketServer and the main server. For quick images from Release binaries, use `Dockerfile.socketserver.local` for SocketServer and `Dockerfile.renderingworker.local` for the renderer; mount their shared socket directory.

See [connection updates and recovery](./deploy/connection-updates.md) for migration and restart behavior. The [development Kubernetes manifest](./kubernetes/dev-k8s.yaml) keeps SocketServer and renderer in one Pod: updating either image replaces that Pod and drops sockets. Use the single-host Compose deployment for independent renderer updates that retain client sockets.

Additionally, a Blazor web client is available at [`SharpMUSH.Client`](./SharpMUSH.Client).
