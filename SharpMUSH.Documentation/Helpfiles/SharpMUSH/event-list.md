<!-- help-article
{
  "corpus": "help",
  "id": "event-list",
  "lookup": "Event List",
  "aliases": [],
  "sections": [
    {"id": "server", "heading": "Server events", "lookup": "event list server"},
    {"id": "players", "heading": "Player and communication events", "lookup": "event list players"},
    {"id": "arguments", "heading": "Arguments", "lookup": "event list arguments"},
    {"id": "sharpmush-only", "heading": "SharpMUSH-only events", "lookup": "event list sharpmush only"}
  ]
}
-->
# Event List

Event names are of the format *<type>\`<event>*. The 'type' is used simply to group similar events together for help.

An event marked **Stoppable** runs its attributes in turn and lets one stop the rest with [stopevent()] (see [events]).

Event syntax in the help is of the form:<br>
*<eventgroup>\`<eventname>* (*<first>*, *<second>*, ...)

These are the events a SharpMUSH game raises. To see the help for a type, type `help event <type>`.

- [event list server]: events of the server and its database
- [event list players]: events of players, connections and talk
- [event list arguments]: how a handler reads the arguments
- [event list sharpmush only]: the events PennMUSH does not have

## Server events

- [event dump] - dump\`5min, dump\`1min, dump\`complete, dump\`error
- [event db] - db\`dbck, db\`purge, db\`wcheck
- [event log] - log\`err, log\`cmd, log\`wiz, log\`conn, log\`trace, log\`check, log\`huh
- [event object] - object\`create, object\`destroy, object\`move, object\`rename, object\`flag
- [event sql] - sql\`connect, sql\`connectfail, sql\`disconnect
- [event signal] - signal\`usr1, signal\`usr2
- [event backup] - backup\`complete, backup\`error

## Player and communication events

- [event player] - player\`create, player\`connect, player\`disconnect, player\`inactivity, player\`channels, player\`warn, player\`link, player\`unlink
- [event socket] - socket\`connect, socket\`disconnect, socket\`loginfail, socket\`createfail, socket\`gmcp, socket\`timezone
- [event http] - http\`blocked, http\`fail, http\`command
- [event room] - room\`contents
- [event channel] - channel\`message, channel\`who
- [event page] - page\`message
- [event mail] - mail\`received
- [event scene] - scene\`status
- [event gallery] - gallery\`add, gallery\`report

## Arguments

The handler gets each argument by position, the first as %0, and by the name shown, as `%<name>` (see [named arguments]). `&OBJECT\`MOVE #9=...` can read the destination as %1 or as `%<destination>`. A name of the form *received/sent/commands* names the parts of a value made of numbers joined by `/`: `%<received>`, `%<sent>` and `%<commands>` are each one number, and the position holds the whole value, as in PennMUSH.

## SharpMUSH-only events

The room, channel, page, mail, scene, backup and gallery events, player\`channels, player\`warn, player\`link, player\`unlink, socket\`gmcp and socket\`timezone are SharpMUSH's own; PennMUSH has no equivalent. The room, channel and page events exist so a handler can send a web client structured updates (the bundled room-contents and comm-feed packages), and each names exactly who the update concerns, so a handler never has to work out again who could see what. player\`warn is how a warning from the web portal reaches the game, socket\`gmcp hands softcode what a client sends over GMCP, socket\`timezone the time zone a client reports, the gallery events tell the game about the web portal's profile pictures, and scene\`status lets a package act when a scene starts or ends.

An argument of a SharpMUSH event that holds text a player wrote, such as a channel line or a page, keeps its colour: `%<message>` in a page\`message handler is the page as the recipients saw it. Use stripansi() for the plain text. PennMUSH's events pass plain text, as PennMUSH does.
