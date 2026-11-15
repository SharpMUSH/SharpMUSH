<!-- help-article
{
  "corpus": "help",
  "id": "sharpmush-features",
  "lookup": "sharpmush features",
  "aliases": ["features"],
  "sections": [
    {
      "id": "features-portal",
      "heading": "The web portal and accounts",
      "lookup": "sharpmush features portal",
      "aliases": ["sharpmush features accounts"]
    },
    {
      "id": "features-roles",
      "heading": "Roles and permissions",
      "lookup": "sharpmush features roles"
    },
    {
      "id": "features-community",
      "heading": "Wiki, feeds, pages and dice",
      "lookup": "sharpmush features community",
      "aliases": ["sharpmush features wiki"]
    },
    {
      "id": "features-plugins",
      "heading": "Plugins, Discord and scenes",
      "lookup": "sharpmush features plugins"
    },
    {
      "id": "features-packages",
      "heading": "Softcode packages",
      "lookup": "sharpmush features packages"
    },
    {
      "id": "features-softcode",
      "heading": "Softcode",
      "lookup": "sharpmush features softcode"
    },
    {
      "id": "features-functions",
      "heading": "Functions",
      "lookup": "sharpmush features functions"
    },
    {
      "id": "features-commands",
      "heading": "Commands and switches",
      "lookup": "sharpmush features commands"
    },
    {
      "id": "features-clients",
      "heading": "Clients, pictures and sound",
      "lookup": "sharpmush features clients"
    },
    {
      "id": "features-dialects",
      "heading": "TinyMUX, RhostMUSH and imports",
      "lookup": "sharpmush features dialects"
    },
    {
      "id": "features-administration",
      "heading": "Running the game",
      "lookup": "sharpmush features administration"
    }
  ]
}
-->
# SharpMUSH Features

SharpMUSH runs PennMUSH softcode and has everything below, which PennMUSH does not. Each line names the help topic that explains it. For where SharpMUSH and PennMUSH behave differently, see [pennmush compatibility].

- [sharpmush features portal]: the web portal and player accounts
- [sharpmush features roles]: roles and permissions
- [sharpmush features community]: the wiki, feeds, the page log and dice
- [sharpmush features plugins]: plugins, the Discord bridge and scenes
- [sharpmush features packages]: installable softcode packages
- [sharpmush features softcode]: what the language adds
- [sharpmush features functions]: function families PennMUSH lacks
- [sharpmush features commands]: new commands and new switches
- [sharpmush features clients]: pictures, sound, GMCP and screen readers
- [sharpmush features dialects]: TinyMUX and RhostMUSH code and databases
- [sharpmush features administration]: backups, storage and diagnostics

## The web portal and accounts

The game serves a web portal from the same server: a Play page with a terminal, mail, character profiles, a [gallery] of pictures for each character, scenes, the wiki, a world map, a softcode editor and staff pages. A new game is set up there, at `/setup`.

- An [accounts] login holds many characters. At the login screen, [register] and [login] reach the account, and [create] and [play] make and connect its characters. Wizards manage accounts with [@account].
- Players can also sign in through an outside provider, such as Discord, when staff turn it on.
- [@push] sends a notification to a player's phone or browser.
- The [command palette] (Ctrl+K) runs game commands from [palette entries], ordered by the player's [@context].
- [@config cosmetic portal] sets how the portal looks, and [layout themes] colour both the portal and the terminal.
- [@reality portal visibility] keeps reality layers apart in the portal too.

## Roles and permissions

- WIZARD and ROYALTY are [roles], and each power is a `game.` permission, held by characters, objects and accounts. A character also holds its account's roles.
- [@role] and [@permission] define the game's own roles and permissions, and give or refuse them to one object or account. [ROLES()], [HASROLE()] and [PERMISSION()] read them.
- A permission is decided as on Discord: an object's own override first, then its account's, then its roles, where one role that allows it wins over any that refuse it.
- [administrative capabilities] lists the permissions that guard snapshots, jobs, queues, profiling and reality layers. [security] gives an overview.
- The `approved` role and [ISAPPROVED()] mark characters that have met the game's own bar.

## Wiki, feeds, pages and dice

- The game keeps a [wiki], with revision history, categories and facts, that the portal shows and [@wiki] edits. [Wiki functions] read it.
- A [@feed] is a stream of messages, such as a radio channel or a staff log, that a softcode system builds on. [Feed functions] read feeds.
- When the game keeps a page log, `page/recall` and `page/conversations` read a player's own pages. See [page recall] and [page functions].
- [@roll] and [roll()] roll [dice] and keep each roll under a number that [rollinfo()] checks later.
- [@appdraft] keeps a form, such as a job request, as a draft the player fills in a field at a time.
- [recurring jobs] run an attribute on a schedule, kept in the world database.

## Plugins, Discord and scenes

[@plugin] lists the .NET plugins the game runs, and turns them on and off. A plugin adds commands, functions, portal pages and help; [plugin help] lists the help each one brings. These ship with SharpMUSH:

- Discord links game channels to Discord channels, and Discord roles and bans to the game's (`help @discord`).
- Scenes keep each scene's members and poses, with every edit of a pose, for the portal's scene pages (`help scene system`).
- World keeps grid maps whose places need no room each, and the portal draws them (`help @world`).
- MCP and LSP let AI tools and code editors check softcode against the game's own parser. Both start turned off.

## Softcode packages

A package is a set of softcode objects that installs, upgrades, rolls back and uninstalls as one, from the portal's Packages page. SharpMUSH ships with bundled packages, such as jobs, bulletin boards, character applications, +finger, +who and the scene commands. More come from package sources, which are Git repositories.

- [@package] turns objects on the game into a package.
- [@attribute packages] shows which package declared each standard attribute.
- Every new game has handler objects for [EVENTS] and [http] requests, so packages can answer them with no setup.

## Softcode

- Every command has an output, which the next command in the same action list reads as `%>`. See [command output].
- [named arguments] pass arguments by name, read as `%<name>`, and [hasarg()] tests for one.
- `%*` marks a [list separators] delimiter, so an item that contains the delimiter stays whole.
- [@input] passes each line a player types to an attribute, and [@map] runs an attribute for each element of a list.
- [cache()] and [memo()] keep the answers of slow code. [@attribute index] and [indexed()] find objects by an attribute's value without a search.
- [@attribute watch] runs event handler code whenever any copy of an attribute changes.
- [restrictedexpr()] evaluates code limited to the functions you allow, with its inputs as literal text.
- Each command runs under an [execution budget], and [queue budgets] limit the work waiting in the queue.
- [~] runs a command with strict argument parsing.

## Functions

- [Layout functions] draw boxes, tables, trees, gauges and panels, which each client gets at its own width, and the portal as layout. [layout themes] colour them.
- [MEDIA FUNCTIONS] write a sound, picture or pane once for every client.
- [JSON FUNCTIONS] add `json_array()`, `json_fill()` and `json_group_by()`.
- [rendermarkdown()] turns Markdown into game text.
- [schema functions] check and draw JSON against a schema.
- [gradient()], [notice()], [chain()], [dateparse()], [formq()], [standin()], [exitmap()] and [activity()] have no PennMUSH equivalent.
- [syntaxdiagnostics()] reports the parse errors in a piece of code.
- [account()] returns a character's account, and [context()] the commands its context stack puts first.

## Commands and switches

New commands, beyond those under the other headings:

- [walk] goes through a list of exits. [@gender] sets the gender attribute.
- [@locale] sets the language the game addresses you in. [@ascii] sets what a character becomes for a client without UTF-8.
- [@queue] lists, pauses and resumes queued work.

New switches on commands PennMUSH has:

- [@dolist then], [@mapsql then]: run something once the list or the query is done.
- [@sql prepare], [@mapsql prepare]: queries with `?` parameters.
- `/args` on [@function], [@command], [@include] and [@trigger] names the arguments.

## Clients, pictures and sound

- [MEDIA FUNCTIONS] and [CMDLINK()] reach MXP, Pueblo, the portal and plain text, each in its own form.
- [IMAGE ATTRIBUTES] name the pictures the portal shows for characters, rooms, exits and things.
- [gmcpsupports()] tells which [GMCP] modules a client asked for.
- A [screen reader] gets text that reads well aloud.
- [charset] sets a connection's character set, and [@ascii] the game's stand-ins for a client without UTF-8.
- [@theme] sets the layout theme an object's output is drawn in.

## TinyMUX, RhostMUSH and imports

- SharpMUSH imports PennMUSH, TinyMUX and RhostMUSH databases, with their configuration files. See [compatibility config].
- An attribute flagged `mux` or `rhost` is read as that server reads it. See [compatibility dialects] and [dialect switches].
- TinyMUX and RhostMUSH commands and functions are there too, such as the MUX channel commands, [piping], [toggles], [@pipe], [@rollback], [@recover], [@tag], [@livewire], [@rxlevel], [printf()], [strdistance()], [colordepth()] and [localfun] (RhostMUSH's @lfunction). A command whose name PennMUSH uses for something else is there under an `@dialect_`, `@rhost_` or `@mux_` name.
- [compatibility left out] lists what is not.

## Running the game

- [@backup] copies the live world, and [@storage] reports its disk use and purges old history.
- [object snapshots] undo a mistake on one room, exit or code object.
- [@reality] sets reality layers, off until staff turn them on, so objects in one room can be present to some viewers and not to others. RhostMUSH and TinyMUX reality levels import into them.
- [@secret] keeps passwords and tokens encrypted, out of reach of softcode.
- [@profile] records which functions and commands run, and [@ps/history] lists recent queue outcomes.
- An internal error is reported as an [exception] with an id the server log can be searched for.
- The server reports Prometheus metrics at `/metrics`. With the update agent, the portal updates the game's containers.

::: seealso
- [pennmush compatibility]
- [Getting Started]
- [topics]
:::
