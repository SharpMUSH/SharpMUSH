<!-- help-article
{
  "corpus": "help",
  "id": "mux-mail-functions",
  "lookup": "tinymux mail functions",
  "aliases": [],
  "sections": [
    {
      "id": "mailcount",
      "heading": "mailcount()",
      "lookup": "mailcount()",
      "aliases": [
        "mailsize()"
      ]
    },
    {
      "id": "mailflags",
      "heading": "mailflags()",
      "lookup": "mailflags()",
      "aliases": []
    },
    {
      "id": "mailinfo",
      "heading": "mailinfo()",
      "lookup": "mailinfo()",
      "aliases": []
    },
    {
      "id": "mailreview",
      "heading": "mailreview()",
      "lookup": "mailreview()",
      "aliases": []
    }
  ]
}
-->
# TinyMUX Mail Functions

Mail functions from TinyMUX. Each reads your own mailbox, or the mailbox of a player you control. A player who is not there answers `#-1 NO SUCH PLAYER`. A message number counts from 1 in the mailbox's current folder, and a number with no message answers `#-1 NO SUCH MESSAGE`.

In the examples, Alice has sent Bob four messages, and Bob has filed one in another folder. Bob asks, except where Alice does.

## mailcount()

`mailcount([<player>])`<br>
`mailsize(<player>)`

- **mailcount()**: how many messages are in the inbox (folder 0), cleared ones included.
- **mailsize()**: the length of every message in the mailbox added up, plus 1 for each message.

```sharp
> think [mailcount()] [mailsize(me)]
3 93
```

## mailflags()

`mailflags(<message>[, <player>])`

The message's flags, as letters: `R`ead, `U`rgent, `C`leared, `T`agged and `F`orwarded. An unread message with no flags gives nothing.

```sharp
> think mailflags(2)
UT
```

## mailinfo()

`mailinfo(<message>, <field>[, <player>])`

One field of the message: `from` (the sender's dbref, `#-1` when anonymous), `to` (the mailbox's player), `tolist` (the same player, as a number), `subject`, `time`, `body`, `size`, `flags` (as mailflags() gives them) or `read` (1 or 0). Another field answers `#-1 INVALID FIELD`.

```sharp
> think mailinfo(1,subject)
Meeting
```

## mailreview()

`mailreview(<player>[, <message>])`

How many of the messages you sent are in *\<player\>*'s mailbox, or the text of the *\<message\>*th of them. You may ask about your own mail to anyone. Alice asks:

```sharp
> think mailreview(*Bob)
4
```

::: seealso
- [Mail Functions]
- [mail()]
:::
