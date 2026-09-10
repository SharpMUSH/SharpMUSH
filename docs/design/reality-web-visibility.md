# Reality visibility in the web portal

Reality layers use the same policy in game commands, object API reads, world snapshots, and live room events. Layers start disabled. Ordinary object access still requires its existing permissions when layers are enabled; staff privileges do not bypass a layer mismatch.

## Authenticated reads

`GET /api/game/state` returns the active character, its current room identity/name, and visible content identities. The response uses the existing `EngineStateResponse` shape with an optional `truncated` boolean. At most 1,000 visible identities are returned. Contents are streamed, and invisible objects do not contribute to the truncation flag. The room contents rules match `look`, including LIGHT, DARK, dark exits, and connected players.

The account-to-character link and full character identity are checked against current server data on every request. A missing character claim returns 401. A revoked link, unavailable location, or hidden current room produces no snapshot (404). The object editor API similarly checks the current character and applies reality visibility before returning metadata or attributes; a hidden target returns 404.

## Live events

`GameHub.JoinRoom` accepts only the current character's physical location. A hidden room can still carry an audible speaker on a shared layer, matching telnet hearing; the subscription itself exposes no room metadata. Subscription state is tied to the connection, account, full character identity, and full room identity. Every delivery checks the current link, current location and subscription for each recipient. Source-bearing events also check source identity and interaction permissions; enabled layers additionally filter reality masks. Leaving or replacing a subscription invalidates an in-flight subscription check. A failed recipient does not prevent delivery to other authorized observers.

Publishers of `RoomEventMessage` should set `ActorDbref` to the source object's full objid (`#number:creation`). `ActorName` remains display text. When layers are enabled, an absent, malformed, bare, stale, or hidden actor identity cannot deliver an event. NATS and internal hub helpers use the same dispatcher. When layers are disabled, legacy events without `ActorDbref` still require a fresh linked character and current physical room for each recipient. No event uses a room-group broadcast that could retain stale recipients.

Room subscriptions carry one current room per connection. Raw database maintenance and the explicit reality administration service remain separate from the ordinary world projection.
