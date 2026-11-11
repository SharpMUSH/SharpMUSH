<!-- help-article
{
  "corpus": "help",
  "id": "rhost-mail-functions",
  "lookup": "rhostmush mail functions",
  "aliases": [],
  "sections": [
    {
      "id": "mailquick",
      "heading": "mailquick()",
      "lookup": "mailquick()",
      "aliases": []
    },
    {
      "id": "mailquota",
      "heading": "mailquota()",
      "lookup": "mailquota()",
      "aliases": [
        "msizetot()"
      ]
    },
    {
      "id": "mailread",
      "heading": "mailread()",
      "lookup": "mailread()",
      "aliases": []
    },
    {
      "id": "foldercurrent",
      "heading": "foldercurrent()",
      "lookup": "foldercurrent()",
      "aliases": [
        "folderlist()"
      ]
    }
  ]
}
-->
# RhostMUSH Mail Functions

Mail functions from RhostMUSH. Only wizards may use them, and only on players they control. They call the inbox `Incoming`, as RhostMUSH does. A message is new while unread, old once read, and marked once cleared. SharpMUSH has no saved messages, so the saved counts are 0.

In the examples, Bob's inbox holds three messages from Alice (one read, one unread, one cleared), and a fourth is filed in Bob's Work folder.

## mailquick()

`mailquick(<player>[, <folder>[, <type>]])`

Counts of the messages in *\<folder\>*, the current folder when it is empty. Type 0, the default, gives total, new, unread, old, marked and saved. Type 1 gives old, new and marked, with the marked among the old; type 2 the total; type 3 old, new and marked, with the marked apart. A folder that does not exist counts nothing. For a player you do not control, every count is -1.

```sharp
> think mailquick(*Bob)
3 1 0 1 1 0
```

## mailquota()

`mailquota(<player>[, <type>])`<br>
`msizetot()`

- **mailquota()**: messages held and the most the player may hold (MAILQUOTA or mail_limit), messages saved and the most saved (0 and 0), messages sent and the most sent (-1, no limit). Types 1, 2 and 3 give one pair each. A player not found or not controlled gives -1 throughout.
- **msizetot()**: the length of every subject and message on the game, added up.

```sharp
> think mailquota(*Alice,3)
4 -1
```

## mailread()

`mailread(<player>, <message>, <type>[, <mark>])`

One part of a message in the player's current folder. *\<message\>* is its number, or `new`, `unread` or `both` for the first unread one. Types: `B` text, `I` number, `S` subject, `F` sender's name, `T` recipient's name, `D` date, `C` 1 if the sender is connected, `G` NEW, OLD or MARKED, `?` FORWARD or nothing, `K` size, `U` nothing. Another type lists the letters. A true *\<mark\>* marks the message read and gives nothing.

```sharp
> think [mailread(*Bob,new,I)] [mailread(*Bob,1,G)]
2 OLD
```

## foldercurrent()

`foldercurrent(<player>[, <shared>])`<br>
`folderlist(<player>)`

- **foldercurrent()**: the player's current folder. RhostMUSH's shared folders have no counterpart, so a true *\<shared\>* gives nothing.
- **folderlist()**: the player's folders, `Incoming` first.

```sharp
> think [foldercurrent(*Bob)]|[folderlist(*Bob)]
Incoming|Incoming Work
```

::: seealso
- [Mail Functions]
- [@mail]
:::
