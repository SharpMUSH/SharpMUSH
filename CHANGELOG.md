# Changelog

SharpMUSH's releases, newest first. Each GitHub Release tagged `vX.Y.Z` publishes the Docker images and must have a section here.

Two version numbers matter:

- **The engine version** is the release: what `version()`, `numversion()`, `@version`, MSSP's `CODEBASE` and a package's `requires_server` see. The `vX.Y.Z` tag sets it.
- **The plugin contract version** belongs to the `SharpMUSH.Library` and `SharpMUSH.Implementation.Generated` NuGet packages, which C# plugins build against, and is checked against a plugin package's `binaries.min_server_version`. It changes only when that contract does, and is released with a `plugin-nuget/vX.Y.Z` tag.

## [Unreleased]

### Changed

- On the Play page, the chosen view in the Story | Terminal switch shows its icon in the theme's accent colour on a tinted pill, so it is plain which one is on.
- The Play page's composer, a scene's live compose box and the pose editor have a text/softcode icon switch beside the format menu, in the look of the Story | Terminal switch. Both icons are always shown, the chosen one in the accent, and either can be picked at any time. When text you are writing looks like softcode, a dot appears on the softcode icon and its tooltip names what it found. It replaces the "looks like softcode" notice, which went away once dismissed, and the switch inside the format menu. On Command, softcode is chosen and the text icon is unavailable. All three keep the format menu and the switch above the field on the right and Send (or Cancel and Save) below it on the right, drawn as the portal's capsule buttons with an icon; on a phone, Send in the Play composer is a round icon as in the live compose box.

[Unreleased]: https://github.com/SharpMUSH/SharpMUSH/compare/v1.0.3...HEAD

## [1.0.3] - 2026-11-18

The 1.0.2 release published no images, because its tagged source had no 1.0.2 section in this changelog. 1.0.3 is the first release whose images carry 1.0.2's changes as well as these. Plugin contract 4.0.0. World format 1: a 1.0.0, 1.0.1 or 1.0.2 world opens as it is.

### Added

- The wiki editor says under the source box, and `help wiki line breaks` explains, that lines next to each other join into one paragraph and how to keep a line on its own (end it with `\` or two spaces).
- `+scene/go <id>` (scene 1.23.0) takes you to the room a running scene is in. A scheduled, paused, finished or cancelled scene is refused, and so is a FIXED character; it needs `scene.join`. In `+scene` a running scene's room is a link to it, and in the portal the room name on a running scene's card, page and live view is a button that goes there and opens Play.
- `wsjson(<json>[, <default>])`, PennMUSH's: a WebSocket connection such as the web portal is sent the JSON as `{"type":"json","data":…}` after the line, on the path `oob()` uses, and every other reader sees `<default>` in its place. Only the server changes.
- The play terminal's input box takes several lines: it grows with what you type, for code and long poses, up to the **Input lines** setting in its settings menu (5 by default), then scrolls. Enter still sends and Shift+Enter starts a new line; each line is sent as its own command, as a MUD client sends a paste. Up and Down move between the box's lines and walk the command history once the caret is at the start or the end. Kept per browser.
- The World map's builder mode asks before throwing away changes not yet applied: leaving the page, opening another map, Back, **Discard changes**, and closing or reloading the tab (the browser's own prompt). While changes wait, the page header shows **Review and apply** beside **Discard changes**, so the review is no longer only at the foot of the builder panel.
- The portal's **Your scenes** list has **Start now** on a scheduled scene (and **Resume scene** on a paused one) for its owner or a holder of `scene.manage`, so a scene starts without opening it first. It runs `+scene/start` as the acting character.

### Changed

- `scene` 1.21.0: `+scene/leave` makes a player inactive. They stay on the scene's cast while it has a pose of theirs, and come off it only with none, or only deleted ones; before, a deleted pose still kept them on. The owner can't leave their own scene: `+scene/leave` only makes them inactive, as `+scene/deactivate` does, and the Play page shows them **Go inactive** in place of **Leave scene**.
- A private scene runs like any other: it shows in its room, anyone there may join it, and its owner and cast pose in it. Once it is finished or cancelled, only those who took part (and wizards) see it, in `+scene/mine` and their own lists. `scene(<id>,hidden)` is 1 for such a scene. Scene package 1.22.0, room-contents 1.3.3, highlights 1.4.2.
- A room running several scenes shows each on its own row under **In scene** on the Play page, and each row opens that scene's Story; before, the page showed only the most recent. A pose made in the room goes to the scene there you are focused on, whichever is most recent. `scenelist(room[, <room>])` lists every active scene in a room, and `scenewhere(<room>, <player>)` answers the one there that player is focused on first. Each row marks whether you are active in that scene, with a green check and **Active**, or a grey circle and **Inactive** or **Not joined**. room-contents 1.4.0 sends them all as `scenes` in `room.info`.
- An ordinary `pose`, `say` or `@emit` is refused while the scene you are active in runs in another room, so it is neither said here nor recorded there; `+scene/pose <id>=<text>` still poses into it from anywhere. A pose into your scene while another active scene in the room had a pose more recently still goes to yours, and warns you which one, in case you are in the wrong scene. Scene package 1.24.0.
- `cmdlink()` needs no power: any room, object or player can make a command link. `tagwrap()`'s SEND and XCH_CMD parameters, `sound()`, `image()` and the other client-control functions still need a Wizard or Send_OOB. `@function/restrict cmdlink=game.wizard game.send_oob` puts the old gate back.
- The Play page's Story composer offers Emit, OOC, Pose and Command, with Emit first and chosen by default. Say is gone; a draft kept as Say opens on Emit with its text.
- `messages` 1.4.2: the connect screen's text logo is drawn with `tone(primary,...)` instead of a fixed `ansi(#00f5b7,...)`, so it takes the reader's `@theme` or the game's `layout_theme` colour. Under the default `sharpmush` theme that is its primary teal, `#00bf8f`. A blank line now separates the welcome from the commands under it, in the object and in the stored connect text.

### Fixed

- Wiki headings keep their levels on a saved page: `#`, `##` and `###` were all drawn at about the same size, because the page turned an `h1` into an `h2` beside the real `h2`s and gave `h3` the browser's default. When a page uses `#` below its title every heading now moves down one level, each level has its own size in the editor's preview and on the page, and the preview hides a leading `#` that repeats the title as the saved page does.
- A portal application whose schema fails to load shows the address it actually asked (`http/chargen/schema?at=`, not the `{path}` template) and the server's answer, such as `Not found.`, so the cause is visible without the browser console.
- The scene functions hid a private scene from its owner when they were not in its cast, so the portal showed them the scene but its compose box and Finish button answered "No such scene". An owner now always reads their own scene, as the portal already did.
- A character created in the web portal raises `` PLAYER`CREATE ``, with `portal` as its `how`, as one made at the connect screen or with `@pcreate` does. It raised only `` PLAYER`LINK ``, so `autoset` rules that run on `create` (giving every new character `approved`, say) never ran for it. `autoset` 1.1.1's help says so.
- **Page**, on a character's profile and on their sheet in Play, opens your conversation with them in Play's pages area, ready to type, whether or not you have paged them before. It went to Play with nothing open from the profile, and from the sheet put `page Name=` in the input line. A game without the `comm-feed` package still gets `page Name=` in the input line from the sheet.

[1.0.3]: https://github.com/SharpMUSH/SharpMUSH/compare/v1.0.2...v1.0.3

## [1.0.2] - 2026-11-16

GitHub release source: `481f0e57bdd6e3d1aa2d90aee9d3a1d491bf2a6e`. Docker publication was blocked by the missing version section in that tagged source; this history entry does not repair the published tag (see [recovery guidance](docs/DOCKERHUB_SETUP.md#recovering-a-release-with-invalid-source-metadata)).

### Added

- MSSP reports `WEBSOCKET` (the mudstandards.org variable), the address MUD clients reach telnet over WebSocket at: `/telnet` at the website's address, so `wss://mush.example.com/telnet` for a `mud_url` of `https://mush.example.com`. It is left out while `mud_url` is unset.
- The play terminal's settings menu has **Wrap at this width**: lines wrap at the Width (cols) setting however wide the window is, and the game is told that width, so its layouts stop there too. Off (the default), a window wider than the largest font fills shows more columns, as before. Kept per browser.

### Changed

- Bundled packages add their commands without `/noparse/rsnoparse` (`alts`, `autoset`, `availability`, `bboards`, `chargen`, `finger`, `health`, `info`, `jobs`, `onboarding`, `plus-config`, `relationships`, `roster`, `scene-safety`, `softcode-editor`, `status`, `summary`, `time`, `watch`), so what a player types after `+autoset`, `+bb` and the rest is evaluated as that player, as a built-in command's argument is: `+autoset/apply %#=approved` names the player who typed it, and a bracket to keep is escaped. Their hooks run with `/localize/clearregs`, so a register set while the argument is evaluated never reaches the package's code. Typed /mush board text, `+bbwrite` lines included, is not evaluated a second time: the board keeps `decompose()` of it as the post's source, and a partial `+bbedit` changes the text as shown and evaluates nothing. `+info/get`, `+pinfo/get` and `+journal/get` print the text decomposed, so the printed command pasted back stores the same text.
- TelnetNegotiationCore 4.13.6. A telnet connection waits for its input to be processed on a signal instead of checking every timer tick, so a line reaches the game in microseconds rather than up to 15 ms. Input is queued in chunks rather than a byte at a time, and MCCP compression costs less on both sides.

### Fixed

- `deploy/docker-compose.prod.yml` and the Fly Dockerfile run the `latest` images, as the one-click files do, unless `SHARPMUSH_IMAGE_TAG` names another. They pinned an exact release, which the 1.0.1 release left on 1.0.0, so a game that pulled its images without setting the tag stayed on 1.0.0.
- The connect screen's logo, and any picture a terminal client draws as half blocks, is solid where it should be in clients that draw `█` from their font, BeipMU among them. A solid cell was sent as `█`, which those clients draw shorter than the line, so the picture came out striped; it is now a space on that colour, which fills the cell (MarkupString 2.31.4).
- A Pueblo client that answers the hello after its connection is registered (SharpMUTerm, or MUSHclient on a slow link) is shown the connect screen once. It was shown it again after the handshake, for a client that clears its screen at `<xch_page clear=text>`, which MUSHclient, BeipMU and SharpMUTerm do not.

[1.0.2]: https://github.com/SharpMUSH/SharpMUSH/compare/v1.0.1...v1.0.2

## [1.0.1] - 2026-11-15

Plugin contract 4.0.0. World format 1: a 1.0.0 world opens as it is.

### Added

- The portal's page is sent with a `Link` header naming the stylesheets, fonts and Blazor boot script it loads first, read from the published `index.html`. Behind Cloudflare with Early Hints on, a browser gets them as a `103 Early Hints` response and starts fetching them while the page itself is still on its way.
- Staff with `config.admin` change character applications in the portal: `/apps/chargen/settings` holds the settings `+config chargen` changes, checked the same way (with `chargen-config` installed), and `/apps/chargen/form` the form's sections and fields as rows to edit, in order. Both appear in the app's sidebar under Setup, and on the portal's configuration page as the Character applications tile, whose options site search finds (chargen 2.8.0, chargen-app 2.1.0, chargen-config 1.1.0).
- `chargen-jobs` follows each character application with a job in the Applications bucket: the first `+app/submit` files it in the applicant's name, submitting again writes on it (opening it again if it was closed), withdrawing and returning write on it, and approving closes it. Applications are still decided in `+app` or the portal. Packages that add to chargen's hooks set `` ON`<hook>`<NAME> ``, which runs after the game's own hook, and `` ON`WITHDRAW `` is a new hook (chargen 2.9.0, chargen-jobs 1.0.0, chargen-jobs-help 1.0.0).
- The jobs API's `` INC`API`REPLY `` takes `quiet,1`, which tells only staff, and `keep,1`, which leaves whose turn it is; `` INC`API`CLOSE `` takes `quiet,1`; and `` INC`API`STAFF `` hands a job back to staff, opening it again when it is closed (jobs 1.18.0).
- A portal form field may name a `placeholder`: an example value shown in grey while a text, textarea or number input is empty.
- `help`, `news`, `ahelp` and the portal's help find a command's topic without its `@`: `help feed examples` shows `@feed examples`. The @-name is tried after the topic's own exact and prefix matches, so `help if` still shows `if()`, and before its fuzzy match; the @-name's prefix and fuzzy matches come last.
- `/metrics` reports `sharpmush.messaging.subscription.dropped`, by subscribed subject: portal output (`game.output.*`, `game.room.*`, `game.scene.*`) the server's bridge dropped because it fell behind. Those drops were silent.
- `@function/restrict` and `@command/restrict` take `role^<role>`, `power^<power>`, `flag^<flag>` and `perm^<permission>`, any one of which lets its holder in, `!` in front of one to turn its holders away, and `everyone`. On a built-in function they change its own restriction instead of adding to it, so `@function/restrict cmdlink=everyone` lets anyone use `cmdlink()`, and `!wizard` opens a wizard-only function as in PennMUSH. `cmdlink()`, `sound()`, `music()`, `stopsound()`, `image()`, `pane()`, `clearscreen()`, `prefetch()` and `expirelinks()` declare their Send_OOB requirement this way. See `help restrict`.
- A scene's members, wherever they are, and the others in its room are told when it starts, pauses, resumes or ends: who did it, the scene's number and title, and for a public scene the command that joins it (`[SCENE] Mira started scene 12: The Docks · +scene/join 12 to join`). While a scene runs, they are also told when a pose in it is edited, or an edit is undone or redone (`[SCENE] Mira undid an edit to their pose 34 in scene 12.`). A private scene is announced only to its members. `+config scene/announce=no` turns it off (scene package 1.20.0).
- A scene's page in the portal has Finish and Cancel buttons, each asking once before it runs. Whoever holds `scene.manage` gets every control on any scene (edit, pause, start, finish, cancel), and whoever holds `scene.close` gets Finish.
- The play terminal's settings menu has an "Echo my commands" checkbox. Off hides the commands you send from the output; on (the default) shows them. The choice is kept per browser.
- The play terminal's settings menu has "Hide pages" and "Hide channel messages" checkboxes. On, pages stay in the sidebar's Pages list and channel messages (connect and disconnect lines included) in their channel's view, out of the main output, the ones already there included. Both are off by default and kept per browser, and are offered only while the game sends channels and pages to the sidebar. A portal connection's output now marks each channel line and page as one.
- A visitor playing as a guest on the portal's Play page sees a line above the terminal saying so, with a link to create an account.
- The play terminal's settings menu lists each option by name only; an (i) beside it shows the details on hover, focus or a tap.
- `help markdown` explains the Markdown that help, +help, the wiki and `rendermarkdown()` read: text and code spans, escaping backticks and bars, help and wiki links, blocks, and Markdown inside softcode.
- The portal's Mail page has a Sent list: each message the character sent, newest first, with its recipients and how many of them have read it. A message sent to several people is one row.
- The Mail link in the portal's navigation shows how many inbox messages are unread, and a notice appears when new mail arrives, with a button to read it. comm-feed 1.5.0 pushes each new message to the recipient as the `mail.new` OOB package.
- Boards (bboards 1.8.0): a new comment or reply is announced to the board's readers as a new post is, as `(New BB comment (5/1/1/3) posted to 'General' by Seliana: Test Post)`.
- A world comes with the official package library, SharpMUSH Official (`https://github.com/SharpMUSH/SharpMUSH.Packages`), as a remote, and Admin → Packages → Browse starts with it selected. A world that already has the library, or a remote under that name, keeps what it has. It is added once: a library the administrator removes stays removed, unless a later release moves a package the game has installed out of the server and into the library.
- The `autoset` package (available, not installed, with the `autoset-help` and `autoset-config` bridges): rules kept as a `+config autoset/rules` table give a character roles, and take roles away, when it is created, logs in (every time or the first time), is linked to an account, or gains or loses a role, so `gain` of `approved` runs when a character is approved. A rule can also `@trigger` an attribute of the game's choosing after its roles. `+autoset` lists the rules and `+autoset/apply <player>=<rule>` runs one now. Changing or running the rules needs `game.wizard`, and a rule gives only roles below wizard and below its setter's highest role.
- The ``role`assign`` and ``role`unassign`` events, raised when an object or an account is given a role it did not hold or loses one it held, by `@role/assign`, `@set`, `@power` or the portal's Roles page. Arguments: *objid*, *role*, *account*.

### Changed

- NATS.Net 3.3.2, from 2.7.3. A core subscription now buffers 16,384 messages before it drops one, where it buffered 1,024, so the portal loses less output in a burst, and a ping that cannot be written counts as a missed pong, so a dead broker connection is noticed. A C# plugin that uses the `NatsConnection` its bridge source is handed builds against NATS.Net 3.
- `help sharpmush features` lists what SharpMUSH has that PennMUSH does not, by area: the portal and accounts, roles, the wiki, feeds and dice, plugins (Discord, scenes, world maps, MCP and LSP), packages, softcode, functions, new commands and switches, clients, TinyMUX and RhostMUSH support, and running the game. Each line points to its own help topic.

### Fixed

- In `@function/restrict`, a `!` in front of `wizard`, `admin`, `god`, `noguest`, `nogagged`, `nofixed` or `nobody` takes that restriction off, as in PennMUSH; it turned away whoever met it. A permission or `royalty` in a built-in's restriction replaces what the built-in asks for instead of adding to it, so a restriction already set may now let more in; check `@function <name>`. Restricting a built-in function needs `config.admin` (the Functions power is still enough for `@function`s). A function refused for lacking a permission names it: `#-1 PERMISSION DENIED: NEEDS CHAT.ADMIN`.
- The package library's behaviour tests wait for a step's `@wait 0` follow-up even when its timer has already fired and it is not on the queue yet, so the next step no longer runs ahead of it. The integration test step in CI may take 15 minutes instead of 10; the suite takes 8 to 10 on a hosted runner.
- In MUSHclient and other Pueblo clients, tables, headers, the room description and the connect screen keep their line breaks. Preformatted text went to Pueblo as `<xch_mudtext>` with bare newlines, which MUSHclient ignores; each line now ends in `<BR>` (MarkupString 2.31.2).
- A Pueblo client that answers the hello before its connection is registered (MUSHclient) is shown the connect screen once. It was shown it again after the handshake, as for a client that answered later.
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
- Help shows attribute paths such as ``LOG`RADIO`` whole in code examples (`help @feed taps`, `help palette`, `help event list` and others), where a backtick in the path cut the example short and left stray markup. A `\|` in a table's code span shows as `|`, as on the Docs site (`help named arguments`). Census help is version 1.0.3.
- `Metrics:Port=0` (game server) and `ConnectionServer:MetricsPort=0` (connection server) turn metrics off: no port answers `/metrics`, where they moved the scrape onto the main web port, which a reverse proxy publishes.

[1.0.1]: https://github.com/SharpMUSH/SharpMUSH/compare/v1.0.0...v1.0.1

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
