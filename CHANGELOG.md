# Changelog

SharpMUSH's releases, newest first. Each GitHub Release tagged `vX.Y.Z` publishes the Docker images and must have a section here.

Two version numbers matter:

- **The engine version** is the release: what `version()`, `numversion()`, `@version`, MSSP's `CODEBASE` and a package's `requires_server` see. The `vX.Y.Z` tag sets it.
- **The plugin contract version** belongs to the `SharpMUSH.Library` and `SharpMUSH.Implementation.Generated` NuGet packages, which C# plugins build against, and is checked against a plugin package's `binaries.min_server_version`. It changes only when that contract does, and is released with a `plugin-nuget/vX.Y.Z` tag.

## [Unreleased]

### Added

- `/metrics` reports `sharpmush.messaging.subscription.dropped`, by subscribed subject: portal output (`game.output.*`, `game.room.*`, `game.scene.*`) the server's bridge dropped because it fell behind. Those drops were silent.
- `@function/restrict` and `@command/restrict` take `role^<role>`, `power^<power>`, `flag^<flag>` and `perm^<permission>`, any one of which lets its holder in, `!` in front of one to turn its holders away, and `everyone`. On a built-in function they change its own restriction instead of adding to it, so `@function/restrict cmdlink=everyone` lets anyone use `cmdlink()`, and `!wizard` opens a wizard-only function as in PennMUSH. `cmdlink()`, `sound()`, `music()`, `stopsound()`, `image()`, `pane()`, `clearscreen()`, `prefetch()` and `expirelinks()` declare their Send_OOB requirement this way. See `help restrict`.
- A scene's members, wherever they are, and the others in its room are told when it starts, pauses, resumes or ends: who did it, the scene's number and title, and for a public scene the command that joins it (`[SCENE] Mira started scene 12: The Docks · +scene/join 12 to join`). While a scene runs, they are also told when a pose in it is edited, or an edit is undone or redone (`[SCENE] Mira undid an edit to their pose 34 in scene 12.`). A private scene is announced only to its members. `+config scene/announce=no` turns it off (scene package 1.20.0).
- A scene's page in the portal has Finish and Cancel buttons, each asking once before it runs. Whoever holds `scene.manage` gets every control on any scene (edit, pause, start, finish, cancel), and whoever holds `scene.close` gets Finish.
- The play terminal's settings menu has an "Echo my commands" checkbox. Off hides the commands you send from the output; on (the default) shows them. The choice is kept per browser.
- The play terminal's settings menu has "Hide pages" and "Hide channel messages" checkboxes. On, pages stay in the sidebar's Pages list and channel messages (connect and disconnect lines included) in their channel's view, out of the main output, the ones already there included. Both are off by default and kept per browser, and are offered only while the game sends channels and pages to the sidebar. A portal connection's output now marks each channel line and page as one.
- A visitor playing as a guest on the portal's Play page sees a line above the terminal saying so, with a link to create an account.
- The play terminal's settings menu lists each option by name only; an (i) beside it shows the details on hover, focus or a tap.
- The portal's Mail page has a Sent list: each message the character sent, newest first, with its recipients and how many of them have read it. A message sent to several people is one row.
- The Mail link in the portal's navigation shows how many inbox messages are unread, and a notice appears when new mail arrives, with a button to read it. comm-feed 1.5.0 pushes each new message to the recipient as the `mail.new` OOB package.
- Boards (bboards 1.8.0): a new comment or reply is announced to the board's readers as a new post is, as `(New BB comment (5/1/1/3) posted to 'General' by Seliana: Test Post)`.
- A world comes with the official package library, SharpMUSH Official (`https://github.com/SharpMUSH/SharpMUSH.Packages`), as a remote, and Admin → Packages → Browse starts with it selected. A world that already has the library, or a remote under that name, keeps what it has. It is added once: a library the administrator removes stays removed, unless a later release moves a package the game has installed out of the server and into the library.

### Changed

- NATS.Net 3.3.2, from 2.7.3. A core subscription now buffers 16,384 messages before it drops one, where it buffered 1,024, so the portal loses less output in a burst, and a ping that cannot be written counts as a missed pong, so a dead broker connection is noticed. A C# plugin that uses the `NatsConnection` its bridge source is handed builds against NATS.Net 3.
- In `@function/restrict`, a `!` in front of `wizard`, `admin`, `god`, `noguest`, `nogagged`, `nofixed` or `nobody` takes that restriction off, as PennMUSH's does, where it turned away whoever met it. A permission or `royalty` in a built-in's restriction replaces what the built-in asks for instead of adding to it, so a restriction already set may now let more in; check `@function <name>`. Restricting a built-in function needs `config.admin` (the Functions power is still enough for @functions). A function refused for lacking a permission names it: `#-1 PERMISSION DENIED: NEEDS CHAT.ADMIN`.

### Fixed

- `connect <character> <password>` takes the password of the account the character is linked to as well as the character's own, so a character made on the web portal, which gets no password of its own, can connect from a MU* client.
- The portal's Mail page shows times in the character's `TZ` zone when it names a known one, where it showed the browser's.
- Boards and Jobs say why they refuse a post, comment or request, such as "Permission denied. Posting on 5 Public needs the approved role." The portal shows it on the board's page and in place of the new-post or new-request form, and a draft (`+bbpost <board>/<title>`, `+request/draft new`) is refused before it starts.
- Boards: reading the conversation that holds a post's new comments (`+bbread 5/1/1/3`, or opening it in the portal) marks the post read, so it no longer shows as new.
- Boards and Jobs show times in the reader's time zone (their `TZ` attribute, set with `+timezone`), in the terminal and the portal, where they showed the game's.
- A reply in a board's conversation view sits just right of the comment it answers in the portal terminal, where it was pushed half the row across (MarkupString 2.31.1).
- An empty list's button (Who is online and Go home on Play's Here and Exits cards, Add a passkey) sits on its own line under the sentence instead of squeezing it into a narrow column.
- Play's Here card shows each character's full name, wrapping to a second line, where it showed only the first word ("The" for "The Foo").
- Coming back to the play output from a channel or a conversation keeps it where it was: on the newest line if you were following it, or on the line you were reading. It landed a screen or more higher, further the longer the scrollback.
- An open portal tab follows a change to its account's permissions without a page reload: a character approved, set WIZARD or given a power sees the new pages and controls at once, and one that loses them stops seeing them. The same holds when a role's permissions are edited.
- `Metrics:Port=0` (game server) and `ConnectionServer:MetricsPort=0` (connection server) turn metrics off: no port answers `/metrics`, where they moved the scrape onto the main web port, which a reverse proxy publishes.

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
