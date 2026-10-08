# messages

The Messages object: one attribute for each message the server shows a connection at a fixed point.

| Attribute | Shown |
|-----------|-------|
| `CONNECT` | at the connect screen |
| `MOTD` | to every player after logging in |
| `WIZMOTD` | after the MOTD, to wizards and royalty |
| `GUEST` | after the MOTD, to a guest |
| `NEWUSER` | after `register` creates an account |
| `REGISTER` | instead of creating a character while player creation is off |
| `DOWN` | before the refusal while logins are off |
| `QUIT` | after the goodbye on `QUIT` and `LOGOUT` |

Each attribute is evaluated as the object when its message is shown: `%#` is the player it is shown to (the
object itself at the connect screen) and `%0` is the connection's descriptor. An attribute that evaluates to
nothing shows nothing; a message the object has no attribute for shows its stored text instead.

Installing the package sets the `messages_object` option to this object, which switches the game to reading its
messages from it; removing the package puts the option back as it was, unless it has been changed since.
**Admin > Messages** in the portal switches back to the stored text, and edits that text, without removing the
package.

`CONNECT` puts the logo beside the ways in, with the layout functions: a `flex()` of the logo as a `figure()`
(the picture `/assets/logo.png` in the web portal, `LOGO` in the logo's green in a terminal) and the commands,
each section a `rule()` over a `fields()` list. On a screen too narrow for both, the commands go under the logo. A
section is left out while the option behind it (`player_creation`, `guests`) is off. `FUN`MENU`, `FUN`SECTION`,
`FUN`LIST` and `FUN`COMMAND` build it.

The other messages greet the player by name (`MOTD`), or use `notice()` (`WIZMOTD`, `GUEST`, `REGISTER`, `DOWN`).
`connect guest`, `WHO`, `QUIT` and `help` are command links. The object holds the `Send_OOB` power for the links
and the picture.
