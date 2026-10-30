<!-- help-article
{
  "corpus": "help",
  "id": "chatformat-command",
  "lookup": "@chatformat",
  "aliases": [],
  "sections": [
    {
      "id": "registers",
      "heading": "Registers",
      "lookup": "@chatformat registers"
    },
    {
      "id": "wrapping-channel-messages",
      "heading": "Wrapping channel messages",
      "lookup": "@chatformat wrapping channel messages"
    },
    {
      "id": "channel-color-themes",
      "heading": "Channel color themes",
      "lookup": "@chatformat channel color themes"
    }
  ],
  "redirects": {
    "@CHATFORMAT2": "@chatformat wrapping channel messages",
    "@CHATFORMAT3": "@chatformat channel color themes"
  }
}
-->
# @chatformat

`@chatformat <object>[=<message>]`

The chatformat attribute is evaluated when an object receives a channel message. If the attribute exists, its evaluated result is shown to the object instead of the default message. If the attribute exists but returns nothing, the object will not see anything.

```sharp
@chatformat me=<%<channel>> %<name>: %<message>
```
Output: when Walker says Hello. on Public, you see `<Public> Walker: Hello.`

::: seealso
- [@chat]
- [@pageformat]
- [@message]
- [speak()]
- [@CHANNEL ADMIN]
:::

## Registers

- **%0** or `%<type>`: The 'type' of the message. It is a single character that will always be set:
- `"`, `;` or `:` for say, semipose and pose, respectively
- `|` for an @cemit
- `@` for a "system" message - such as "Walker has connected."
- **%1** or `%<channel>`: The channel name. e.g: "Public", "Admin", "Softcode"
- **%2** or `%<message>`: The message as typed (post-evaluation, if necessary) by the speaker. Be warned, though - if type is '@', then %2 will contain the entire message, and will include the name of the speaker that caused it
- **%3** or `%<name>`: The speaker name, unless channel is set NO_NAME
- **%4** or `%<title>`: The speaker's channel title, unless none is set, or the channel is NO_TITLE
- **%5** or `%<default>`: The default message, as shown when no chatformat is set
- **%6** or `%<says>`: The 'say' string for the message. This will usually be "says", unless altered by the SPEECHTEXT mogrifier
- **%7** or `%<options>`: A space-separated list of extra options. Currently can contain: "silent" if a silent @cemit caused this message, otherwise "noisy"

If the channel is NO_NAME, and the speaker either has no title or the channel is also set NO_TITLE, then %3 will be "Someone".

Each value is passed by position and by the name shown (see [named arguments]), so `%<message>` is %2. Code written for PennMUSH reads the positions.

## Wrapping channel messages

**Examples**

Walker's preferred @chatformat, which strips all ansi out, wraps every line to your width and prefixes them with \<ChannelName\>:

```sharp
@chatformat me=<%1> [switch(%0,@,%2,edit(wrap(speak(&[if(%4,%4%b)]%3,%0[stripansi(%2)],%6\\,),sub(width(%!),add(4,strlen(%1)))),%r,%r<%1>%b))]
```

If you're on a system with chat_strip_quote set to "no", you might want to change the '%0%2' arg to speak() to '`[switch(%0,\",%2,%0%2)]`'

Suppose you want it just like that, but anytime somebody says your name, you want it all in red:

```sharp
@chatformat me=ansi(switch(%2,*[name(%!)]*,r,n),%5)
```

## Channel color themes

Many clients colour channels by name, and @chatformat can do it in the game. Let's suppose you want "Public" channel chatter to all be green, "Softcode" to be blue and "Admin" to be cyan.

```sharp
@chatformat me=ansi(switch(%1,Public,g,Softcode,b,Admin,c,n),%5)
```

Maybe you dislike players who re-@name themselves a lot:

```sharp
&playernames me=#6061:Walker #7:Javelin #6388:Cheetah
@chatformat me=<%1> [switch(%0,@,%2,speak(&[if(%4,%4%b)][firstof(after(grab(v(playernames),%#:*),:),%3)],%2,%6\\,))]
```

Or you're writing a loggerbot, and you want to convert all channel input to HTML:

```sharp
@chatformat me=CHAT:%1:[edit(switch(%0,@,%2,speak(if(%4,%4%b)%3,%0%2,%6\\,)),&,&amp;,<,&lt;,>,&gt;,%r,<BR>,%b%b,%b&nbsp;)]
```
or
```sharp
@chatformat me=CHAT:%1:[render(switch(%0,@,%2,speak(if(%4,%4%b)%3,%0%2,%6\\,)),html)]
```
