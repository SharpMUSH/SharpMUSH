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

Installing the package switches the game to reading its messages from this object. **Admin > Messages** in the
portal switches back to the stored text, and edits that text, without removing the package.

`CONNECT` lays `LOGO` beside `TEXT` line by line, in the logo's green. Both are read with `v()`, unevaluated, so
their spacing and brackets reach the screen as written.
