# Chat
# Chat System
# comsys
# Channels

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

# @cemit
# @nscemit
# cemit()
# nscemit()

- `@cemit[/noisy|/silent][/noeval][/spoof] <channel>=<message>`
- `@nscemit[/noisy|/silent][/noeval] <channel>=<message>`
- `cemit(<channel>, <message>[, <noisy>])`
- `nscemit(<channel>, <message>[, <noisy>])`

@cemit emits *\<message\>* on *\<channel\>*. It does not include your name. The channel prefix is included if the /noisy switch is given, and omitted if /silent is given - if neither is given, the default behaviour is controlled by the noisy_cemit @config option. The /noeval switch prevents *\<message\>* from being evaluated.

You must be able to speak on the channel, or have the See_All and Pemit_All @powers, to @cemit on the channel.

@nscemit is exactly the same, but does not produce nospoof information when used by players with the Can_spoof @power. `@cemit/spoof` is the same as @nscemit.

cemit() and nscemit() work the same as @cemit/silent and @nscemit/silent, respectively. If *\<noisy\>* is given as a true value, they work like @cemit/noisy and @nscemit/noisy, respectively, instead.

@cemit is intended for use in writing extended chat systems. 

::: seealso
- [@chat]
:::

# @channel

The `@channel` command is used to add, join, list and modify channels in the chat system. It takes many different switches.

Help for `@channel` is split into a number of topics. For help on a specific switch to `@channel`, use `help @channel/<switch>`.

- [@channel joining] - How to find, join, and leave channels
- [@channel other] - Setting channel titles, recalling previous chat messages
- [@channel admin] - Adding, deleting and modifying channels
- [@channel clock] - Locking who may join, speak on, see or moderate a channel

::: seealso
- [CHAT]
- [@chat]
- [@CEMIT]
- [channel functions]
:::

# @channel other
# @channel/recall
# @channel/title
# @channel/buffer

- `@channel/recall[/quiet] <channel>[=<count>]`
- `@channel/title <channel>=<title>`
- `@channel/buffer <channel>=<size>`

`@channel/recall` displays the last *\<count\>* messages sent on *\<channel\>*, oldest first, between a `CHAT: Recall from channel <name>` header and a `CHAT: End recall` footer, with each line stamped with the time it was said. If *\<count\>* is not given, it shows the last 10; `=0` shows the whole buffer. A second right-hand argument (`@channel/recall <channel>=<count>,<start>`) starts the replay at the *\<start\>*th line of the buffer. The /quiet switch drops the timestamps, keeping the header and footer. You need not be a member to recall from a channel; being able to join it is enough.

`@channel/title` sets your title on *\<channel\>*. Your title appears in front of your name when you speak on the channel, if the channel is set to show titles. `@channel/title <channel>=` with nothing after the `=` clears it, and `@channel/title <channel>` with no `=` at all tells you what it currently is. A title may not be longer than the `chan_title_len` configuration setting, and may not contain a newline, a tab or a bell.

`@channel/buffer` sets the recall buffer size for *\<channel\>* to *\<size\>*. Only channel admins can do this. A size of 0 disables the recall buffer.

::: seealso
- [@channel joining]
- [@CHANNEL ADMIN]
:::

# @channel list

The output of `@channel/list` is one header line and one row per channel:
```text
Name                           Users     Msgs Chan Type        Status    Buf
Public                            12      843 [-P----o -----*] [On     ]   1
Admin                              3       17 [-P-A--- js---- ] [Off    ]   0
```

- **Name** is the channel's name, padded to 30 columns. A longer name overflows the column rather than being truncated.
- **Users** is how many of its members are listed by [@channel joining]: connected players, things, and members hiding on the channel only if you may see them.
- **Msgs** is how many messages the channel's recall buffer holds.
- **Chan Type** is two groups in one bracket. The first seven characters are the channel's privileges, a `-` where the privilege is absent: `D`isabled, `P`layer, Object (`T`), `A`dmin or `W`izard, `Q`uiet, `H`ide_ok, `o`pen. The next six are its locks and your relationship to it: `j`oin, `s`peak, `m`od, see (`v`), `h`ide, and `*` if you own the channel.
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

# MUX Comsys
# Muxcomsys
# addcom
# delcom
# comlist
# comtitle
# allcom
# clearcom

`addcom <alias>=<channel>`<br>
`delcom <alias>`<br>
`comlist`<br>
`comtitle <alias>=<title>`<br>
`@clist[/full]`

SharpMUSH provides the MUX comsys commands for players more familiar with them:

- `addcom <alias>=<channel>` joins `<channel>` and remembers `<alias>` for it
- `delcom <alias>` leaves the channel `<alias>` names and forgets the alias
- `comlist` lists your aliases, the channels they name, whether you are on each (`On`, `Gag` or `Off`) and your title there
- `comtitle <alias>=<title>` is `@channel/title <channel>=<title>`
- `<alias> <message>` > `@chat <channel>=<message>`, and `<alias> :<pose>` and `<alias> ;<pose>` pose
- `<alias> on` > `@channel/ungag <channel>`
- `<alias> off` > `@channel/gag <channel>`
- `<alias> who` > `@channel/who <channel>`
- `allcom on|off|who` does that on every channel your aliases name, and `clearcom` is `delcom` for every alias
- `@clist` > `@channel/list`; the `/full` switch is accepted and changes nothing

Aliases are stored on you as `` CHANALIAS`<alias> `` attributes, so they survive a disconnect. These commands and aliases work only while the `use_muxcomm` option is on; otherwise they answer `Command disabled.` Where a command takes a channel name rather than an alias you must give enough of the name to identify it uniquely.

::: seealso
- [@channel]
- [@chat]
- [CHAN_USEFIRSTMATCH]
- [MUX Channel Admin]
:::

# MUX Channel Admin
# @ccreate
# @cdestroy
# @cboot
# @cwho
# @cchown
# @coflags
# @cpflags

`@ccreate <channel>`<br>
`@cdestroy <channel>`<br>
`@cboot[/quiet] <channel>=<object>`<br>
`@cwho <channel>[/all]`<br>
`@cchown <channel>=<player>`<br>
`@coflags <channel>=[!]join`<br>
`@cpflags <channel>=[!]join`

TinyMUX's channel commands. Each runs the `@channel` command it stands for, as you:

- `@ccreate` > `@channel/add <channel>=player object`, a channel players and objects may join
- `@cdestroy` > `@channel/delete`
- `@cboot` > `@channel/off <channel>=<object>`; `/quiet` is accepted and changes nothing
- `@cwho` > `@channel/who`, which lists every member, so `/all` changes nothing
- `@cchown` > `@channel/chown`
- `@coflags` and `@cpflags` add or remove the `object` or `player` privilege: whether objects or players may join. TinyMUX's `transmit` and `receive` flags have no equivalent; `@clock/speak` chooses who may speak.

Like `addcom`, they work only while `use_muxcomm` is on. `@cset` changes a channel's other settings.

::: seealso
- [@cset]
- [MUX Comsys]
- [@CHANNEL ADMIN]
:::

# @cset

`@cset/<option> <channel>[=<value>]`

TinyMUX's `@cset` changes one channel setting. Each option is a `@channel` or `@clock` change, made as you:

- `/public`: removes the see lock (`@clock/see <channel>=`), so everyone finds the channel in `@channel/list`
- `/private`: sets the see lock to `#FALSE`, so only its members and those with See_All find it
- `/loud`: removes the `quiet` privilege, so connects and disconnects are announced
- `/mute` or `/quiet`: adds the `quiet` privilege
- `/spoof` or `/anon`: adds the `nonames` privilege, so a message shows the title without the speaker's name
- `/nospoof`: removes the `nonames` privilege
- `/log`: `@channel/buffer <channel>=<value>`, how many messages the channel keeps
- `/object`: `@channel/mogrifier <channel>=<value>`, the object that stands in for TinyMUX's channel object (see [mogrifier members])

`/header` and `/timestamp_logs` have no SharpMUSH equivalent, and say so.

Example:

```
> @cset/mute Public
> think clflags(Public)
```

Output: `Player Object Quiet`

::: seealso
- [MUX Channel Admin]
- [@channel/buffer]
- [@CHANNEL CLOCK]
:::
