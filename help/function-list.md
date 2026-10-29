<!-- help-article
{
  "corpus": "help",
  "id": "function-list",
  "lookup": "Function List",
  "aliases": ["Function Types"],
  "sections": [
    {"id": "objects", "heading": "Functions for objects and communication", "lookup": "function list objects"},
    {"id": "data", "heading": "Functions for data, text and output", "lookup": "function list data"}
  ]
}
-->
# Function List
Functions are grouped by what they work on. Each topic below covers one group, with some of its functions in brackets.

`@list/functions` lists every function on the game, and `@function` only the global functions the game added with @function.

- [function list objects]: attributes, objects, connections, channels, mail and feeds
- [function list data]: numbers, lists, strings, time, JSON, SQL, HTML and layout

## Functions for objects and communication

- [Attribute functions]: attribute-related manipulations (GET, UFUN)
- [channel functions]: get information about channels (CTITLE, CWHO)
- [Communication functions]: send messages to objects (PEMIT, OEMIT)
- [Connection functions]: get information about a player's connection (CONN)
- [Dbref functions]: return dbref info related to objects (LOC, LEXITS)
- [Feed functions]: read the feeds a game's systems run (FEEDMSG, FEEDWHO)
- [Information functions]: find out something about objects (FLAGS, MONEY)
- [Mail Functions]: manipulate @mail (MAIL, FOLDERSTATS)
- [Page functions]: read your own page log (PAGERECALL, PAGECONVERSATIONS)
- [Wiki functions]: read the shared wiki (WIKI, WIKILIST)

From plugins: [discordlink()], [scene functions], [scene type functions], [scene write functions]

## Functions for data, text and output

- [Bitwise functions]: manipulation of individual bits of numbers (SHL, BOR)
- [Boolean functions]: produce 0 or 1 (false or true) answers (OR, AND)
- [HTML FUNCTIONS]: output HTML tags for Pueblo and WebSocket clients
- [JSON FUNCTIONS]: create and manipulate JSON objects (JSON, JSON_MAP)
- [Layout functions]: boxes, tables, columns and trees the portal draws as layout (BOX, DATATABLE)
- [List functions]: manipulate lists (REVWORDS, FIRST)
- [Math functions]: number manipulation, generic or integers only (ADD, DIV)
- [MEDIA FUNCTIONS]: sounds, pictures and panes, written once for every client (SOUND, IMAGE)
- [Regular expression functions]: Regular expressions (REGMATCH, REGEDIT)
- [SQL functions]: access SQL databases (SQL, SQLESCAPE)
- [String functions]: string manipulation (ESCAPE, FLIP)
- [Time functions]: formatting and display of time (TIME, CONVSECS)
- [Utility functions]: general utilities (ISINT, COMP)
- [Vector functions]: arithmetic on lists of numbers as vectors (VADD, VMAG)
