<!-- help-article
{
  "corpus": "help",
  "id": "messages",
  "lookup": "messages",
  "aliases": [
    "CONNECT SCREEN"
  ],
  "sections": [
    {
      "id": "messages-object",
      "heading": "The Messages object",
      "lookup": "messages object"
    }
  ]
}
-->
# messages

The server shows a connection a few fixed texts: the connect screen, the message of the day and the rest. PennMUSH reads these from files named in `mush.cnf`; SharpMUSH keeps them in the database and edits them on the web portal's Messages page (Admin > Content > Messages), which needs the `config.admin` permission. Text is edited as it will look, in a monospaced field, and stored with its colours.

| Message | Attribute | Shown |
| --- | --- | --- |
| Connect screen | `CONNECT` | to a connection arriving at the login prompt |
| Message of the day | `MOTD` | to every player after logging in |
| Wizard message of the day | `WIZMOTD` | after the MOTD, to wizards and royalty |
| Guest message | `GUEST` | to a guest, after the MOTD |
| New account | `NEWUSER` | after `register` creates an account |
| Registration closed | `REGISTER` | instead of `create` or `register` while player creation is off |
| Logins disabled | `DOWN` | before the refusal while logins are off |
| Goodbye | `QUIT` | after `QUIT` and `LOGOUT` |

An emptied text shows nothing. Reset puts back the text SharpMUSH ships. A message set with [@motd] is shown in place of the MOTD or wizard MOTD while it is set.

## The Messages object

The bundled `messages` package creates a thing named Messages that holds each message in the attribute named above. The setup wizard offers it, and it can be installed later from the Packages page. While it is installed, the game reads each message from that object, evaluated as softcode; a message the object has no attribute for falls back to the stored text, and an attribute that evaluates to nothing shows nothing. The Messages page can choose either source regardless of the package.

The object's attributes are evaluated with the object as executor. `%#` is the player who will see the message (the object itself at the login screen, where there is none), and `%0` is the connection's descriptor. The shipped `CONNECT` draws the logo from `LOGO` and the text beside it from `TEXT`, one line of each per row.

```sharp
> &MOTD Messages=%rWelcome back, [name(%#)]. [words(lwho())] connected.
```

::: seealso
- [@motd]
- [@listmotd]
- [@package]
:::
