# onboarding

`+welcome`: a getting-started checklist for new characters. It shows at login while a step is still open and any
time with `+welcome`. Each step is numbered, and `+welcome <number>` tells you more about it. The game chooses the
steps.

```
> +welcome
╔═════════════════════════════╡ Getting started ╞════════════════════════════╗
║ A few things to do first. The list keeps track as you go.                  ║
║ #           Step                     How                                   ║
║ -------------------------------------------------------------------------- ║
║ 1  [To do]  Learn the basics         help getting started walks you        ║
║                                      through moving, talking and chat.     ║
║                                      Done? +welcome/done 1                 ║
║ 2  [To do]  Read this game's help    +help lists this game's own commands  ║
║                                      and topics. Done? +welcome/done 2     ║
║ 3  [Done]   Describe your character                                        ║
║ 4  [To do]  Get approved             Staff approve characters before they  ║
║                                      join scenes. Ask staff how to apply.  ║
╠════════════════════════════════════════════════════════════════════════════╣
║ 3 steps to go. +welcome <number> tells you more about a step.              ║
║ +welcome/hide stops it showing at login.                                   ║
╚════════════════════════════════════════════════════════════════════════════╝
> +welcome 4
╔═════════════════════════════╡ 4. Get approved ╞════════════════════════════╗
║ [To do]                                                                    ║
║                                                                            ║
║ Staff approve characters before they join scenes. Ask staff how to apply.  ║
║                                                                            ║
║ Many games have staff look over a new character before it takes part in    ║
║ the story, to check it fits the setting. How you apply differs from game   ║
║ to game: the game's policies, its wiki or a member of staff can tell you.  ║
╠════════════════════════════════════════════════════════════════════════════╣
║ This ticks itself off once it is done · +welcome lists every step          ║
╚════════════════════════════════════════════════════════════════════════════╝
```

- **Checked or ticked.** A step with a check ticks itself off: setting a description, joining a channel, getting
  approved. Any other step the player ticks off with `+welcome/done <number>` (or the step's name).
- **At login.** The list shows on the connection that logged in, at every login while a step is open, with a line
  saying `+welcome` shows it again. A game can show it on the first login only, or never. `+welcome/hide` stops
  it for one player.
- **Per player.** A step can leave itself off a player's list: a guest gets no description or approval step, and
  the channel step shows only where there is a channel to join. Numbers follow the player's own list.

## Steps

The steps are a `+config` table (with `onboarding-config`): `+config onboarding/steps`. Each is an entry on the
Onboarding Steps object, `` STEP`<KEY> `` holding its label, with `HINT` (one line, in the list), `DETAIL` (up to 1,000 characters, for
`+welcome <number>`) and `ORDER` beneath. A step's key starts with a letter, so a number always means a position.

| Step | Order | Ticks itself off when |
|---|---|---|
| `basics` | 10 | never: the player ticks it off |
| `describe` | 30 | the player has a `DESCRIBE` (not shown to guests) |
| `channel` | 40 | the player is on a channel (shown only where the player can see one) |
| `approval` | 60 | `isapproved()` (not shown to guests) |

`+config/add onboarding/steps/rules=Read the house rules` adds a step the player ticks off.
`+config/remove onboarding/steps/approval` takes one away.

## Adding a step from another package

A package adds a step by attaching an entry to `{{onboarding/store}}` and, if it needs one, code to
`{{onboarding/guide}}`, keyed the same way. Each piece of code is given the player as `%0`:

- `` CHECK`<KEY> ``: true once the step is done. Without one, the player ticks it off.
- `` SHOW`<KEY> ``: false leaves the step off that player's list.
- `` NOTE`<KEY> ``: a line shown in place of the hint, such as where an application stands.

The bridges below are written this way. A package's own messages never name another package's command; the bridge
that joins them does.

## Settings

Registered with [`+config`](../plus-config/) by `onboarding-config` (`+config onboarding`), kept on the Onboarding
object:

| Setting | Type | Default | What it changes |
|---|---|---|---|
| `login` | choice: `open`, `first`, `off` | `open` | When the list shows at login: every login while a step is open, the first login only, or never. |
| `title` | text, 1 to 60 | Getting started | The list's title. |
| `intro` | text, up to 400 | A few things to do first. The list keeps track as you go. | The line above the steps. Empty shows none. |
| `steps` | table | the four above | The steps. |

## What is kept on the player

`` ONBOARDING`DONE`<KEY> `` when they ticked a step off, `` ONBOARDING`HIDE `` when they hid the list, and
`` ONBOARDING`SEEN `` when it first showed at login.

## Bridges

| Package | Adds |
|---|---|
| [`onboarding-help`](../onboarding-help/) | `+help welcome`, and a step: Read this game's help (`+help`), order 20 |
| [`onboarding-config`](../onboarding-config/) | `+config onboarding`, and `+help welcome staff` |
| [`onboarding-chargen`](../onboarding-chargen/) | The approval step says where the player's `+app` application stands |

## Objects

- **Onboarding**, in the master room, `WIZARD`: the command, the settings, the step code and the login copy (from
  `` PLAYER`CONNECT`ONBOARDING `` on the event handler).
- **Onboarding Steps**, inside it: the steps.
- **Onboarding Help**, inside it: the `+help welcome` topics, registered by `onboarding-help`.

Requires nothing but the event handler.
