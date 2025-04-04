# @config parameters
Many of the mush's run-time options can be set from the game by wizards, using `@config/set <option>=<new value>`. Those that can be set with visible changes are listed below, grouped by category. See [help @config <category>|@config] for details on each.

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
- `use_muxcomm=<boolean>`: Enable MUX-style channel aliases? See [help muxcomsys|muxcomsys]
- `chat_token_alias=<character>`: A single character that can be used as well as + for talking on channels (+<chan> <msg>)

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

# @config cosmetic
These are cosmetic options of various sorts.

- `money_singular=<string>`: What is one penny called?
- `money_plural=<string>`: What are many pennies called?
- `player_name_spaces=<boolean>`: Can player names have spaces in them?
- `ansi_names=<boolean>`: Are names in look hilighted?
- `monikers=<list>`: Where should @monikers be displayed? See [help monikers|monikers]
- `float_precision=<numbers>`: How many digits after the decimal point in floating point numbers are kept when formatting the result of a floating point function?
- `comma_exit_list=<boolean>`: Do exits show up like North, East, and West or as North East West?
- `count_all=<boolean>`: Does the count of connected players in WHO include hidden connections for mortals?

See [help @config cosmetic2|@config cosmetic2]

# @config cosmetic2
More cosmetic options.

- `page_aliases=<boolean>`: Are aliases included in page listings? For example, Foo(F) pages: Blah
- `flags_on_examine=<boolean>`: Are flag names included when examining objects?
- `ex_public_attribs=<boolean>`: Show visual attributes when examining objects you don't control?
- `wizwall_prefix=<string>`: Prefix for @wizwall messages.
- `rwall_prefix=<string>`: Prefix for @rwall messages.
- `wall_prefix=<string>`: Prefix for @wall messages.
- `announce_connects=<boolean>`: Should (dis)connects be announced to non-HEAR_CONNECT players and to channels?
- `chat_strip_quote=<boolean>`: Does +chan "foo strip the "?
- `newline_one_char=<boolean>`: Is strlen(%r) equal to 1?
- `only_ascii_in_names=<boolean>`: Names are ascii-only or are extended characters permitted?

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
These options set the default flags for newly-created objects and channels.

- `player_flags=<string>`: List of flags to set on newly created players
- `room_flags=<string>`: List of flags to set on newly created rooms
- `thing_flags=<string>`: List of flags to set on newly created things
- `exit_flags=<string>`: List of flags to set on newly created exits
- `channel_flags=<string>`: List of flags to set on newly created channels

# @config funcs
These options affect the behavior of some functions.

- `safer_ufun=<boolean>`: Are objects stopped from evaluting attributes on objects with more privileges than themselves?
- `function_side_effects=<boolean>`: Are function side effects (functions which alter the database) allowed?

# @config limits
Limits and other constants.

- `max_dbref=<dbref>`: The highest dbref an object can have. If 0, there is no limit on database size.
- `max_attrs_per_obj=<number>`: The maximum attributes an object can have.
- `max_logins=<number>`: The maximum number of connected players.
- `max_guests=<number>`: The maximum number of connected guests. If 0, no limit. If -1, limited by the number of guest players in the db.
- `max_named_qregs=<number>`: The maximum number of qregs except for a-z and 0-9. The limit is per-localize()-call.
- `connect_fail_limit=<count>`: The maximum number of times in a 10 minute period that an IP can attempt to log in and fail. Maximum is 50, 0 means no limit.
- `idle_timeout=<time>`: The number of minutes a connection can be idle before getting booted. 0 means no limit.
- `unconnected_idle_timeout=<time>`: The number of minutes a connection can be sitting at the login screen before getting booted. 0 means no limit.

See [help @config limits2|@config limits2]

# @config limits2
Limits and constants, continued.

- `whisper_loudness=<number>`: The percentage chance of a whisper/noisy being heard.
- `starting_quota=<number>`: How much quota new players get.
- `starting_money=<number>`: How many pennies new players get.
- `paycheck=<number>`: How many pennies players get each day they log on.
- `max_pennies=<number>`: The maximum pennies an object can have.
- `mail_limit=<number>`: How many @mail messages someone can have.
- `max_depth=<number>`: How deep indirect @lock chains can go.
- `player_queue_limit=<number>`: The number of commands a player can have queued at once.
- `queue_loss=<number>`: One in <number> times, queuing a command will cost an extra penny that doesn't get refunded.
- `queue_chunk=<number>`: How many queued commands get executed in a row before checking for network activity.

See [help @config limits3|@config limits3]

# @config limits3
Limits and constants, continued.

- `function_recursion_limit=<number>`: The depth to which softcode functions can call more functions.
- `function_invocation_limit=<number>`: The maximum number of softcode functions that can be called in one command.
- `guest_paycheck=<number>`: How many pennies guests get each day.
- `max_guest_pennies=<number>`: The maximum pennies a guest can have.
- `player_name_len=<number>`: The maximum length of a player name.
- `queue_entry_cpu_time=<number>`: The maximum number of milliseconds a queue entry can take to run.
- `use_quota=<boolean>`: Controls if quotas are used to limit the number of objects a player can own.

See [help @config limits4|@config limits4]

# @config limits4
Limits and constants, continued.

- `max_aliases=<number>`: The maximum number of aliases a player can have.
- `keepalive_timeout=<time>`: How often should an 'Are you still there?' query be sent to clients, to stop players' routers booting idle connections?
- `max_parents=<number>`: The maximum number of levels of parenting allowed.
- `call_limit=<number>`: The maximum number of times the parser can be called recursively for any one expression.
- `chunk_migrate=<number>`: Maximum number of attributes that can be moved to disk cache per second.

# @config log
These options affect logging.

- `log_commands=<boolean>`: Are all commands logged?
- `log_forces=<boolean>`: Are @forces of wizard objects logged?

# @config net
Networking and connection-related options.

- `mud_name=<string>`: The name of the mush for mudname() and @version and the like.
- `mud_url=<string>`: If this is set, the welcome message for the mush is bracketed in <!-- ... --> for all clients, and web browsers are redirected to the url described in mud_url.
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