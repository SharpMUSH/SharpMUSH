<!-- help-article
{
  "corpus": "help",
  "id": "webhook-command",
  "lookup": "@webhook",
  "aliases": ["webhooks"],
  "sections": [
    {
      "id": "webhook-managing",
      "heading": "Managing webhooks",
      "lookup": "@webhook managing",
      "aliases": ["@webhook/list", "@webhook/create", "@webhook/url", "@webhook/events", "@webhook/enable", "@webhook/disable", "@webhook/delete"]
    },
    {
      "id": "webhook-deliveries",
      "heading": "What an endpoint receives",
      "lookup": "@webhook deliveries",
      "aliases": ["webhook payload"]
    },
    {
      "id": "webhook-signing",
      "heading": "Checking the signature",
      "lookup": "@webhook signing",
      "aliases": ["@webhook/secret", "@webhook/rotate"]
    },
    {
      "id": "webhook-retries",
      "heading": "Failures and retries",
      "lookup": "@webhook retries",
      "aliases": ["@webhook/test"]
    },
    {
      "id": "webhook-stream",
      "heading": "The event stream",
      "lookup": "@webhook stream"
    }
  ]
}
-->
# @webhook

`@webhook[/<switch>] [<name>[=<value>]]`

A webhook sends the game's events to a program outside the game: a Discord bot, a website, a logger. Each time an event it listens for happens (a player connects, a channel message is sent, an object is created), the server posts the event to the webhook's address. The program does not need to log in or poll the game.

The events are the same ones the event handler gets (see [events] and [event list]). A webhook hears an event whether or not the event handler has an attribute for it.

@webhook needs the `config.admin` permission, which the wizard role allows. Webhooks can also be managed in the web portal's admin pages. This is a SharpMUSH command; PennMUSH has no outbound webhooks.

- [@webhook managing]: creating webhooks and choosing their events
- [@webhook deliveries]: what an endpoint receives
- [@webhook signing]: checking that a delivery came from the game
- [@webhook retries]: failures, retries and the recent tries
- [@webhook stream]: hearing events without a public address

## Managing webhooks

- `@webhook[/list]`
- `@webhook <name>`
- `@webhook/create <name>[=<url>]`
- `@webhook/events <name>=<pattern> [<pattern> ...]`
- `@webhook/url <name>[=<url>]`
- `@webhook/enable <name>` and `@webhook/disable <name>`
- `@webhook/delete <name>`

A name is up to 64 letters, digits, `-` and `_`. Case does not matter.

`@webhook/create` makes a webhook and shows its signing secret (see [@webhook signing]). A new webhook hears no events. `@webhook/events` sets the events it hears, replacing any it had. Each pattern is an event name, where `*` matches anything and `?` matches one character. ``PLAYER`*`` is every player event, and `*` is every event.

The address must be a full `https://` URL. Addresses on a private network, such as `10.0.0.5` or `localhost`, are refused, so that webhooks cannot reach the server's own network. The server's operator can allow both (see [@webhook retries]). A webhook with no address is heard only through its event stream (see [@webhook stream]).

`@webhook` lists every webhook. `@webhook <name>` shows one: its address, its events, how many deliveries succeeded, failed or were dropped, and its most recent tries.

Example:
```
> @webhook/create logger=https://logs.example.com/game
Webhook logger created. It hears no events yet: set them with @webhook/events logger=<pattern> ...
Its signing secret is whsec_kJ0p3q9u5xv0Zr0c3D8m2YyZq1n6bq7N0gH8Qy3m5Lw=
> @webhook/events logger=player`connect player`disconnect channel`message
Webhook logger now hears: PLAYER`CONNECT PLAYER`DISCONNECT CHANNEL`MESSAGE
```

Creating, changing and deleting a webhook are recorded in the audit log.

## What an endpoint receives

Each event is one `POST` with a JSON body:

```
{
  "type": "PLAYER`CONNECT",
  "timestamp": "2026-10-14T21:04:11.000Z",
  "data": {
    "enactor": { "dbref": "#5", "objid": "#5:1792017800803", "name": "Alice" },
    "arguments": { "objid": "#5:1792017800803", "connections": "1", "descriptor": "3" },
    "positional": [ "#5:1792017800803", "1", "3" ]
  }
}
```

- `type` is the event's name.
- `enactor` is who caused the event, or `null` for an event the server raised itself.
- `arguments` has the event's arguments by the names [event list] gives them.
- `positional` has them in order, the way the event handler gets them as %0-%9.

Three headers come with it: `webhook-id` (the delivery's id, the same on every retry, so a receiver can ignore one it has already handled), `webhook-timestamp` and `webhook-signature`.

## Checking the signature

- `@webhook/secret <name>`
- `@webhook/rotate <name>`

Every delivery is signed with the webhook's secret, following the Standard Webhooks specification. That specification has libraries for most languages that check a delivery in one call. To check it by hand:

1. Join the `webhook-id` header, the `webhook-timestamp` header and the body with `.` between them.
2. Compute HMAC-SHA256 of that text. The key is the secret without its `whsec_` prefix, decoded from base64.
3. Encode the result in base64 and put `v1,` in front of it. It must equal the `webhook-signature` header.

A receiver should also refuse a timestamp more than a few minutes old.

`@webhook/secret` shows the secret again. `@webhook/rotate` gives the webhook a new secret. The old one stops working at once, including for deliveries still waiting to be retried, and any open event stream is closed.

## Failures and retries

- `@webhook/test <name>`

A delivery succeeds when the endpoint answers with a 2xx status. Otherwise it is tried again, after 5 seconds, then 25 seconds, then about 2 minutes, 10 minutes and 52 minutes, then every 2 hours, until 9 tries have failed. Then it is dropped. An endpoint that answers `410 Gone` is asking never to be sent to again: the webhook is turned off, and `@webhook <name>` shows why. `@webhook/enable` turns it back on.

A webhook keeps at most 1000 deliveries waiting. Past that, new events are dropped until the endpoint catches up. Deliveries waiting for a retry are kept in memory, so a restart drops them.

`@webhook/test` sends a ``WEBHOOK`TEST`` event at once, whatever events the webhook hears and even while it is off, and tries it only once. `@webhook <name>` then shows how it went.

The server's operator sets the limits in the `Webhooks` section of the server's settings, or with environment variables such as `Webhooks__MaxAttempts`: `MaxAttempts`, `FirstRetryDelay`, `MaxRetryDelay`, `Timeout` (10 seconds), `MaxPendingPerSubscription`, `AllowHttp` and `AllowPrivateNetworks`. Deliveries also count in the `sharpmush_webhook_attempts_total` and `sharpmush_webhook_duration_seconds` metrics.

## The event stream

A program that has no public address, such as a bot on a home computer, can hear a webhook's events by connecting to the server instead. It opens a WebSocket to:

```
wss://<your game's web address>/api/webhooks/<name>/stream
```

with the header `Authorization: Bearer <secret>`. Each event arrives as one text message, the same JSON a delivery's body has, with the delivery's id added as `id`. The stream gets the events the webhook hears, as long as the webhook is on, whether or not it has an address.

Nothing is kept for a stream that is not connected, and a program that falls behind loses its oldest events. Deleting the webhook or rotating its secret closes the stream.

::: seealso
- [events]
- [event list]
- [@http]
:::
