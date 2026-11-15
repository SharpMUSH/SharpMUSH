# Changelog

SharpMUSH's releases, newest first. Each GitHub Release tagged `vX.Y.Z` publishes the Docker images and must have a section here.

Two version numbers matter:

- **The engine version** is the release: what `version()`, `numversion()`, `@version`, MSSP's `CODEBASE` and a package's `requires_server` see. The `vX.Y.Z` tag sets it.
- **The plugin contract version** belongs to the `SharpMUSH.Library` and `SharpMUSH.Implementation.Generated` NuGet packages, which C# plugins build against, and is checked against a plugin package's `binaries.min_server_version`. It changes only when that contract does, and is released with a `plugin-nuget/vX.Y.Z` tag.

## [Unreleased]

### Added

- The play terminal's settings menu has an "Echo my commands" checkbox. Off hides the commands you send from the output; on (the default) shows them. The choice is kept per browser.

### Fixed

- Play's Here card shows each character's full name, wrapping to a second line, where it showed only the first word ("The" for "The Foo").
- Coming back to the play output from a channel or a conversation keeps it where it was: on the newest line if you were following it, or on the line you were reading. It landed a screen or more higher, further the longer the scrollback.
- An open portal tab follows a change to its account's permissions without a page reload: a character approved, set WIZARD or given a power sees the new pages and controls at once, and one that loses them stops seeing them. The same holds when a role's permissions are edited.

[Unreleased]: https://github.com/SharpMUSH/SharpMUSH/compare/v1.0.0...HEAD

## [1.0.0] - 2026-11-14

The first release. Plugin contract 4.0.0. World format 1.

### The server

- A MUSH server on .NET 11 that aims at PennMUSH compatibility: the softcode parser, commands, functions, flags, locks, attributes, zones, parents, the queue, `@mail` and channels.
- Imports a PennMUSH database and its `mush.cnf` from the setup wizard. Colour codes that cannot be converted safely are removed, with a warning. Differences from PennMUSH are listed in `help pennmush compatibility`.
- Stores the world in an embedded LMDB database (the `lightning` provider): one directory per world, no database server to run. `@backup` copies a live world without stopping it; `@storage` reports space and history retention.
- Three processes: the game server, a socket server that owns client connections and keeps them across game server restarts, and a rendering worker. NATS connects them. The socket server and the rendering worker are native binaries, compiled ahead of time. The Compose files in `deploy/` run the set.
- Telnet with TTYPE, CHARSET, MSSP, GMCP, MXP and Pueblo; WebSocket clients; ASCII output for clients without UTF-8, and a screen-reader mode.
- Styled text is MarkupString, rendered for each client's protocol and width. Listings and panels follow the game's `layout_border` and `layout_theme`.
- Roles and permissions in place of fixed wizard and royalty bits, managed with `@role`. Secrets are stored encrypted and never readable from softcode.
- Softcode packages (`package.yaml`) installed, upgraded and rolled back from the portal. 110 packages are bundled with the server, counting each system's help, configuration and portal packages. Among them are jobs, bulletin boards, scenes, chargen, a wiki reader, `+who`/`+where` and the onboarding tutorial.
- C# plugins built against the plugin contract: Discord, Scene, World (grid maps), a language server for softcode and an MCP server are bundled.

### The portal

- A web portal served by the game server: account sign-in, characters, a terminal, scenes, mail, a wiki with semantic facts, a softcode editor, and administration for configuration, roles, packages, backups and moderation.
- A setup wizard on first boot: claim the admin account, import a PennMUSH world, choose handlers and the bundled packages.
- Themes, including built-in genre, anime, console and tabletop role-playing game, fan-game setting, game-menu, creature-game and Chinese history themes, and staff stylesheets. Any theme can mix how menus mark the current line, button shapes, card materials, meters, motion and atmosphere.

### Upgrading

- There is nothing to upgrade from. Worlds written by development builds from before 6 October 2026 cannot be opened.
- A world is stamped with its format. A server refuses a world stamped newer than its own.
- Requires the .NET 11 runtime. The Docker images carry it.

[1.0.0]: https://github.com/SharpMUSH/SharpMUSH/releases/tag/v1.0.0

## [0.9.0] - Intial Commit
A semi-pre-release before 1.0.0 to help shift Latest.
