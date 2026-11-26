<!-- help-article
{
  "corpus": "help",
  "id": "oob-function",
  "lookup": "oob()",
  "aliases": ["GMCP"],
  "sections": [
    {"id": "supported", "heading": "Sending only supported packages", "lookup": "oob supported"},
    {"id": "engine", "heading": "The server's own packages", "lookup": "oob engine"},
    {"id": "examples", "heading": "Examples", "lookup": "oob examples"}
  ]
}
-->
# oob()
`oob(<players>, <package>[, <message>[, supported]])`

  This function sends an out-of-band message using the General MUD Communication Protocol (GMCP - http://www.gammon.com.au/gmcp) and a WebSocket.

  `<players>` is a space-separated list of player names/dbrefs to send the message to. Player names which contain spaces should be given in "quotes". A descriptor number sends to that one connection, even before anyone has logged in on it; this is how an event handler answers [event socket] socket\`gmcp.

  `<package>` is the name of the package/message type. Names starting with `sharp.` are the server's own; see [oob engine].

  If specified, `<message>` is a JSON-formatted message to be sent. Use the JSON() function to construct valid JSON.

  You must be a wizard or have the Send_OOB power to send messages to anyone but yourself, or to a descriptor other than your own.

  A WebSocket (portal) connection is also told who sent the package: the objid of the object that ran oob(). Portal apps use it to ignore packages a player's own code sent to themselves.

  Returns the number of descriptors the message was sent to on success, or a string starting with #-1 on error.

- [oob supported]: the supported argument and GMCP Core.Supports
- [oob engine]: sending the server's own `sharp.` packages
- [oob examples]: sending Room.Info only to clients that listed it

## Sending only supported packages

  With the fourth argument `supported`, a telnet client is sent the package only when it listed the package, or a module it belongs to, in GMCP Core.Supports, or listed nothing at all: Blightmud, TinTin++ and MUSHclient never send Core.Supports, so an empty list means the client never said. `Room` covers `Room.Info`. A WebSocket (portal) connection always gets the package. Any other fourth argument is an error. Without it every client that agreed to GMCP gets the package, which suits packages Mudlet uses without listing. See [gmcpsupports()].

## The server's own packages

  Packages whose names start with `sharp.` are the server's own, such as `sharp.play`, which tells the portal who is playing and in which room. Sending one needs the `oob.engine` permission, which the wizard role has; anyone else gets `#-1 PERMISSION DENIED: NEEDS OOB.ENGINE`. The portal believes these packages as it believes the server, so they are for staff code that corrects what the server sent.

## Examples

Examples:
```sharp
> think oob(me, Room.Info, json(object, num, json(number, 1234)), supported)
1
```

Output: the number of your connections that took Room.Info. A Mudlet that listed `Room 1` counts; a client that listed modules without Room does not.


::: seealso
- [@oob]
- [json()]
- [wsjson()]
:::
