<!-- help-article
{
  "corpus": "help",
  "id": "socket-commands",
  "lookup": "Socket Commands",
  "aliases": [],
  "sections": [
    {"id": "behavior", "heading": "How socket commands behave", "lookup": "socket commands behavior"},
    {"id": "login-screen", "heading": "Login screen commands", "lookup": "socket commands at login"}
  ]
}
-->
# Socket Commands

These commands can only be entered through a client, on the connection they are typed into. They act on that connection rather than on a game object, so they work whether or not you have connected to a character, and would be meaningless if run by an object or from a queued action.

- [IDLE]
- [INFO]
- [LOGOUT]
- [OUTPUTPREFIX]
- [OUTPUTSUFFIX]
- [PROMPT_NEWLINES]
- [QUIT]
- [SCREENREADER]
- [SCREENWIDTH]
- [SCREENHEIGHT]
- [SOCKSET]
- [MSSP-REQUEST]
- [VERSION]

## How socket commands behave

PennMUSH handles these before it decides whether a descriptor has a player behind
it, and SharpMUSH does the same, which is why they answer at the connect screen
too. Because they act on the connection that typed them, a player with two
clients open sets the screen width of one without touching the other.

VERSION is a SharpMUSH addition: PennMUSH has only `@version`. It reports the
same lines that `@version` does and is accepted at the connect screen, because
players arriving from MUX-family servers and crawler bots type it unprefixed.

LOGOUT leaves your character but keeps the connection: you return to the screen
above and can connect again, as the same character or a different one, without
reconnecting. QUIT closes the connection instead. Logging out clears the
settings you made on that connection, so the next login on it starts clean.

## Login screen commands

In addition, the following commands can only be used at the login screen:

- [cd]
- [ch]
- [cv]
- [claim]
- [connect]
- `create`
- [login]
- [make]
- [play]
- [register]

The [WHO] command can also be used at the login screen. Please note that this is different to the in-game WHO command. [DOING] and [SESSION] show that same login-screen listing when you are not connected, and their own in-game output once you are.
