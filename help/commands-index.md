<!-- help-article
{
  "corpus": "help",
  "id": "commands-index",
  "lookup": "Commands",
  "aliases": [],
  "sections": [
    {"id": "at-commands", "heading": "@-commands", "lookup": "commands with @"},
    {"id": "login", "heading": "At the login screen and on a connection", "lookup": "commands at login"},
    {"id": "game", "heading": "Commands the game runs", "lookup": "commands the game runs"}
  ]
}
-->
# Commands

Help is available for the following MUSH commands:

|             |             |             |             |             |
|-------------|-------------|-------------|-------------|-------------|
| [ahelp]     | [anews]     | [brief]     | [buy]       | [desert]    |
| [dismiss]   | [DOING]     | [drop]      | [empty]     | [enter]     |
| [events]    | [examine]   | [follow]    | [get]       | [give]      |
| [go]        | [help]      | [home]      | [inventory] | [leave]     |
| [LOGOUT]    | [look]      | [move]      | [news]      | [page]      |
| [pose]      | [QUIT]      | [read]      | [say]       | [score]     |
| [teach]     | [think]     | [unfollow]  | [use]       | [whisper]   |
| [WHO]       | [with]      |             |             |             |
| ["]         | [:]         | [;]         | [+]         | [&]         |
| [~]         | [}]         |             |             |             |

The `]` prefix runs a command without evaluating its arguments; see [literal command].

- [commands with @]: the @-commands, which have permanent effects
- [commands at login]: login-screen, account, connection and MUX channel commands
- [commands the game runs]: HUH_COMMAND, WARN_ON_MISSING and unimplemented commands

From plugins: [ooc]

## @-commands

In addition to these, there are several types of '@' commands. @-commands are usually commands which have permanent effects on the MUSH (such as creating a new object). Here are the help topics on @-commands:

- [@-ATTRIBUTES]
- [@-BUILDING]
- [@-GENERAL]
- [@-WIZARD]

## At the login screen and on a connection

At the login screen, [connect] reaches a character. On a game with web-portal accounts, [register] and [login] reach your account, [make] and [play] create and connect its characters, and [claim] adds a character you already have. See [accounts].

Commands that act on your connection rather than your character are listed in [socket commands]. Players used to MUX can also use its channel commands; see [MUX Comsys].

## Commands the game runs

The game runs two commands itself, and either can be @hooked: [HUH_COMMAND] for a line no command matches, and [WARN_ON_MISSING] for a line that starts with a function. [UNIMPLEMENTED_COMMAND] says a command has not been implemented.
