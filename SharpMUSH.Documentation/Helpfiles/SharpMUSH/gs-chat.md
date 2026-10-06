<!-- help-article
{
  "corpus": "help",
  "id": "gs-chat",
  "lookup": "gs chat",
  "aliases": [
    "GS CHANNELS"
  ],
  "sections": [
    {
      "id": "speaking-on-channels",
      "heading": "Speaking on channels",
      "lookup": "gs chat speaking on channels"
    }
  ],
  "redirects": {
    "GS CHAT2": "gs chat speaking on channels"
  }
}
-->
# gs chat

SharpMUSH has a built-in channel system, which allows you to talk with players who are on the same channels as you, even if you're in different rooms.

Most games have a number of different channels, either for discussing different subjects, or for different groups/factions of players to chat on.

To see a list of channels, type '`@channel`/list'. This shows the names of all the channels, whether or not you're on the channel, and some other information (the rest is explained in [@CHANNEL LIST]).

To join a channel, type '`@channel`/on `<channel>`'. To leave it again, use '`@channel`/leave `<channel>`'. If you want to stop hearing a channel for a while without leaving it totally, use '`@channel`/gag `<channel>`'.

## Speaking on channels

When you've joined a channel, you can chat on it in two ways:

    +`<channel>` `<message>`<br>
    `@chat` `<channel>`=`<message>`

You don't need to type the entire channel name, just enough letters to make it distinct from other channels. For instance, '`+pub` Hello'.

When you talk on a channel, everyone who is on the channel will see the channel name in '<>' angle brackets, then your name and the message.<br>
For example, '`+pub` Hello' will show everyone<br>
    `<Public>` Skye says, "Hello"<br>
If the `<message>` starts with a ':' or ';' it will be posed or semiposed, respectively. For example, '`+pub` :waves' shows<br>
    `<Public>` Skye waves.

Some games customize the appearance of channels a little (for instance, adding color or using '[]' square brackets instead of angle brackets), so it may look a little different.

There's much more you can do with the channel system - see [@channel] for the other commands, and [@channel joining] for joining and leaving.

::: seealso
- [gs talking]
- [@channel]
- [@chat]
:::
