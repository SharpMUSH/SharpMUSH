<!-- help-article
{
  "corpus": "help",
  "id": "newbie",
  "lookup": "newbie",
  "aliases": [],
  "sections": [
    {
      "id": "reading-command-syntax",
      "heading": "Reading command syntax",
      "lookup": "newbie reading command syntax"
    },
    {
      "id": "finding-related-help",
      "heading": "Finding related help",
      "lookup": "newbie finding related help"
    }
  ],
  "redirects": {
    "newbie2": "newbie reading command syntax",
    "newbie3": "newbie finding related help"
  }
}
-->
# newbie

If you are new to MUSHing, the help files may seem confusing. Most of them are written in a specific style, however, and once you understand it the files are extremely helpful.

The first line of a help file on a command or function will normally be the syntax of the command. "Syntax" means the way the command needs to be typed in. In the help files, when the syntax of a command is described, square brackets [] mean that that part of the command is optional and doesn't have to be typed in. Also, pointy brackets <> mean that that part of the command needs to be replaced with a specific piece of information.

You should not type the [] or <> brackets when entering a command.

## Reading command syntax

For example, the syntax of the help command is:

`help [<topic>]`

What this means is that to get help, you would type first the word "help" and then you could optionally type the name of a more specific topic in order to get help on that topic. Just typing "help" will work too (that's why the `<topic>` part is optional).

Some common commands that you should look at help for are [look], [say], [go], [page], [pose], [get], [give] and [home].

Just type help `<command>` for help. Example: help page

## Finding related help

There is help available on every standard MUSH command. If you see a command or someone mentions one to you that you want to know more about, try just typing: help `<command name>` -- that will most likely bring up the help file on it. If you don't know the name, search the help text with help [help search].

The full lists are in [COMMANDS], [FUNCTION LIST] and [topics].

Please note that just because there is help available on a command does not necessarily mean that the command can be used on this MUSH. The siteadmin of the MUSH can choose to turn off some commands. If there's something that you would like available, and it isn't, please ask a wizard why not.

::: seealso
- [Getting Started]
- [help search]
- [COMMANDS]
- [topics]
- [sharpmush features]
:::
