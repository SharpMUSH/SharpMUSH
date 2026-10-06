<!-- help-article
{
  "corpus": "help",
  "id": "channel-joining-command",
  "lookup": "@channel joining",
  "aliases": [
    "@channel/list",
    "@CLIST",
    "@channel/what",
    "@channel/who",
    "@channel/on",
    "@channel/join",
    "@channel/off",
    "@channel/leave"
  ],
  "sections": [
    {
      "id": "personal-channel-settings",
      "heading": "Personal channel settings",
      "lookup": "@channel joining personal channel settings",
      "aliases": [
        "@channel/gag",
        "@channel/ungag",
        "@channel/hide",
        "@channel/unhide",
        "@channel/mute",
        "@channel/combine",
        "@channel/uncombine"
      ]
    }
  ],
  "redirects": {
    "@CHANNEL JOINING2": "@channel joining personal channel settings"
  }
}
-->
# @channel joining

- `@channel/list[/on|/off][/quiet] [<prefix>]`
- `@channel/what [<prefix>]`
- `@channel/who <channel>`
- `@channel/on <channel>[=<player>]`
- `@channel/off <channel>[=<player>]`

`@channel/list` shows a list of all the channels you can see, along with some basic information such as whether you are on the channel, how it's locked, etc. [@CHANNEL LIST] explains the output in detail. If a *\<prefix\>* is given, only channels whose names begin with *\<prefix\>* are shown. If the /on switch is given, only channels you've joined are shown. If /off is given, channels you are on will not be shown. The /quiet switch shows just a list of channel names, without any extra information.

`@channel/what` shows the name, description, owner, priv flags, mogrifier and buffer size for all channels, or all channels whose names begin with *\<prefix\>* if one is given. Channels you may decompile also show their locks.

`@channel/who` lists the members of the given channel: connected players and things. Members hiding on the channel with `@channel/hide` are omitted unless you have the `Who` power. A member's own hidden or gagging state is noted beside their name. A channel does not announce its members connecting and disconnecting unless it has the `announce` privilege ([@CHANNEL PRIVS]), so this is how to see who is on it; the web portal's channel view keeps the same list beside the conversation.

`@channel/on` and `@channel/off` add or remove you from the given *\<channel\>*. You only hear messages for channels you're on, and most channels require you to join them before you can speak on them. /join and /leave are aliases for /on and /off.

## Personal channel settings

- `@channel/gag [<channel>][=<yes|no>]`
- `@channel/mute [<channel>][=<yes|no>]`
- `@channel/hide [<channel>][=<yes|no>]`
- `@channel/combine [<channel>][=<yes|no>]`
- `@channel/ungag [<channel>]`
- `@channel/unmute [<channel>]`
- `@channel/unhide [<channel>]`
- `@channel/uncombine [<channel>]`

`@channel/gag` allows you to stay on a channel but stop receiving messages on it. Channels are automatically ungagged when you disconnect. You cannot speak on channels you're gagging unless they have the "open" priv.

Channels without the 'quiet' priv broadcast messages when players connect or disconnect from the MUSH. You can use `@channel/mute` to suppress these messages if you don't want to see them.

On channels with the 'hide_ok' priv, `@channel/hide` lets you hide from the @channel/who list if you pass the channel's @clock/hide.

Connect and disconnect messages across all channels you have marked with `@channel/combine` will be combined into a single message with a |-separated list of all channel names. Only players can use this.

For all four of these commands, you can specify a single channel to affect, or omit *\<channel\>* to affect every channel you are on. Naming a channel resolves against the channels you are on, so an abbreviation cannot be made ambiguous by a channel you never joined; the argument-less form reports what it did in one line and names no channel. To undo the gag/mute/hide, either use `@channel/<switch> [<channel>]=no` or `@channel/un<switch> [<channel>]`.

These are all your OWN settings on a channel. There is no command for muting or gagging somebody else; to stop a member speaking, lock the channel with `@clock/speak` (see [@CHANNEL CLOCK]).

::: seealso
- [@channel joining]
- [cstatus()]
- [cowner()]
- [cflags()]
- [channels()]
- [@CHANNEL ADMIN]
:::
