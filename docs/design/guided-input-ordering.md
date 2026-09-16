# Guided-input prompt ordering

Binding for `InputSessionService`, `NotifyService`, `ListenerRoutingService` and the socket owner's
`MarkupOutputConsumer`. Tracks #1010.

## Contract

For one connection handle and one transport incarnation (`SessionId`):

1. A prompt or lifecycle notice for a guided-input capture is displayed in the order its lifecycle
   transition was committed. The transitions are start, reprompt, replacement (a second start),
   explicit cancellation (`@input/cancel` as a command), escape (`@input/cancel` typed as the reply)
   and revocation.
2. Output published after a transition commits is displayed after every prompt or notice reserved
   before that commit. This covers the cancellation notice, a replacement prompt, the timeout
   callback's output and the output of a character switch.
3. Output already authorized before a transition may still be displayed after the transition's own
   output has been reserved, but never after output reserved later. Nothing is retracted: a prompt
   whose capture has since been cancelled is still displayed, before the cancellation notice.
4. Output for another handle, or for another incarnation of the same handle, is unaffected. The
   existing `SessionId` check fences incarnations on both sides, before and after rendering.

Disconnect, timeout, callback failure and shutdown publish no notice of their own. Rule 2 still
orders whatever is published after them.

## Mechanism

- **One order per handle, reserved synchronously.** `HandlePublicationLane` (owned by
  `NotifyService`) gives each publication a place. A place starts publishing once every earlier
  place for the handle has published, been abandoned or been cancelled. A caller that gives up still
  holds its place until the earlier ones finish, so a later place can never overtake it.
- **Reserved under the input lock, published outside it.** `InputSessionService` takes the place
  while `_gate` commits the transition, then binds it (`HandlePublicationLane.Bind`) around its
  unchanged `PromptToSession` / `NotifyLocalizedToSession` call. It never awaits while holding
  `_gate`. Every other publication reserves its place when it publishes; puppet relays in
  `ListenerRoutingService` do too.
- **One subject on the wire.** A prompt travels as `MarkupOutputMessage { Prompt = true }`. The
  socket owner consumes that subject in one sequential loop and writes a prompt to the connection's
  prompt channel, so stream order is display order. A publication completes only once JetStream
  acknowledges it, so reservation order is stream order.

## Older socket owners

A socket owner advertises the capability with `ConnectionEstablishedMessage.OrderedPrompts` (and
`SessionResumeRequestMessage.OrderedPrompts` on a websocket resume). It also persists the capability
in connection metadata (`OrderedPrompts = "1"`), which engine reconciliation copies. A connection
without it keeps receiving `MarkupPromptMessage` on the separate prompt subject: publication is
still ordered, but delivery has no order relative to output, as before. The current socket owner
still consumes `MarkupPromptMessage`, so an older engine keeps working against it.

A notifier that is not an `IOrderedHandlePublisher`, such as a test substitute or a plugin's own
notifier, publishes in call order with no reservation.

## Restarts

Nothing in the contract is a counter, so there are no epochs to reconcile. Captures live in the
engine's memory and end with it. Anything a stopped engine published is already in the stream,
ahead of anything its successor publishes. A persistent socket accepts frames from either engine by
`SessionId` alone.
