<!-- help-article
{
  "corpus": "help",
  "id": "push-command",
  "lookup": "@push",
  "aliases": [],
  "sections": [
    {"id": "delivery", "heading": "When a notification is sent", "lookup": "@push delivery", "aliases": ["@push/always", "@push/urgent"]},
    {"id": "limits", "heading": "Limits and the signing key", "lookup": "@push limits"},
    {"id": "examples", "heading": "Examples", "lookup": "@push examples"}
  ]
}
-->
# @push

`@push[/always][/urgent] <player>=<title>[,<text>[,<path>[,<topic>]]]`

Sends a notification to every phone and browser where `<player>`'s account has turned notifications on, in the web portal under Settings, Notifications. It shows `<title>` with `<text>` under it, and opening it shows `<path>` in the portal, playing the character it was sent to. `<path>` is a portal page starting with `/`, such as `/mail` or `/play`; the front page when it is left out. A title is cut at 100 characters and the text at 1000.

The game sends nothing by itself: a notification goes out only when code runs @push. The bundled `notifications` package sends one for a page or new @mail, as each player chooses with `+notify`.

Using @push needs the `notify.push` permission, which the `wizard` role allows.

`@push` outputs one word: `sent`; `none` when the player has no device with notifications on, or no account; `attended` when the player is at the game and `/always` was not given; `limited` past `push_limit`. See [command output].

- [@push delivery]: topics, players at the game, and urgent notifications
- [@push limits]: how many notifications and browsers, and the signing key
- [@push examples]: sending a notification and reading the result

## When a notification is sent

`@push/always <player>=<title>[,<text>[,<path>[,<topic>]]]`<br>
`@push/urgent <player>=<title>[,<text>[,<path>[,<topic>]]]`

A later notification with the same `<topic>` for the same character replaces an earlier one, on the device and at the push service while it waits there, so ten pages from one person show as one notification.

Nothing is sent while `<player>` is at the game: a connection of theirs sent something in the last `push_idle_seconds` (300 by default), or a portal tab of theirs is open and in view. `/always` sends anyway. `/urgent` asks the push service to wake a device that is saving power; keep it for what cannot wait.

## Limits and the signing key

A character is sent at most `push_limit` notifications a minute (10 by default); @push drops the rest.

An account may turn notifications on in at most `push_devices` browsers (10 by default); each @push sends one request per browser.

The server signs every notification with a key it makes the first time it needs one, kept as the `push.vapid` secret (see [@secret]). `@secret/clear push.vapid` makes a new key the next time, after which every device has to turn notifications on again.

## Examples

```sharp
> &SEND me=@push Mira=Page from Tobin,,/play,page-tobin; think %>
> @include me/SEND
sent
> &SEND me=@push Tobin=Your scene starts soon; think %>
> @include me/SEND
none
```

::: seealso
- [@secret]
- [@config limits]
- [roles]
:::
