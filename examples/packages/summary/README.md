# summary

`+summary <text>`: a line that sums up your last pose, told to the people in the room with you and kept
nowhere.

```
> +summary Bars the door and will not let anyone leave.
[SUMMARY] Mira: Bars the door and will not let anyone leave.
```

- **Local.** Everyone in the player's location hears it, the player included. Nobody elsewhere does.
- **Not recorded.** A scene running in the room does not log it, and it never reaches `+scene/recall` or
  the scene archive. The scene package records what its hooks on `POSE`, `SAY`, `SEMIPOSE` and `@EMIT` see,
  and its own `+scene/<mode>` verbs; `+summary` uses none of them and tells the room with `@remit`, which is
  not hooked.
- **Kept nowhere.** The package stores no summary, on the player or anywhere else.
- **As typed.** `+summary` is added with `@command/add/noparse/rsnoparse` and hooked to `` CMD`SUMMARY ``,
  so the text reaches the code exactly as typed and is never evaluated.

A summary is refused when it is empty, longer than the limit, or when the player is gagged or fails the
room's Speech lock.

## Settings

One setting, kept on the Summary object. The `summary-config` bridge registers it with [`+config`](../plus-config/) (`+config summary`):

| Setting | Type | Default | What it changes |
|---|---|---|---|
| `max-length` | integer, 20 to 2000 | 200 | The longest summary, in characters. A longer one is refused, not cut. |

## Extending the line

Another package can change what the room is told with a filter on the Summary object: an attribute named
`` FILTER`SUMMARY`LINE`<name> ``. Filters run in name order with `chain()`; each gets the line so far as
`%0`, the player as `%1` and the summary as typed as `%2`, and returns the line.

## Objects

- **Summary**, in the master room, `WIZARD`: the command, the setting and the line.
- **Summary Help**, inside it: the `+help summary` topics. The `summary-help` bridge registers them with the `+help` librarian.

Requires nothing else.
