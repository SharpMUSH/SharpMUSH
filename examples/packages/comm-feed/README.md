# comm-feed

The ``CHANNEL`MESSAGE``, ``PAGE`MESSAGE``, ``PLAYER`CHANNELS`` and
``CHANNEL`WHO`` event handlers, delivered as an attach-mode package (decision
20.3). They are what fills the web portal's Play sidebar **Channels** and
**Pages** groups and its channel view and member list, as `room-contents` fills *Here* and *Exits*.

- **`comm.channels`** — `{"v": 2, "viewer": {…}, "channels": [ … ]}`, the
  channels the player is on, sent to that player on connect, on resume (a
  reloaded page keeps its session), and whenever
  their list changes (join, leave, their own channel flags, a rename or
  deletion).
- **`comm.message`** — `{"v": 2, "kind": "channel" | "page", …}`, one channel
  line or page, sent only to the players who received it: the engine names
  them, after every channel lock, gag, mute, `@chatformat`, page lock, HAVEN
  and interaction check has run. The package never widens that list. Each
  line carries `id`: for a channel line, the id the portal's recall endpoint
  returns for it; for a page, the id the page log keeps it under (when the
  game has `page_log` on).
- **`comm.who`** — `{"v": 2, "channel": "…", "member": {"name", "objid"},
  "online": true | false}`, a member coming onto or going off a channel's
  member list (connect, disconnect, join, leave, hiding on the channel), sent
  only to the members whose view of the list changed. The portal reads the
  whole list from `api/comm/channels/{channel}/who` when it opens a channel and
  keeps it current with these.

A connect or disconnect line on a channel (style `presence`) is not sent as a
`comm.message`: the member list shows who is on the channel instead. Players on
a terminal still see those lines as before, on every channel without the
`quiet` privilege.

There are no unread counts in the payloads: the engine does not know what a
player has read. The portal works them out from its own read markers.

It manages only these attributes (and its ``FN`COMM`*`` helpers) on the
configured `event_handler` object (`{{$event_handler}}`, `#9` by default). It
never creates or destroys the object, and uninstalling leaves the object's
other softcode untouched.

The payload shapes, the event arguments and the tests are in
[`docs/softcode/comm-feed-handler.md`](../../../docs/softcode/comm-feed-handler.md).

## Customising

| Attribute | Decides |
|---|---|
| ``FN`COMM`VIEWER`` (`%0` objid) | who is pushed to (default: a connected player) |
| ``FN`COMM`RECIPIENTS`` (`%0` objids) | who a `comm.message` or `comm.who` goes to — it can narrow the engine's list, never widen it |
| ``FN`COMM`TEXT`` (`%0` style, `%1` name, `%2` message) | the `text` of a line |
| ``FN`COMM`CHANNELROW`` / ``FN`COMM`CHANNELS`` | the `comm.channels` rows and payload |
| ``FN`COMM`MESSAGE`` | the `comm.message` payload |
| ``FN`COMM`WHO`` | the `comm.who` payload |

To silence one of them, blank its event attribute (`&PAGE`MESSAGE #9=`); to
remove them all, `@package uninstall comm-feed`.
