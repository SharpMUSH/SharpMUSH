# Play events for portal applications

A portal application can follow the tab's play connection: who is playing, whether the connection is up, which room
the character is in, and the out-of-band packages the server sends that character. Compiled components get it as a
cascading `IPlaySession`; hosted (`kind: ui`) pages get the same events over their bridge.

## The model

One tab plays one character, so a tab has one play session. `PlaySession` (`SharpMUSH.Client/Services/Play`) wraps
the play terminal and is built at startup, so it sees every package from the first. It turns the connection into an
ordered stream of `PlayEvent`s:

| Event | When |
|---|---|
| `PlayReset(Generation, Reason)` | What an app holds for the previous character or session is stale. A `PlayStateChanged` follows. |
| `PlayStateChanged(Generation, Previous, Current)` | The character, the connection or the room changed. |
| `PlayPackage(Generation, Package, Version, Data, Origin, Sender, SenderObjId)` | A package arrived. |

`PlayState.Generation` goes up on every reset, and every event carries the generation it belongs to. The set of event
types grows: an app switches on the ones it handles and discards the rest.

Reset reasons:

| Reason | Cause |
|---|---|
| `CharacterChanged` | The tab switched character (the play terminal was rebuilt while signed in), or the connection announced a different character. |
| `SignedOut` | The account signed out. Sent before the terminals hang up. |
| `NewSession` | A resume was refused and the connection logged in afresh. |
| `Ended` | The player quit, the server ended the session, or the page disconnected. |

## Subscribing

```csharp
[CascadingParameter] public IPlaySession? Play { get; set; }

protected override void OnInitialized() =>
	_subscription = Play?.Subscribe(new PlaySubscription { Packages = ["myapp.*"], Delivery = PlayDelivery.Latest },
		OnPlayEventAsync);
```

- `Packages` are exact names or prefixes ending in `.*`. Empty receives lifecycle events only.
- `Delivery = Latest` starts with the cached value of each matching package (`Origin = Cached`); `Each`, the default,
  starts with the next one. Use `Latest` for state packages and `Each` for events.
- `AcceptSelfSent` is off by default: a package the player's own softcode sent (`think oob(me, …)`) is dropped, since
  only a wizard or a holder of `Send_OOB` can send a package to someone else.

Each handler runs on the component's dispatcher, one event at a time in the order received, inside its own error
boundary: a handler that throws is logged and still receives the next event. Disposing the subscription stops it; the
application's session also ends every subscription when its page goes.

`ConnectAsync()` opens the play connection as the tab's active character. Connecting shows the character online and
runs its connect events, so an app calls it only from a player's click, never on first render.

## What an application may hear

An application declares the packages it reads. A compiled component declares them in
`RegisteredApplication.Subscribes`; a hosted page in its `ui.json` `subscribes`. The session hands each application
only the packages that match both its declaration and the subscription. `query.*` (the portal's own query answers) and
`sharp.*` (the engine's own packages) are never delivered and cannot be declared. At most 32 patterns of up to 128
characters, of letters, digits, `.`, `_` and `-`.

## Versions and tolerant reading

A payload's version is the integer in its top-level `"v"`, 1 when there is none and 0 when it is there but not a
positive integer, as `room.info`, `room.contents` and `room.exits` already do. A new field keeps the version; a
removed or re-meant field raises it.

`PlayPackage.Read<T>(minVersion, maxVersion)` returns a `PlayRead<T>` union and never throws:

| Case | Meaning |
|---|---|
| `T` | The payload, read with property names in any case, unknown properties skipped, numbers accepted from strings. |
| `PlayWrongVersion(Version)` | The version is outside the range asked for; nothing was read. |
| `PlayUnreadable(Path, Reason)` | A value of the wrong type, a null for a non-nullable value type or a missing `required` member, with its JSON path. |

Model payloads with nullable properties and defaults rather than `required`. The overload taking a
`JsonTypeInfo<T>` reads with a source-generated context instead of reflection.

## The engine's side

- Every `oob()` envelope to a WebSocket connection carries `"from"`, the objid of the object that ran it. The portal
  compares it with the playing character to tell `Self` from `Other`; no `from` reads as `Unknown`.
- `oob()` refuses packages under `sharp.` with `#-1 RESERVED PACKAGE NAME`.
- `PlayStateOobHandler` sends `sharp.play`, `{"v":1,"character":"#12:…","name":"…","room":"#0:…"}`, to the portal
  connection that logs in or resumes and to every portal connection of a player who moves. It rides the play
  connection, so it is ordered with every other package and reaches guests too. The portal accepts it only without a
  `from`.
- A frame replayed after a resume (between the server's `reattached` and its resume token) is marked
  `Origin = CatchUp`.

## Hosted pages

A hosted page's frame sends `{"type":"play-subscribe","v":1,"channel","requestId","packages":[…],"delivery":"each"|"latest","acceptSelfSent":false}`
and is answered `{"type":"play-subscribed","v":1,"channel","requestId","subscription","state"}`, or with `error`. Events
arrive as `{"type":"play-event","v":1,"channel","subscription","event"}`, where `event` has a `kind` of `reset`,
`state` or `package` (`HostedPlayEvents`). `{"type":"play-unsubscribe","channel","subscription"}` ends one. A frame
holds at most 16 subscriptions. A frame skips kinds and fields it does not know; a field is never removed or given
another meaning without raising `v`. Hosted pages cannot open the play connection: the host cannot tell a player's
click inside the frame from a script.

## Contract

`IPlaySession` and its types live in `SharpMUSH.Contracts` (namespace `SharpMUSH.Library.Models.Portal.Play`) and
arrived with plugin contract 4.1.0. `IPlaySession` keeps its three members; a later capability is a new interface the
same object also implements, found with `is`.
