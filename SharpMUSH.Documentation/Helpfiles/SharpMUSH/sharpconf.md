# @config parameters
Many of the mush's run-time options can be set from the game by wizards, using `@config/set <option>=<new value>`. Those that can be set with visible changes are listed below, grouped by category. See [@config <category>] for details on each.

Categories:
- Attribs
- Chat
- Cmds
- Cosmetic
- Costs
- Db
- Dump
- Flags
- Funcs
- Limits
- Log
- Net
- Tiny

The categories and groups are the same as those used by `@config/list`. Values must be of the listed type for each option. They include: *<number>*, *<dbref>*, *<boolean>* (Yes/No), *<time>*, or *<string>*.

Options which take a *<time>* will accept either a number of seconds or a combination of numbers followed by 's' for seconds, 'm' for minutes or 'h' for hours, making `1h30m` and `5400` equivalent.

*<dbref>* options can be given with or without the leading '#', so '1' and '#1' are the same.

A few options map names to lists of words, such as `command_aliases`, `command_restrictions`, `sitelock_rules` and `mssp`. `@config <category>` and `@config <option>` show one entry per line, the name followed by its words.

# @config attribs
These options control some attribute behavior.

- `adestroy=<boolean>`: Is the @adestroy attribute used?
- `amail=<boolean>`: Is the @amail attribute used?
- `player_listen=<boolean>`: Is @listen checked on players?
- `player_ahear=<boolean>`: Is @ahear triggered on players?
- `startups=<boolean>`: Are @startup triggered at restart?
- `room_connects=<boolean>`: Are @aconnect and @adisconnect triggered on rooms?
- `read_remote_desc=<boolean>`: Can anyone remotely retrieve @descs?
- `empty_attrs=<boolean>`: Can attributes be set to an empty string?
- `reverse_shs=<boolean>`: Reverse the endedness of the shs password encryption? (**Warning**: Playing with this will break logins)

# @config chat
These options control chat system settings.

- `chan_cost=<number>`: How many pennies a channel costs to create.
- `max_channels=<number>`: How many channels can exist total.
- `max_player_chans=<number>`: How many channels can each non-admin player create? If 0, mortals cannot create channels.
- `noisy_cemit=<boolean>`: Is @cemit/noisy the default?
- `chan_title_len=<number>`: How long can @channel/title's be?
- `use_muxcomm=<boolean>`: Enable MUX-style channel aliases? See [MUXCOMSYS]
- `chat_token_alias=<character>`: A single character that can be used as well as + for talking on channels (+<chan> <msg>)
- `page_log=<boolean>`: Keep each player's pages so they can read their page history, in game (page/recall) and in the web portal? A SharpMUSH extension, on by default; set it to no to keep no page log. See [page log]
- `page_log_retention_days=<number>`: How many days a logged page is kept. -1 (the default) never deletes.

# @config cmds
These options affect command behavior.

- `noisy_whisper=<boolean>`: Does whisper default to whisper/noisy?
- `possessive_get=<boolean>`: Does "get container's object" work?
- `possessive_get_d=<boolean>`: Does it work on disconnected players?
- `link_to_object=<boolean>`: Can exits have objects as their destination?
- `owner_queues=<boolean>`: Are command queues kept per-owner, or per-object?
- `full_invis=<boolean>`: Should say by a dark player show up as 'Someone says,'?
- `wiz_noaenter=<boolean>`: If yes, dark players don't trigger @aenters.
- `really_safe=<boolean>`: Does SAFE prevent @nuking?
- `destroy_possessions=<boolean>`: When a player is destroyed, are their objects as well?

# @config costs
These options control how many pennies various things cost.

- `object_cost=<number>`: How many pennies it costs to create an object.
- `exit_cost=<number>`: How many pennies it costs to create an exit.
- `link_cost=<number>`: How many pennies it costs to use @link.
- `room_cost=<number>`: How many pennies it costs to @dig a room.
- `queue_cost=<number>`: How many pennies it costs to queue a command. Refunded when the command executes.
- `quota_cost=<number>`: How much @quota goes down by for each object.
- `find_cost=<number>`: How many pennies it costs to use @search, @find, @entrances, and their function versions.

# @config db
These are database options.

- `player_start=<dbref>`: What room newly created players are in.
- `master_room=<dbref>`: The location of the master room.
- `ancestor_room=<dbref>`: If set to a good object, this is considered a global parent for all rooms. If -1 or a nonexistant object, then disabled.
- `ancestor_exit=<dbref>`: As ancestor_room for exits.
- `ancestor_thing=<dbref>`: As ancestor_room for things.
- `ancestor_player=<dbref>`: As ancestor_room for players.
- `base_room=<dbref>`: The starting room used to determine if other rooms are disconnected.
- `default_home=<dbref>`: The room to send things to when they're homeless.
- `exits_connect_rooms=<boolean>`: Is a room with any exit at all in not considered disconnected for FLOATING checks?
- `zone_control_zmp_only=<boolean>`: Do we only perform control checks on ZMPs, or do we check ZMOs and ZMRs too?

# @config dump
These options affect database saves and other periodic checks.

- `forking_dump=<boolean>`: Does the game clone itself and save in the copy, or just pause while the save happens?
- `dump_message=<string>`: Notification message for a database save.
- `dump_complete=<string>`: Notification message for the end of a save.
- `dump_warning_1min=<string>`: Notification one minute before a save.
- `dump_warning_5min=<string>`: Notification five minutes before a save.
- `dump_interval=<time>`: Seconds between database saves.
- `warn_interval=<time>`: Seconds between automatic @wchecks.
- `purge_interval=<time>`: Seconds between automatic @purges.
- `dbck_interval=<time>`: Seconds between automatic @dbcks.

# @config flags
These options set the default flags for newly-created objects and channels. Each is a
space-separated list, and setting one replaces the default rather than adding to it.

- `player_flags=<string>`: List of flags to set on newly created players. Default: `enter_ok ansi no_command`
- `room_flags=<string>`: List of flags to set on newly created rooms. Default: `no_command`
- `thing_flags=<string>`: List of flags to set on newly created things. Default: `no_command`
- `exit_flags=<string>`: List of flags to set on newly created exits. Default: none
- `channel_flags=<string>`: List of flags to set on newly created channels. Default: `player`

An object set NO_COMMAND is not checked for `$`-commands. Use `@set <object>=!NO_COMMAND` on the
ones that carry them, or drop the flag from `thing_flags` if your game puts `$`-commands on things
as a matter of course.

::: seealso
- [NO_COMMAND]
- [@set]
- [FLAG LIST]
:::

# @config funcs
These options affect the behavior of some functions.

- `safer_ufun=<boolean>`: Are objects stopped from evaluting attributes on objects with more privileges than themselves?
- `function_side_effects=<boolean>`: Are function side effects (functions which alter the database) allowed?

# @config log
These options affect logging.

- `log_commands=<boolean>`: Are all commands logged?
- `log_forces=<boolean>`: Are @forces of wizard objects logged?

# @config net
Networking and connection-related options.

- `mud_name=<string>`: The name of the mush for mudname() and @version and the like.
- `mud_url=<string>`: If this is set, the welcome message for the mush is bracketed in <!-- ... --> for all clients, and web browsers are redirected to the url described in mud_url. First-run setup fills it in with the address the web portal was reached at, unless that is the server's own machine. An MXP or Pueblo client is sent the game's own pictures, such as the connect screen's logo, at this address; while it is unset, such a client is shown their text instead.
- `http_handler=<dbref/number>`: If this is set, support HTTP requests to MUSH port.
- `http_per_second=<number>`: If this is set, limit HTTP requests allowed per second.
- `use_dns=<boolean>`: Are IP addresses resolved into hostnames?
- `logins=<boolean>`: Are mortal logins enabled?
- `player_creation=<boolean>`: Can CREATE be used from the login screen?
- `guests=<boolean>`: Are guest logins allowed?
- `pueblo=<boolean>`: Is Pueblo support turned on?
- `sql_platform=<string>`: What kind of SQL server are we using? ("mysql", "postgreql", "sqlite" or "disabled")
- `sql_host=<string>`: What is the hostname or ip address of the SQL server
- `ssl_require_client_cert=<boolean>`: Are client certificates verified in SSL connections?

# @config tiny
Options that help control compability with TinyMUSH servers.

- `null_eq_zero=<boolean>`: Is a null string where a number is expected considered a 0?
- `tiny_booleans=<boolean>`: Use Tiny-style boolean values where only non-zero numbers are true.
- `tiny_trim_fun=<boolean>`: Are the second and third arguments to trim() reversed?
- `tiny_math=<boolean>`: Is a string where a number is expected considered a 0?
- `silent_pemit=<boolean>`: Does @pemit default to @pemit/silent?
- `paren_groups=<boolean>`: Does an unescaped `(` that starts no function call open a literal group, as in PennMUSH? Its commas are text and its `)` does not close the call around it, so `cat(x,(a,b)c)` has two arguments. When off, escape literal parentheses inside function arguments with `\(` `\)` or `%(` `%)`. Importing a PennMUSH database turns it on.
