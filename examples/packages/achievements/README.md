# achievements

`+achievements`: plaques staff award to characters. A plaque is a name on a pill of its own colours, style and
typeface, with a description. A character holds as many as they are given, and picks a few to feature.

```
> +achievements/create Hero of the Siege=Held the north gate through the night.
[ACHIEVEMENTS] Done: Plaque 1, Hero of the Siege, is made. +achievements/award <player>=1 awards it.
> +achievements/colour hero=#f5e9c8/#1b3a5c
[ACHIEVEMENTS] Done:  Hero of the Siege  is #f5e9c8 on #1b3a5c now.
> +achievements/award Ada=hero/For holding the north gate
[ACHIEVEMENTS] Done: Ada holds Hero of the Siege now.
```

- **Awarded by staff.** Nothing is awarded automatically. Staff holding `softcode.achievements.admin` make, change,
  award, take back and destroy plaques, in the game or on the Achievements page.
- **Looks like a plaque.** `+achievements/colour <plaque>=<text>/<background>` gives the pill its two colours
  (`#rrggbb` or a name such as `+gold`), `+achievements/style <plaque>=<styles>` any of bold, italic and
  underline, and `+achievements/typeface <plaque>=<typeface>` its face on the website: the theme's own `title` or
  `mono` face, which change with the theme, or `serif`, `sans` or `script`. Without colours a pill takes the portal
  theme's chip colours, and the reader's own in a terminal.
- **CSS if you want it.** `+achievements/css <plaque>=<declarations>` is optional CSS for the pill on the website,
  such as `letter-spacing: 0.1em; border: 1px solid #c9a227`. It is declarations only and loads nothing, and it
  cannot set colours or the face, or move, hide or lay the pill over the page; the game refuses such CSS, and the
  portal leaves it out too.
- **Readable in every colour vision.** The two colours go together and need a contrast of 4.5:1 (WCAG AA), checked
  when they are set. Contrast is lightness, which the protan, deutan, tritan and monochrome themes all keep, so a
  pair that passes reads in them too. The name is always written, so the colour never carries the meaning alone,
  and a thin line in the theme's border colour keeps a pill apart from a page of the same lightness.
- **On the website, pills.** The Achievements page and a character's profile show plaques as a row of pills; the
  description shows when a pill is hovered or focused.
- **Kept on the character.** An award, who gave it and why, and the plaques a character features are on that
  character, in their `ACHIEVEMENT` attributes. Only wizards write those, so a player cannot give themselves a
  plaque or fake one on `+finger`; a tree whose root is not flagged wizard counts for nothing. Only the plaques,
  which belong to the whole game, are on the store. Awards outlive an uninstall, so a plaque made after reinstalling
  takes a number above any a character carries.
- **Renames change nothing else.** A plaque keeps a number for good, and an award names the plaque by that number.
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

On each character, under `ACHIEVEMENT` (flagged wizard and no_command by the package):

| Attribute | Holds |
|---|---|
| ``ACHIEVEMENT`<n>`` | When plaque `<n>` was awarded, in seconds; `BY` (who awarded it) and `CITATION` beneath |
| ``ACHIEVEMENT`FEATURED`` | The plaques the character features, in their order |

On the Achievements Store, inside the Achievements object in the master room:

| Attribute | Holds |
|---|---|
| ``PLAQUE`<n>`` | The plaque's name; `DESCRIPTION`, `BACKGROUND`, `FOREGROUND`, `STYLE`, `TYPEFACE`, `CSS`, `CREATED` and `BY` beneath |
| ``DATA`LAST`` | The last number given, so a destroyed plaque's number is never used again |

Other packages read a character's featured plaques with
``u(<Achievements>/FUN`FEATURED`NAMES,<objid>)`` (their names as pills) or ``FUN`FEATURED`` (their numbers).
