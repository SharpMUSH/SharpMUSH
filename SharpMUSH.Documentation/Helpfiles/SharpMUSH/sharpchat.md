# CHAT
# CHAT SYSTEM
# comsys
# CHANNELS

SharpMUSH has a built-in chat system which allows you to speak to other players who are on the same channel without needing to be in the same room as them. It supports a large number of channels which can be customized and restricted in various ways.

Many of the chat system commands take a *\<channel\>* argument; you don't need to enter the entire channel name, only as many letters as needed to make it distinct from other channels.

You can list, join, and configure channels using the `@channel` command.

To speak on channels, use the `@chat` command.

There are some aliases in place for players more familiar with the MUX comsys - see [MUXCOMSYS] for more details.

::: seealso
- [@channel]
- [@chat]
- [@CEMIT]
- [channel functions]
- [CHAN_USEFIRSTMATCH]
- [@chatformat]
- [@CHANNEL CLOCK]
:::

# @chat
# +

- `@chat <channel>=<message>`
- `+<channel> <message>`

The `@chat` command is used to speak on channels. Everyone on the channel will see your message, and it will be added to the channel's recall buffer, if it has one. If *\<message\>* begins with a ':' or ';' it will be posed (or semiposed) instead of spoken. You will usually need to join a channel before you can speak on it.

`+<channel> <message>` is short-hand for the `@chat` command.

**Example**
```sharp
> @chat pub=Hello
<Public> Mike says, "Hello"
> +pub :waves
<Public> Mike waves
```

::: seealso
- [@channel]
- [@CEMIT]
:::

# CHAN_USEFIRSTMATCH

**Flag**: CHAN_USEFIRSTMATCH (any type)

Normally, when an object attempts to speak on the channel system with @chat, using an ambiguous channel name produces an error message. With this flag set, it will instead speak on the first channel whose name is a match. Other commands in the chat system are not affected by the flag.

::: seealso
- [CHAT]
- [@chat]
- [@CEMIT]
:::

# @CEMIT
# @NSCEMIT
# CEMIT()
# NSCEMIT()

- `@cemit[/noisy|/silent][/noeval] <channel>=<message>`
- `@nscemit[/noisy|/silent][/noeval] <channel>=<message>`
- `cemit(<channel>, <message>[, <noisy>])`
- `nscemit(<channel>, <message>[, <noisy>])`

@cemit emits *\<message\>* on *\<channel\>*. It does not include your name. The channel prefix is included if the /noisy switch is given, and omitted if /silent is given - if neither is given, the default behaviour is controlled by the noisy_cemit @config option. The /noeval switch prevents *\<message\>* from being evaluated.

You must be able to speak on the channel, or have the See_All and Pemit_All @powers, to @cemit on the channel.

@nscemit is exactly the same, but does not produce nospoof information when used by players with the Can_spoof @power.

cemit() and nscemit() work the same as @cemit/silent and @nscemit/silent, respectively. If *\<noisy\>* is given as a true value, they work like @cemit/noisy and @nscemit/noisy, respectively, instead.

@cemit is intended for use in writing extended chat systems. 

::: seealso
- [@chat]
:::

# @channel

The `@channel` command is used to add, join, list and modify channels in the chat system. It takes many different switches.

Help for `@channel` is split into a number of topics. Please see [@channel \<topic\>] for more, where *\<topic\>* is one of the words below. For help on a specific switch to `@channel`, use [@channel/<switch>].

- **Joining** - How to find, join, and leave channels
- **Other** - Setting channel titles, recalling previous chat messages
- **Admin** - Adding, deleting and modifying channels

::: seealso
- [CHAT]
- [@chat]
- [@CEMIT]
- [channel functions]
:::

# @CHANNEL OTHER
# @channel/recall
# @channel/title
# @channel/buffer

- `@channel/recall[/last] <channel>[=<count>]`
- `@channel/title <channel>=<title>`
- `@channel/buffer <channel>=<size>`

`@channel/recall` displays the last *\<count\>* messages sent on *\<channel\>*, oldest first, between a `CHAT: Recall from channel <name>` header and a `CHAT: End recall` footer, with each line stamped with the time it was said. If *\<count\>* is not given, it shows the last 10; `=0` shows the whole buffer. A second right-hand argument (`@channel/recall <channel>=<count>,<start>`) starts the replay at the *\<start\>*th line of the buffer. The /quiet switch drops the timestamps, keeping the header and footer. You need not be a member to recall from a channel — being able to join it is enough.

`@channel/title` sets your title on *\<channel\>*. Your title appears in front of your name when you speak on the channel, if the channel is set to show titles. `@channel/title <channel>=` with nothing after the `=` clears it, and `@channel/title <channel>` with no `=` at all tells you what it currently is. A title may not be longer than the `chan_title_len` configuration setting, and may not contain a newline, a tab or a bell.

`@channel/buffer` sets the recall buffer size for *\<channel\>* to *\<size\>*. Only channel admins can do this. A size of 0 disables the recall buffer.

::: seealso
- [@channel joining]
- [@CHANNEL ADMIN]
:::

# @CHANNEL ADMIN
# @channel/add
# @channel/delete
# @channel/mogrifier
# @channel/chown
# @channel/name
# @channel/rename
# @channel/describe
# @channel/privs
# @channel/wipe
# @channel/decompile

- `@channel/add <channel>=<privlist>`
- `@channel/delete <channel>`
- `@channel/mogrifier <channel>=<object>`
- `@channel/chown <channel>=<player>`
- `@channel/name <channel>=<newname>`
- `@channel/rename <channel>=<newname>`
- `@channel/describe <channel>=<description>`
- `@channel/privs <channel>=<privlist>`
- `@channel/wipe <channel>`
- `@channel/decompile[/brief] <channel>`
- `@clock/join|/speak|/see|/hide|/mod <channel>[=<lock>]`

`@channel/add` creates a new channel with the privileges in *<privlist>* — the same list `@channel/privs` takes, and it is required. You may not own more channels than the `max_channels` limit allows unless you are staff.

`@channel/delete` removes a channel. Only channel admins can do this.

`@channel/mogrifier` sets an object to be the channel's mogrifier. Only channel admins can do this. See [mogrifier] for details.

`@channel/chown` changes the owner of a channel. Only channel admins can do this.

`@channel/name` and `@channel/rename` both rename a channel. Only channel admins can do this.

`@channel/describe` changes a channel's description. Only channel admins can do this.

`@channel/privs` changes a channel's privileges. Only channel admins can do this. See [@CHANNEL PRIVS] for details.

`@channel/wipe` removes all players from a channel. Only channel admins can do this.

`@channel/decompile` prints the commands that would recreate the channel: its privileges, owner, mogrifier, locks, description, buffer size and membership. You must be able to decompile the channel. The /brief switch stops before the membership; members hiding on the channel are listed only to someone who could see them on [@channel joining].

Channel locks are managed by the separate `@clock` command, not by a `@channel` switch. See [@CHANNEL CLOCK] for details.

::: seealso
- [@channel joining]
- [@CHANNEL ADMIN]
- [@CHANNEL CLOCK]
:::

# @CHANNEL LIST

The output of `@channel/list` is one header line and one row per channel:
```text
Name                           Users     Msgs Chan Type        Status    Buf
Public                            12      843 [-P----o -----*] [On     ]   1
Admin                              3       17 [-P-A--- js---- ] [Off    ]   0
```

- **Name** is the channel's name, padded to 30 columns. A longer name overflows the column rather than being truncated.
- **Users** is how many of its members are listed by [@channel joining] — connected players, things, and members hiding on the channel only if you may see them.
- **Msgs** is how many messages the channel's recall buffer holds.
- **Chan Type** is two groups in one bracket. The first seven characters are the channel's privileges, a `-` where the privilege is absent: `D`isabled, `P`layer, Object (`T`), `A`dmin or `W`izard, `a`nnounce, `H`ide_ok, `o`pen. The next six are its locks and your relationship to it: `j`oin, `s`peak, `m`od, see (`v`), `h`ide, and `*` if you own the channel.
- **Status** is `On`, `Off`, or `Gag` if you are gagging the channel, followed by a character each for your own `Q` (muted), `H` (hidden) and `C` (combined) flags on it.
- **Buf** is the channel's configured buffer size.

`@channel/list/quiet` replaces all of that with a single line:
```text
CHAT: Channel list: Public, Admin
```
which reads `(None)` when nothing matched.

::: seealso
- [@channel joining]
- [@CHANNEL ADMIN]
- [@CHANNEL CLOCK]
:::

# @CHANNEL PRIVS
# CHANNEL-PRIVS

`@channel/privs <channel>=<privlist>`

Channel privileges say who a channel admits and how its messages are rendered. They are set with `@channel/privs`, and on a brand new channel with `@channel/add <channel>=<privlist>`.

*<privlist>* is a space-separated list of privilege names, or of the single-letter abbreviations below. The list **replaces** the channel's current privileges rather than adding to them, so name every privilege the channel is to keep. Names match without regard to case; the letters do not, because `O` is Object and `o` is Open, and `A` is Admin and `a` is Announce.

Available privileges:
- **player** (`P`): Players may use the channel
- **object** (`O`): Non-players may use the channel
- **admin** (`A`): Only royalty, wizards, and holders of the `chat_privs` power may use the channel
- **wizard** (`W`): Only wizards may use the channel
- **announce** (`a`): The channel shows its members' connect and disconnect messages, and keeps them in its recall buffer. Without it a channel says nothing when a member connects or disconnects; [@channel joining]'s `@channel/who` shows who is on it now.
- **open** (`o`): You may speak on the channel even when you are not listening to it
- **hide_ok** (`H`): You may hide yourself from the channel's who list
- **notitles** (`T`): Channel titles are not shown in channel messages
- **nonames** (`N`): Speakers' names are not shown in channel messages
- **nocemit** (`C`): [@CEMIT] is prohibited on the channel
- **interact** (`I`): Channel output is filtered through the game's interaction rules
- **disabled** (`D`): No one can join or speak on the channel

These are privileges, not locks. A privilege says which *kind* of thing the channel is open to; a lock says which particular objects get through. Joining, speaking, seeing, hiding and modifying are each governed by a lock of their own — see [@CHANNEL CLOCK].

PennMUSH has the opposite privilege, `quiet`, and announces connections on every channel without it. SharpMUSH has no `quiet`: a channel is quiet unless it has `announce`, and a PennMUSH channel imports quiet.

`loud` is not a channel privilege and cannot be given to a channel. It is a flag set on an object — see [FLAG LIST].

**Examples**
```sharp
@channel/privs Public=player announce open nocemit
@channel/privs Admin=player admin
@channel/privs Public=P a o C
```

::: seealso
- [@channel joining]
- [@CHANNEL CLOCK]
- [@CHANNEL CLOCK]
- [cflags()]
:::

# @CHANNEL CLOCK
# @channel/clock
# @clock

`@clock/join <channel>[=<lock>]`<br>
`@clock/speak <channel>[=<lock>]`<br>
`@clock/see <channel>[=<lock>]`<br>
`@clock/hide <channel>[=<lock>]`<br>
`@clock/mod <channel>[=<lock>]`

Channel locks are set with `@clock`, which is its own command — there is no `@channel/clock` switch. Each switch names the one lock it sets, and omitting *<lock>* removes that lock. With no switch at all, `@clock` sets the join lock. See [lock keys] for what may go in a *<lock>*.

There are five locks:
- **join**: Restricts who can join the channel
- **speak**: Restricts who can speak on the channel
- **see**: Restricts who can see the channel on `@channel/list`
- **hide**: Restricts `@channel/hide` on a channel that is `hide_ok`
- **mod**: Restricts who can modify the channel. Passing the mod lock lets you do anything to a channel short of deleting it

A lock is evaluated against the object trying to pass it, exactly as if it had been set on that object.

You may set a channel's locks if you own it, if you pass its mod lock, or if you are a wizard. A new channel starts with none of the five set, and an unset lock is one everybody passes.

**Examples**
```sharp
@clock/join Public=flag^wizard
@clock/speak Public=!flag^gagged
@clock/mod Secret=#123
@clock/speak Public=
```

::: seealso
- [LOCKING]
- [LOCKING]
- [lock keys]
- [@CHANNEL PRIVS]
:::

# MUXCOMSYS
# ADDCOM
# DELCOM
# COMLIST
# COMTITLE

`addcom <alias>=<channel>`<br>
`delcom <alias>`<br>
`comlist`<br>
`comtitle <alias>=<title>`

SharpMUSH provides the MUX comsys commands for players more familiar with them:

- `addcom <alias>=<channel>` joins `<channel>` and remembers `<alias>` for it
- `delcom <alias>` leaves the channel `<alias>` names and forgets the alias
- `comlist` lists your aliases and the channels they name
- `comtitle <alias>=<title>` is `@channel/title <channel>=<title>`
- `<alias> <message>` > `@chat <channel>=<message>`
- `<alias>:` > `@chat <channel>=:`
- `<alias>;` > `@chat <channel>=;`

Aliases are stored on you as `` CHANALIAS`<alias> `` attributes, so they survive a disconnect. Where a command takes a channel name rather than an alias you must give enough of the name to identify it uniquely.

::: seealso
- [@channel]
- [@chat]
- [CHAN_USEFIRSTMATCH]
:::
