<!-- help-article
{
  "corpus": "help",
  "id": "motd-command",
  "lookup": "@motd",
  "aliases": ["@listmotd", "@wizmotd", "@rejectmotd"],
  "sections": [
    {"id": "types", "heading": "Types of MotD", "lookup": "@motd types", "aliases": ["@motd/list"]}
  ]
}
-->
# @motd

`@motd[/<type>] <message>`<br>
`@motd/clear[/<type>]`<br>
`@motd/list`

This command is used for manipulating the various Messages of the Day, or MotD. The first form of this command sets the `<type>` MotD to `<message>`, the second form clears the `<type>` MotD, and the third form lists the current value of each MotD. If no switch is given, `<type>` defaults to /connect.

You must have the Announce @power or the `chat.admin` permission to change the Connect MotD; `@motd/list` and the other types need `chat.admin`.

@wizmotd and @rejectmotd work like `@motd/wizard` and `@motd/full`. @listmotd is open to everyone: it shows the Connect MotD (and, with `chat.admin`, the others) and where the game reads its messages from.

- [@motd types]: who sees each MotD, how long it lasts, and the list

## Types of MotD

These messages are intended for temporary announcements. A /connect or /wizard MotD is shown in place of the MOTD or wizard MOTD kept on the Messages page (see [messages]) while it is set; a /down MotD is shown after the logins-disabled message. MotDs set via this command are kept across restarts until cleared.

| Valid `<type>` | and is seen by... |
| --- | --- |
| /connect | all players on connect |
| /wizard | connecting Wizards and Royalty |
| /full | players failing to connect because all available connections are in use |
| /down | mortals failing to connect when logins are disabled |

@listmotd also says where the game reads its messages from: the stored texts or the Messages object.
