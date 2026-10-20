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
  Several major variants of functions are available. The help topics are listed below, together with a quick summary of the function type and some examples of that type of function.

  The command "@list/functions" lists all functions on the game.<br>
  The command "@function" lists only the game's custom global functions defined via the @function command.

- [function list objects]: attributes, objects, connections, channels, mail and feeds
- [function list data]: numbers, lists, strings, time, JSON, SQL, HTML and layout

## Functions for objects and communication

  [Attribute functions]: attribute-related manipulations (GET, UFUN) <br>
  [channel functions]: get information about channels (CTITLE, CWHO) <br>
  [Communication functions]: send messages to objects (PEMIT, OEMIT) <br>
  [Connection functions]: get information about a player's connection (CONN) <br>
  [Dbref functions]: return dbref info related to objects (LOC, LEXITS) <br>
  [Feed functions]: read the feeds a game's systems run (FEEDMSG, FEEDWHO) <br>
  [Information functions]: find out something about objects (FLAGS, MONEY) <br>
  [Mail Functions]: manipulate @mail (MAIL, FOLDERSTATS) <br>
  [Page functions]: read your own page log (PAGERECALL, PAGECONVERSATIONS) <br>
  [Wiki functions]: read the shared wiki (WIKI, WIKILIST)

From plugins: [scene functions], [scene type functions], [scene write functions]

## Functions for data, text and output

  [Bitwise functions]: manipulation of individual bits of numbers (SHL, BOR) <br>
  [Boolean functions]: produce 0 or 1 (false or true) answers (OR, AND) <br>
  [HTML FUNCTIONS]: output HTML tags for Pueblo and WebSocket clients <br>
  [JSON FUNCTIONS]: create and manipulate JSON objects (JSON, JSON_MAP) <br>
  [Layout functions]: boxes, tables, columns and trees the portal draws as layout (BOX, DATATABLE) <br>
  [List functions]: manipulate lists (REVWORDS, FIRST) <br>
  [Math functions]: number manipulation, generic or integers only (ADD, DIV) <br>
  [MEDIA FUNCTIONS]: sounds, pictures and panes, written once for every client (SOUND, IMAGE) <br>
  [Regular expression functions]: Regular expressions (REGMATCH, REGEDIT) <br>
  [SQL functions]: access SQL databases (SQL, SQLESCAPE) <br>
  [String functions]: string manipulation (ESCAPE, FLIP) <br>
  [Time functions]: formatting and display of time (TIME, CONVSECS) <br>
  [Utility functions]: general utilities (ISINT, COMP) <br>
  [Vector functions]: arithmetic on lists of numbers as vectors (VADD, VMAG)
