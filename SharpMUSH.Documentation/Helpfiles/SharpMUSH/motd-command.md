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

You must have the Announce @power to change the Connect MotD; only wizards and royalty can see or alter the others.

For historical reasons, @listmotd, @wizmotd and @rejectmotd are aliases for `@motd/list`, `@motd/wizard` and `@motd/full`, respectively.

- [@motd types]: who sees each MotD, how long it lasts, and the list

## Types of MotD

These messages are intended for temporary announcements. A /connect or /wizard MotD is shown in place of the MOTD or wizard MOTD kept on the Messages page (see [messages]) while it is set; a /down MotD is shown after the logins-disabled message. MotDs set via this command are cleared when the MUSH restarts.

| Valid `<type>` | and is seen by... |
| --- | --- |
| /connect | all players on connect |
| /wizard | connecting Wizards and Royalty |
| /full | players failing to connect because all available connections are in use |
| /down | mortals failing to connect when logins are disabled |

`@motd/list` also says where the game reads its messages from: the stored texts or the Messages object.
