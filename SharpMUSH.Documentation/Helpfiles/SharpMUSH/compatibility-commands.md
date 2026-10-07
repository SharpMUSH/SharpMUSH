<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-commands",
  "lookup": "compatibility commands",
  "aliases": [],
  "sections": [
    {
      "id": "added-commands-do-not-run-unimplemented-command",
      "heading": "Added commands do not run `UNIMPLEMENTED_COMMAND`",
      "lookup": "compatibility commands added commands do not run unimplemented command"
    },
    {
      "id": "config-set-is-stored",
      "heading": "`@config/set` is stored",
      "lookup": "compatibility commands config set is stored"
    },
    {
      "id": "command-restrictions-reapplies-without-a-restart",
      "heading": "`command_restrictions` reapplies without a restart",
      "lookup": "compatibility commands command restrictions reapplies without a restart"
    },
    {
      "id": "a-new-game-restricts-no-commands",
      "heading": "A new game restricts no commands",
      "lookup": "compatibility commands a new game restricts no commands"
    },
    {
      "id": "pennmush-s-file-and-allocator-housekeeping-answers-not-supported",
      "heading": "PennMUSH's file and allocator housekeeping answers `NOT SUPPORTED`",
      "lookup": "compatibility commands pennmush s file and allocator housekeeping answers not supported"
    },
    {
      "id": "the-http-and-event-handlers-exist-from-the-start",
      "heading": "The HTTP and event handlers exist from the start",
      "lookup": "compatibility commands the http and event handlers exist from the start"
    },
    {
      "id": "a-drop-to-move-s-event-names-who-caused-it",
      "heading": "A drop-to move's event names who caused it",
      "lookup": "compatibility commands a drop to move s event names who caused it"
    },
    {
      "id": "typed-input-is-bounded-by-a-pending-count-not-a-replenishing-rate",
      "heading": "Typed input is bounded by a pending count, not a replenishing rate",
      "lookup": "compatibility commands typed input is bounded by a pending count not a replenishing rate"
    }
  ]
}
-->
# Compatibility Commands

Deliberate differences in the command table, the configuration and the objects the game starts with.

## Added commands do not run `UNIMPLEMENTED_COMMAND`

**A choice.**

**PennMUSH** runs a command made with `@command/add` and never `@hook`ed through the
`UNIMPLEMENTED_COMMAND` entry (`command.c:1900-1903`), so a hook on `UNIMPLEMENTED_COMMAND` changes
what every such command does.<br>
**SharpMUSH** has the added command print "This command has not been implemented." itself. A hook on
`UNIMPLEMENTED_COMMAND` changes only what typing `UNIMPLEMENTED_COMMAND` does, and that can be typed
directly.<br>
**Why.** Both print the same text, so only a hook on `UNIMPLEMENTED_COMMAND` can tell them apart.
Re-entering hook dispatch by command name from inside a command body needs a mechanism only
`HUH_COMMAND` has. (#1257)<br>
**Workaround.** `@hook` the added command itself rather than `UNIMPLEMENTED_COMMAND`.

```sharp unchecked
> @command/add foo
> foo
This command has not been implemented.
```

## `@config/set` is stored

**A choice.**

**PennMUSH** changes the running value; the change is lost at restart unless it is also written to
`mush.cnf`.<br>
**SharpMUSH** keeps one stored configuration, which the portal also edits, and every `/set` is written
to it and lasts across restarts. `/save` does the same and says so.<br>
**Why.** There is no `mush.cnf` for a running game to fall back to.<br>
**Workaround.** Set the value back when a change was meant to be temporary.<br>
**No example.** The difference shows only after a restart, and the parity harness starts each server
once per run.

## `command_restrictions` reapplies without a restart

**A choice.**

**PennMUSH** applies the `restrict_command` lines of `mush.cnf` at startup and restart (`game.c:757`,
`bsd.c:1284`). `@config/set restrict_command=<command> <restriction>` applies one more while the game
runs (`cmds.c:334-335`, `conf.c:866-896`). Each one replaces the command's restriction
(`command.c:1752-1760`). There is no list to take a line back out of, so nothing returns a command to the
restriction it was made with.<br>
**SharpMUSH** keeps the lines as one setting, `command_restrictions`. `@config/set restrict_command`
adds a line to it, or replaces the line for that command, and applies it at once, as PennMUSH does. The
setting is applied at startup, before `@STARTUP` runs, as PennMUSH does, and again whenever it is changed
from the portal or `@config/set`, in both directions: a command the change no
longer restricts goes back to the restriction it was made with, and is enabled again if the
configuration had disabled it with `nobody`. Every command the old or the new setting names starts
again from the restriction it was made with, so a live `@command/restrict` on one of those commands
is lost. Commands the setting does not name, and changes to other options, leave live restrictions
alone. (#1250) `@config/set restrict_command` with words that are neither restriction words nor a
valid lock says `Couldn't set that option.`; PennMUSH says `Option set.` and leaves the command
unlocked.<br>
**Why.** The configuration can be changed while the game is running; a restriction that waits for a
restart looks as if it had been ignored. A mistyped restriction is refused rather than leaving the command
open to everyone.<br>
**Workaround.** After changing `command_restrictions`, repeat any `@command/restrict` that should
still apply to a command it names, or put the restriction in `command_restrictions` itself.<br>
**Example.** The parity case `choice.restrictions-reapply` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## A new game restricts no commands

**A choice.**

**PennMUSH** ships `restrict.cnf` and includes it from `mush.cnf`, so a stock game already applies 21
`restrict_command` lines at startup (`game/restrictcnf.dst`): a guest cannot `@create`, `@dig`,
`@open`, `@set`, `@link` or `@lock`, a FIXED player's `home` is refused with `You can't do that IC!`,
and `@destroy` is `noplayer` with its own message, so a player who types `@destroy <thing>` is
answered `Use @recycle instead` and the thing is not destroyed (`game/restrictcnf.dst:86`, sent by
`command.c:2336-2339`).<br>
**SharpMUSH** ships `command_restrictions` empty. Every command in a new game is open to whatever its
own permission check allows: a player's `@destroy <thing>` destroys the thing, and a guest can
`@create`. An imported game keeps its own: reading a PennMUSH `mush.cnf` follows its `include` lines,
as `config_file_startup` does, and carries each `restrict_command` line into `command_restrictions`
and each `restrict_function` line into `function_restrictions`, and both are applied at startup, so
an imported `restrict_function lstats noguest` answers a guest's `lstats()` with
`#-1 PERMISSION DENIED` as PennMUSH does. Only the `restrict_function` words that say who may call
a function are applied (`nobody`, `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`, `god`). A
word that changes how it runs (`nosidefx`, `logargs`, `noparse` and the like), or a `!` word that
clears one of a built-in's own restrictions, is kept but has no effect, and the import names it. A line that cannot be carried (an include that cannot be read, a restriction with no value,
a `restrict_attribute`) is logged by name, and so is every `include` in a `mush.cnf` uploaded through
the portal, which is not followed because it would name a file on the server.<br>
**Why.** Those lines are one site's policy, not the engine's behaviour; `restrict.cnf` is a file the
administrator edits, and two PennMUSH games rarely ship the same one. Taking `@destroy` away from
ordinary players on a fresh install is the game's decision to make, and SharpMUSH does not make it for
a world nobody has configured yet.<br>
**Workaround.** Put the lines back, in the words PennMUSH writes them:
`@config/set restrict_command=@destroy noplayer " Use @recycle instead`, one per command, or set
`command_restrictions` from the portal's configuration page. `noguest`, `nofixed`, `noplayer`,
`nobody`, a flag or power name, a lock and the `" <message>` suffix all mean there what they mean in
`restrict.cnf`, and the restriction applies at once and lasts across restarts. The same goes for
`function_restrictions` from the portal, with `noguest`, `nogagged`, `nofixed`, `admin`, `wizard`,
`god` and `nobody`.<br>
**Example.** The parity case `obj.create` in `tools/parity/scenarios/10-player-commands.scn` runs it on both servers.

## PennMUSH's file and allocator housekeeping answers `NOT SUPPORTED`

**A choice.**

**PennMUSH** trims its own log files with `@logwipe` and reports its attribute-chunk allocator with
`@stats/chunks`, `/regions`, `/paging` and `/freespace`.<br>
**SharpMUSH** writes its logs to the sinks named in its configuration and stores attributes in its
database, so it has neither. Those commands return `#-1 NOT SUPPORTED`, and `@logwipe` also records
the attempt in the server log.<br>
**Why.** Nothing in the game owns the resource those commands manage.<br>
**Workaround.** Rotate logs where they are configured.<br>
**Example.** The parity case `choice.housekeeping` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## The HTTP and event handlers exist from the start

**A choice.**

**PennMUSH** has no HTTP or event handler until a wizard creates an object and points
`http_handler` or `event_handler` at it.<br>
**SharpMUSH** creates the HTTP Handler (#8) and the Event Handler (#9) in a new database, with the
options already pointing at them and the default HTTP verb attributes installed.<br>
**Why.** The web portal is built on handler routes and needs them present.<br>
**Workaround.** Extend the shipped handlers rather than creating new ones. See `help http` and
`help event`.

A new PennMUSH database has no `#8` or `#9`, and answers `#-1 NO SUCH OBJECT VISIBLE` twice:

```sharp
> think [name(#8)]|[name(#9)]
HTTP Handler|Event Handler
```

## A drop-to move's event names who caused it

**A choice.**

**PennMUSH** reports the move of an object sent through a drop-to as caused by `SYSEVENT`
(`move.c:187`).<br>
**SharpMUSH** has no `SYSEVENT` object and passes on the enactor whose action caused the move. This
shows only in the enactor of the ``OBJECT`MOVE`` event. (#1006 item 9)<br>
**Why.** There is no `SYSEVENT` dbref to name.<br>
**Workaround.** Do not rely on an ``OBJECT`MOVE`` handler's enactor to tell a drop-to move from any
other.<br>
**Example.** The parity case `choice.dropto` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## Typed input is bounded by a pending count, not a replenishing rate

**A choice.**

**PennMUSH** keeps a typed line out of the queue entirely (`run_user_input` hands its
`QUEUE_SOCKET` entry straight to `do_entry` (`cque.c:1076-1088`), so it never touches the tally
`queue_limit` reads) and bounds it on the descriptor instead: a burst of `COMMAND_BURST_SIZE`
commands that replenishes at `COMMANDS_PER_SECOND` (`conf.h:99-100`, `bsd.c:197,1000-1004`).<br>
**SharpMUSH** also keeps typed input out of `player_queue_limit`, so a full owner queue never stops
its owner typing and a typed line is never why another of that owner's commands is refused. It
bounds it by socket with `command_burst_size`, which defaults to the same 100 but caps the number
of typed lines *outstanding* rather than the rate at which they start. A connection over that cap
is told `Queue admission rejected: ConnectionLimit.` (#1320)<br>
**Why.** SharpMUSH queues typed input rather than running it inside the network loop, so a pending
count is the quantity it has; a replenishing rate would delay input the queue is already ready to
run.<br>
**Workaround.** None needed for softcode. Raise `command_burst_size` if a client that sends large
scripted bursts is refused.

PennMUSH answers `#-1 NO SUCH CONFIG OPTION`:

```sharp
> think config(command_burst_size)
100
```
