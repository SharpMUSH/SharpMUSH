# CHAT
# CHAT SYSTEM
# comsys
# CHANNELS

SharpMUSH has a built-in chat system which allows you to speak to other players who are on the same channel without needing to be in the same room as them. It supports a large number of channels which can be customized and restricted in various ways.

Many of the chat system commands take a *<channel>* argument; you don't need to enter the entire channel name, only as many letters as needed to make it distinct from other channels.

You can list, join, and configure channels using the `@channel` command.

To speak on channels, use the `@chat` command.

There are some aliases in place for players more familiar with the MUX comsys - see [help muxcomsys|muxcomsys] for more details.

## See Also
- [help @channel|@channel]
- [help @chat|@chat]
- [help @cemit|@cemit]
- [help channel functions|channel functions]
- [help CHAN_USEFIRSTMATCH|CHAN_USEFIRSTMATCH]
- [help @chatformat|@chatformat]
- [help @clock|@clock]

# @chat
# +

`@chat <channel>=<message>`
`+<channel> <message>`

The `@chat` command is used to speak on channels. Everyone on the channel will see your message, and it will be added to the channel's recall buffer, if it has one. If *<message>* begins with a ':' or ';' it will be posed (or semiposed) instead of spoken. You will usually need to join a channel before you can speak on it.

`+<channel> <message>` is short-hand for the `@chat` command.

## Example
```
> @chat pub=Hello
<Public> Mike says, "Hello"
> +pub :waves
<Public> Mike waves
```

## See Also
- [help @channel|@channel]
- [help @cemit|@cemit]

# @CHATFORMAT

`@chatformat <object>[=<message>]`

The chatformat attribute is evaluated when an object receives a channel message. If the attribute exists, its evaluated result is shown to the object instead of the default message. If the attribute exists but returns nothing, the object will not see anything.

## Registers
- **%0**: The 'type' of the message. It is a single character that will always be set:
  - `"`, `;` or `:` for say, semipose and pose, respectively
  - `|` for an @cemit
  - `@` for a "system" message - such as "Walker has connected."
- **%1**: The channel name. e.g: "Public", "Admin", "Softcode"
- **%2**: The message as typed (post-evaluation, if necessary) by the speaker. Be warned, though - if type is '@', then %2 will contain the entire message, and will include the name of the speaker that caused it
- **%3**: The speaker name, unless channel is set NO_NAME
- **%4**: The speaker's channel title, unless none is set, or the channel is NO_TITLE
- **%5**: The default message, as shown when no chatformat is set
- **%6**: The 'say' string for the message. This will usually be "says", unless altered by the SPEECHTEXT mogrifier
- **%7**: A space-separated list of extra options. Currently can contain: "silent" if a silent @cemit caused this message, otherwise "noisy"

If the channel is NO_NAME, and the speaker either has no title or the channel is also set NO_TITLE, then %3 will be "Someone".

## See Also
- [help @chat|@chat]
- [help @pageformat|@pageformat]
- [help @message|@message]
- [help speak()|speak()]
- [help mogrify|mogrify]

# @CHATFORMAT2

## Examples

Walker's preferred @chatformat, which strips all ansi out, wraps every line to your width and prefixes them with <ChannelName>:

```
@chatformat me=<%1> [switch(%0,@,%2,edit(wrap(speak(&[if(%4,%4%b)]%3,%0[stripansi(%2)],%6\\,),sub(width(%!),add(4,strlen(%1)))),%r,%r<%1>%b))]
```

If you're on a system with chat_strip_quote set to "no", you might want to change the '%0%2' arg to speak() to '[switch(%0,\",%2,%0%2)]'

Suppose you want it just like the old version, but anytime somebody says your name, you want it all in red:

```
@chatformat me=ansi(switch(%2,*[name(%!)]*,r,n),%5)
```

See [help @chatformat3|@chatformat3] for more examples.

# @CHATFORMAT3

A popular feature in clients now available in SharpMUSH directly: Let's suppose you want "Public" channel chatter to all be green, "Softcode" to be blue and "Admin" to be cyan.

```
@chatformat me=ansi(switch(%1,Public,g,Softcode,b,Admin,c,n),%5)
```

Maybe you dislike players who re-@name themselves a lot:

```
&playernames me=#6061:Walker #7:Javelin #6388:Cheetah
@chatformat me=<%1> [switch(%0,@,%2,speak(&[if(%4,%4%b)][firstof(after(grab(v(playernames),%#:*),:),%3)],%2,%6\\,))]
```

Or you're writing a loggerbot, and you want to convert all channel input to HTML:

```
@chatformat me=CHAT:%1:[edit(switch(%0,@,%2,speak(if(%4,%4%b)%3,%0%2,%6\\,)),&,&amp;,<,&lt;,>,&gt;,%r,<BR>,%b%b,%b&nbsp;)]
```
or
```
@chatformat me=CHAT:%1:[render(switch(%0,@,%2,speak(if(%4,%4%b)%3,%0%2,%6\\,)),html)]
```

# CHAN_USEFIRSTMATCH

**Flag**: CHAN_USEFIRSTMATCH (any type)

Normally, when an object attempts to speak on the channel system with @chat, using an ambiguous channel name produces an error message. With this flag set, it will instead speak on the first channel whose name is a match. Other commands in the chat system are not affected by the flag.

## See Also
- [help CHAT|CHAT]
- [help @chat|@chat]
- [help @cemit|@cemit]

# @CEMIT
# @NSCEMIT
# CEMIT()
# NSCEMIT()

`@cemit[/noisy|/silent][/noeval] <channel>=<message>`
`@nscemit[/noisy|/silent][/noeval] <channel>=<message>`
`cemit(<channel>, <message>[, <noisy>])`
`nscemit(<channel>, <message>[, <noisy>])`

@cemit emits *<message>* on *<channel>*. It does not include your name. The channel prefix is included if the /noisy switch is given, and omitted if /silent is given - if neither is given, the default behaviour is controlled by the noisy_cemit @config option. The /noeval switch prevents *<message>* from being evaluated.

You must be able to speak on the channel, or have the See_All and Pemit_All @powers, to @cemit on the channel.

@nscemit is exactly the same, but does not produce nospoof information when used by players with the Can_spoof @power.

cemit() and nscemit() work the same as @cemit/silent and @nscemit/silent, respectively. If *<noisy>* is given as a true value, they work like @cemit/noisy and @nscemit/noisy, respectively, instead.

@cemit is intended for use in writing extended chat systems. 

## See Also
- [help @chat|@chat]

# @channel

The `@channel` command is used to add, join, list and modify channels in the chat system. It takes many different switches.

Help for `@channel` is split into a number of topics. Please see [help @channel <topic>|@channel] for more, where *<topic>* is one of the words below. For help on a specific switch to `@channel`, use [help @channel/<switch>|@channel].

- **Joining** - How to find, join, and leave channels
- **Other** - Setting channel titles, recalling previous chat messages
- **Admin** - Adding, deleting and modifying channels

## See Also
- [help CHAT|CHAT]
- [help @chat|@chat]
- [help @cemit|@cemit]
- [help channel functions|channel functions]

# @CHANNEL JOINING
# @channel/list
# @channel/what
# @channel/who
# @channel/on
# @channel/join
# @channel/off
# @channel/leave

`@channel/list[/on|/off][/quiet] [<prefix>]`
`@channel/what [<prefix>]`
`@channel/who <channel>`
`@channel/on <channel>[=<player>]`
`@channel/off <channel>[=<player>]`

`@channel/list` shows a list of all the channels you can see, along with some basic information such as whether you are on the channel, how it's locked, etc. [help @channel list|@channel list] explains the output in detail. If a *<prefix>* is given, only channels whose names begin with *<prefix>* are shown. If the /on switch is given, only channels you've joined are shown. If /off is given, channels you are on will not be shown. The /quiet switch shows just a list of channel names, without any extra information.

`@channel/what` shows the name, description, owner, priv flags, mogrifier and buffer size for all channels, or all channels whose names begin with *<prefix>* if one is given.

`@channel/who` lists all the players on the given channel.

`@channel/on` and `@channel/off` add or remove you from the given *<channel>*. You only hear messages for channels you're on, and most channels require you to join them before you can speak on them. /join and /leave are aliases for /on and /off.

# @CHANNEL JOINING2
# @channel/gag
# @channel/ungag
# @channel/hide
# @channel/unhide
# @channel/mute
# @channel/combine
# @channel/uncombine

`@channel/gag [<channel>][=<yes|no>]`
`@channel/mute [<channel>][=<yes|no>]`
`@channel/hide [<channel>][=<yes|no>]`
`@channel/combine [<channel>][=<yes|no>]`

`@channel/gag` allows you to stay on a channel but stop receiving messages on it. Channels are automatically ungagged when you disconnect. You cannot speak on channels you're gagging unless they have the "open" priv.

Channels without the 'quiet' priv broadcast messages when players connect or disconnect from the MUSH. You can use `@channel/mute` to suppress these messages if you don't want to see them.

On channels with the 'hide_ok' priv, `@channel/hide` lets you hide from the @channel/who list if you pass the channel's @clock/hide.

Connect and disconnect messages across all channels you have marked with `@channel/combine` will be combined into a single message with a |-separated list of all channel names. Only players can use this.

For all four of these commands, you can specify a single channel to affect, or omit *<channel>* to affect all channels you're on. To undo the gag/mute/hide, either use `@channel/<switch> [<channel>]=no` or `@channel/un<switch> [<channel>]`.

## See Also
- [help @channel/who|@channel/who]
- [help cstatus()|cstatus()]
- [help cowner()|cowner()]
- [help cflags()|cflags()]
- [help channels()|channels()]
- [help @channel/privs|@channel/privs]

# @CHANNEL OTHER
# @channel/recall
# @channel/title
# @channel/buffer

`@channel/recall[/last] <channel>[=<count>]`
`@channel/title <channel>=<title>`
`@channel/buffer <channel>=<size>`

`@channel/recall` displays the last *<count>* messages sent on *<channel>*. If *<count>* is not given, it shows the last 10. The /last switch shows messages starting from the *<count>*th most recent message.

`@channel/title` sets your title on *<channel>*. Your title appears in front of your name when you speak on the channel, if the channel is set to show titles. If *<title>* is not given, your title is cleared.

`@channel/buffer` sets the recall buffer size for *<channel>* to *<size>*. Only channel admins can do this. A size of 0 disables the recall buffer.

## See Also
- [help @channel/who|@channel/who]
- [help @channel/privs|@channel/privs]

# @CHANNEL ADMIN
# @channel/add
# @channel/delete
# @channel/mogrifier
# @channel/chown
# @channel/name
# @channel/desc
# @channel/privs
# @channel/wipe
# @channel/clock

`@channel/add <channel>[=<description>]`
`@channel/delete <channel>`
`@channel/mogrifier <channel>=<object>`
`@channel/chown <channel>=<player>`
`@channel/name <channel>=<newname>`
`@channel/desc <channel>=<description>`
`@channel/privs <channel>=<privlist>`
`@channel/wipe <channel>`
`@channel/clock[/on|/off|/clear|/add|/remove|/hide|/unhide|/list] <channel>[=<lock>]`

`@channel/add` creates a new channel. You must be able to pay the cost of the channel. The channel's description is optional.

`@channel/delete` removes a channel. Only channel admins can do this.

`@channel/mogrifier` sets an object to be the channel's mogrifier. Only channel admins can do this. See [help mogrifier|mogrifier] for details.

`@channel/chown` changes the owner of a channel. Only channel admins can do this.

`@channel/name` renames a channel. Only channel admins can do this.

`@channel/desc` changes a channel's description. Only channel admins can do this.

`@channel/privs` changes a channel's privileges. Only channel admins can do this. See [help @channel privs|@channel privs] for details.

`@channel/wipe` removes all players from a channel. Only channel admins can do this.

`@channel/clock` manages channel locks. Only channel admins can do this. See [help @channel clock|@channel clock] for details.

## See Also
- [help @channel/who|@channel/who]
- [help @channel/privs|@channel/privs]
- [help @clock|@clock]

# @CHANNEL LIST

The output of `@channel/list` looks like this:
```
Channel        Status  Lock                  Description
Public         On-     *UNLOCKED*           Public chat channel
Admin          Off     =WIZARD              Administrative discussion
```

The Status column shows whether you are on the channel (On) or not (Off), followed by any special flags:
- `-`: Channel is gagged
- `!`: Channel is hidden
- `@`: Channel is muted
- `+`: Channel is combined

The Lock column shows:
- `*UNLOCKED*` if anyone can use the channel
- `=<lock>` if there's a lock on speaking
- `J=<lock>` if there's a lock on joining
- `H=<lock>` if there's a lock on hiding
- `(DISABLED)` if the channel is disabled

## See Also
- [help @channel/who|@channel/who]
- [help @channel/privs|@channel/privs]
- [help @clock|@clock]

# @CHANNEL PRIVS

Channel privileges control what players can do with a channel. They are set with `@channel/privs <channel>=<privlist>`, where *<privlist>* is a space-separated list of privilege names, optionally prefixed with `no_` to remove the privilege.

Available privileges:
- **join**: Players must pass the join lock to join the channel
- **speak**: Players must pass the speak lock to speak on the channel
- **hide**: Players must pass the hide lock to hide on the channel
- **open**: Players can speak on the channel without joining it
- **quiet**: Don't show connect/disconnect messages
- **hide_ok**: Players can hide on the channel
- **loud**: Show channel prefix even on @cemit/silent
- **disabled**: Channel cannot be used

## Examples
```
@channel/privs Public=no_join no_speak
@channel/privs Admin=join speak no_hide_ok
```

## See Also
- [help @channel/who|@channel/who]
- [help @channel clock|@channel clock]
- [help @clock|@clock]

# @CHANNEL CLOCK
# @clock

`@channel/clock[/switch] <channel>[=<lock>]`

Channel locks control who can join, speak on, or hide on a channel. The basic switches are:
- **/on** *<lock>*: Set the speak lock
- **/off**: Remove the speak lock
- **/clear**: Remove all locks
- **/add** *<lock>*: Set the join lock
- **/remove**: Remove the join lock
- **/hide** *<lock>*: Set the hide lock
- **/unhide**: Remove the hide lock
- **/list**: Show all locks

Only channel admins can set locks. Players must pass:
- The join lock to join the channel (if the channel has the 'join' priv)
- The speak lock to speak on the channel (if the channel has the 'speak' priv)
- The hide lock to hide on the channel (if the channel has the 'hide_ok' priv)

## Examples
```
@channel/clock/on Public=WIZARD
@channel/clock/add Admin=WIZARD
@channel/clock/hide Secret=WIZARD
```

## See Also
- [help @lock|@lock]
- [help locks|locks]
- [help @channel privs|@channel privs]

# CHANNEL FUNCTIONS
# CHANNELS()
# COWNER()
# CFLAGS()
# CSTATUS()
# CEMIT()
# NSCEMIT()

`channels([<player>][,<type>])`
`cowner(<channel>)`
`cflags(<channel>[,<player>])`
`cstatus([<player>][,<channel>])`
`cemit(<channel>,<message>[,<noisy>])`
`nscemit(<channel>,<message>[,<noisy>])`

These functions provide information about channels:

- **channels()**: Lists channels visible to *<player>* (or me). If *<type>* is given, only shows channels of that type:
  - **all**: All visible channels (default)
  - **on**: Channels *<player>* is on
  - **off**: Channels *<player>* is not on
  - **quiet**: Just channel names, no extra info

- **cowner()**: Returns the dbref of *<channel>*'s owner

- **cflags()**: Returns channel flags for *<channel>*, or status flags for *<player>* on *<channel>*:
  - Channel flags: DISABLED LOUD OPEN QUIET
  - Status flags: COMBINE GAG HIDE MUTE

- **cstatus()**: Returns information about *<player>*'s channel status:
  - With no args: List of channels I'm on
  - With *<player>*: List of channels they're on
  - With *<channel>*: My status on that channel
  - With both: Their status on that channel
  Status is one of: OFF ON GAG HIDE MUTE COMBINE

- **cemit()** and **nscemit()**: Emit *<message>* on *<channel>*. See [help @cemit|@cemit].

## Examples
```
> think channels(#123,on)
Public Admin
> think cowner(Public)
#1
> think cflags(Public)
OPEN
> think cflags(Public,#123)
COMBINE
> think cstatus(#123,Public)
ON COMBINE
```

## See Also
- [help @channel|@channel]
- [help @chat|@chat]
- [help @cemit|@cemit]

# MUXCOMSYS

SharpMUSH provides some aliases for players more familiar with the MUX comsys:

- `addcom <alias>=<channel>` > `@channel/on <channel>`
- `delcom <alias>` > `@channel/off <channel>`
- `comlist` > `@channel/list`
- `comtitle <channel>=<title>` > `@channel/title <channel>=<title>`
- `<alias> <message>` > `@chat <channel>=<message>`
- `<alias>:` > `@chat <channel>=:`
- `<alias>;` > `@chat <channel>=;`

Note that SharpMUSH does not actually support channel aliases - the above commands work by looking up the real channel name. You must use enough of the channel name to uniquely identify it.

## See Also
- [help @channel|@channel]
- [help @chat|@chat]
- [help CHAN_USEFIRSTMATCH|CHAN_USEFIRSTMATCH]