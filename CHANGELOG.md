# Changelog

SharpMUSH's releases, newest first. Each tag `vX.Y.Z` publishes the Docker images and must have a section here.

Two version numbers matter:

- **The engine version** is the release: what `version()`, `numversion()`, `@version`, MSSP's `CODEBASE` and a package's `requires_server` see. The `vX.Y.Z` tag sets it.
- **The plugin contract version** belongs to the `SharpMUSH.Library` and `SharpMUSH.Implementation.Generated` NuGet packages, which C# plugins build against, and is checked against a plugin package's `binaries.min_server_version`. It changes only when that contract does, and is released with a `plugin-nuget/vX.Y.Z` tag.

## [1.0.0] - Unreleased

The first release. Plugin contract 3.0.0. World format 1.

### The server

- A MUSH server on .NET 11 that aims at PennMUSH compatibility: the softcode parser, commands, functions, flags, locks, attributes, zones, parents, the queue, `@mail` and channels.
- Imports a PennMUSH database and its `mush.cnf` from the setup wizard. Colour codes that cannot be converted safely are removed, with a warning. Differences from PennMUSH are listed in `help pennmush compatibility`.
- Stores the world in an embedded LMDB database (the `lightning` provider): one directory per world, no database server to run. `@backup` copies a live world without stopping it; `@storage` reports space and history retention.
- Three processes: the game server, a socket server that owns client connections and keeps them across game server restarts, and a rendering worker. NATS connects them. `docker compose up -d` runs the set.
- Telnet with TTYPE, CHARSET, MSSP, GMCP, MXP and Pueblo; WebSocket clients; ASCII output for clients without UTF-8, and a screen-reader mode.
- Styled text is MarkupString, rendered for each client's protocol and width. Listings and panels follow the game's `layout_border` and `layout_theme`.
- Roles and permissions in place of fixed wizard and royalty bits, managed with `@role`. Secrets are stored encrypted and never readable from softcode.
- Softcode packages (`package.yaml`) installed, upgraded and rolled back from the portal. 59 packages are bundled with the server, among them jobs, bulletin boards, scenes, chargen, a wiki reader, `+who`/`+where` and the onboarding tutorial.
- C# plugins built against the plugin contract: Discord, Scene, a language server for softcode and an MCP server are bundled.

### The portal

- A web portal served by the game server: account sign-in, characters, a terminal, scenes, mail, a wiki with semantic facts, a softcode editor, and administration for configuration, roles, packages, backups and moderation.
- A setup wizard on first boot: claim the admin account, import a PennMUSH world, choose handlers and the bundled packages.
- Themes, including built-in genre themes and staff stylesheets.

### Upgrading

- There is nothing to upgrade from. Worlds written by development builds from before 6 October 2026 cannot be opened.
- A world is stamped with its format. A server refuses a world stamped newer than its own.
- Requires the .NET 11 runtime. The Docker images carry it.

[1.0.0]: https://github.com/SharpMUSH/SharpMUSH/releases/tag/v1.0.0
