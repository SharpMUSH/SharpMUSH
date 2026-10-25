<!-- help-article
{
  "corpus": "help",
  "id": "socket-events",
  "lookup": "Socket Events",
  "aliases": ["Event Socket"],
  "sections": [
    {"id": "client-reports", "heading": "Client reports", "lookup": "socket events client reports"}
  ]
}
-->
# Socket Events

- **socket\`connect** (*descriptor*, *ip*)
- Triggered when a socket first connects to the port. Using both this and player\`connect could be spammy. This happens when a connecting socket sees the connect screen.

- **socket\`disconnect** (*descriptor*, *ip*, *cause*, *received/sent/commands*)
- Triggered when a socket disconnects. Using this and player\`disconnect could be spammy.

- **socket\`loginfail** (*descriptor*, *ip*, *count*, *reason*, *objid*, *name*)
- Triggered when a login attempt fails. *<count>* is the number of fails in the past 10 minutes. If used in conjunction with the config option connect_fail_limit, then any failures after the limit is reached will **NOT** trigger socket\`loginfail. If the connect is a failed attempt to log into a valid player, *<objid>* will be set to that objid. Otherwise it will be set to #-1. *<name>* is the name the connection attempted to connect with, and is only set when *<objid>* is #-1.

- **socket\`createfail** (*descriptor*, *ip*, *count*, *reason*, *name*, *error*)
- Triggered when the connect screen refuses to create an account (`register`) or a character (`create`). *<reason>* is "creation disabled" when player_creation is off, "name taken" or "bad name" for a character name, or the account service's own message for a refused account. *<name>* is the name asked for, empty when creation is disabled. *<count>* is always 1, and *<error>* always empty: SharpMUSH sends no registration mail.

## Client reports

- **socket\`gmcp** (*descriptor*, *package*, *message*, *player*)
- Stoppable. Triggered when a telnet client sends a GMCP package, such as Mudlet's External.Discord.Hello when it connects. *<message>* is the package's JSON, empty when it sent none. *<player>* is whoever is logged in on the descriptor, empty at the connect screen; %# is that player too, or #1 before login. Answer with oob() on the descriptor, which reaches the client before anyone logs in. Core.Ping and Core.KeepAlive are answered by the server and do not trigger this. The client chooses everything it sends, so check *<package>* before acting on *<message>*. The modules a client lists in Core.Supports are visible to softcode as the connection's GMCP_SUPPORTS, through gmcpsupports(). That list is recorded apart from this event, so the event for a Core.Supports package may still see the list from before it; read the list in a later event, such as player\`connect.

- **socket\`timezone** (*descriptor*, *zone*, *player*)
- Triggered when a client reports its time zone, such as Europe/Paris: the web portal reports the browser's, and a telnet client its TZ through NEW-ENVIRON. *<zone>* is always a zone the server knows; anything else a client sends is dropped. It is raised once per zone a connection reports, usually before login, when *<player>* is empty and %# is #1; afterwards *<player>* and %# are whoever is logged in on the descriptor. clientzone() reads the zone at any time, so a handler that acts at login reads it from player\`connect's *<descriptor>* instead. The zone is only what the client said, and says roughly where someone is.
