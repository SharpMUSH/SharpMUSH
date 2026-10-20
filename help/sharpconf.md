# @config attribs
These options control some attribute behavior.

- `adestroy=<boolean>`: Is the @adestroy attribute used?
- `amail=<boolean>`: Is the @amail attribute used?
- `player_listen=<boolean>`: Is @listen checked on players?
- `player_ahear=<boolean>`: Is @ahear triggered on players?
- `startups=<boolean>`: Accepted but ignored; @startup attributes run at every server start.
- `room_connects=<boolean>`: Are @aconnect and @adisconnect triggered on rooms?
- `read_remote_desc=<boolean>`: Accepted but ignored.
- `empty_attrs=<boolean>`: Can attributes be set to an empty string?
- `gender_attr=<string>`: The attribute that holds an object's gender, which pronoun substitution reads and [@gender] sets. SEX when blank, and by default.
- `reverse_shs=<boolean>`: Accepted but ignored; it has no effect on passwords or logins.

# @config chat
These options control chat system settings.

- `chan_cost=<number>`: Accepted but ignored; channels cost nothing to create.
- `max_channels=<number>`: How many channels each non-admin player may own.
- `max_player_chans=<number>`: Accepted but ignored; see max_channels.
- `noisy_cemit=<boolean>`: Accepted but ignored.
- `chan_title_len=<number>`: How long can @channel/title's be?
- `use_muxcomm=<boolean>`: Accepted but ignored; MUX-style channel aliases always work. See [MUXCOMSYS]
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
SharpMUSH has no money, so only quota_cost has an effect.

- `object_cost=<number>`: Accepted but ignored.
- `exit_cost=<number>`: Accepted but ignored.
- `link_cost=<number>`: Accepted but ignored.
- `room_cost=<number>`: Accepted but ignored.
- `queue_cost=<number>`: Accepted but ignored.
- `quota_cost=<number>`: How much @quota each object uses.
- `find_cost=<number>`: Accepted but ignored.

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
- `exits_connect_rooms=<boolean>`: Accepted but ignored.
- `zone_control_zmp_only=<boolean>`: Do we only perform control checks on ZMPs, or do we check ZMOs and ZMRs too?

# @config dump
These options affect database saves and other periodic checks.

- `dump_message=<string>`: Notification message for a database save.
- `dump_complete=<string>`: Notification message for the end of a save.
- `dump_warning_1min=<string>`: Notification one minute before a save.
- `dump_warning_5min=<string>`: Notification five minutes before a save.
- `warn_interval=<time>`: Seconds between automatic @wchecks.
- `purge_interval=<time>`: Seconds between automatic @purges.

A save is a backup: the messages are shown around every `@backup`, `@dump` and scheduled backup, and an empty one is not shown. How often backups are scheduled is the server's SHARPMUSH_BACKUP_INTERVAL setting, not an option, so PennMUSH's dump_interval is read and ignored; so is dbck_interval, since `@dbck` runs only when asked (see [event dump] and [event db]).

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

- `safer_ufun=<boolean>`: Accepted but ignored: the privilege rule described in [u()] always applies.
- `function_side_effects=<boolean>`: Are function side effects (functions which alter the database) allowed?

# @config log
These options affect logging.

- `log_commands=<boolean>`: Accepted but ignored.
- `log_forces=<boolean>`: Accepted but ignored.
