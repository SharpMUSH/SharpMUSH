# achievements

`+achievements`: plaques staff award to characters. A plaque has a name, a description, and a frame in its own
colour, border and theme. A character holds as many as they are given, and picks a few to feature.

```
> +achievements/create Hero of the Siege=Held the north gate through the night.
[ACHIEVEMENTS] Done: Plaque 1, Hero of the Siege, is made. +achievements/award <player>=1 awards it.
> +achievements/award Ada=hero/For holding the north gate
[ACHIEVEMENTS] Done: Ada holds Hero of the Siege now.
```

- **Awarded by staff.** Nothing is awarded automatically. Staff holding `softcode.achievements.admin` make, change,
  award, take back and destroy plaques, in the game or on the Achievements page.
- **Styled.** `+achievements/style <plaque>/colour=<colour>` colours its name (a theme colour such as `success`
  follows each reader's theme; `hy`, `+gold` or `#d4af37` are that colour for everyone), and `/border` and `/theme`
  set its frame, drawn with `box()`.
- **Renames change nothing else.** A plaque keeps a number for good, and an award names the plaque by that number
  and the character by objid. Renaming a plaque or a character needs no other change, and nothing is written on the
  characters.
- **Featured plaques.** `+achievements/feature <plaque>` picks one the character holds, up to `+config
  achievements/featured` (3). The list is checked against what they hold every time it is read, so it can only name
  plaques they hold.
- **Citations.** `+achievements/award <player>=<plaque>/<citation>` says why; it shows under the plaque.

## Packages that go with it

| Package | What it adds |
|---|---|
| `achievements-app` | The Achievements page at `/apps/achievements`: every plaque and who holds it, and for staff forms to make, change, award, take back and destroy them |
| `achievements-profile` | A character's plaques at the bottom of their profile page, featured ones first |
| `achievements-finger` | The plaques a character features, as the Plaques field on `+oocfinger` (needs finger) |
| `achievements-help`, `achievements-config` | The topics in `+help` and the settings in `+config` |

## Storage

On the Achievements Store, inside the Achievements object in the master room. `<character>` is the objid with `#`
dropped and `:` a branch: `#15:1792044974852` is ``15`1792044974852``.

| Attribute | Holds |
|---|---|
| ``PLAQUE`<n>`` | The plaque's name; `DESCRIPTION`, `COLOUR`, `BORDER`, `THEME`, `CREATED` and `BY` beneath |
| ``AWARD`<character>`<n>`` | When it was awarded, in seconds; `BY` and `CITATION` beneath |
| ``FEATURED`<character>`` | The plaques the character features, in their order |
| ``DATA`LAST`` | The last number given, so a destroyed plaque's number is never used again |

Other packages read a character's featured plaques with
``u(<Achievements>/FUN`FEATURED`NAMES,<objid>)`` (their names in their colours) or ``FUN`FEATURED`` (their numbers).
