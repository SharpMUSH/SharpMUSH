<!-- help-article
{
  "corpus": "help",
  "id": "theme-command",
  "lookup": "@theme",
  "aliases": [],
  "sections": [
    {
      "id": "light-dark",
      "heading": "Light and dark",
      "lookup": "@theme light and dark",
      "aliases": [
        "@theme/light",
        "@theme/dark"
      ]
    },
    {
      "id": "parents",
      "heading": "Parents and the player ancestor",
      "lookup": "@theme parents"
    },
    {
      "id": "code",
      "heading": "Code in a theme",
      "lookup": "@theme code",
      "aliases": [
        "@theme/refresh"
      ]
    },
    {
      "id": "precedence",
      "heading": "Which theme wins",
      "lookup": "@theme precedence"
    },
    {
      "id": "errors",
      "heading": "Themes that do not read",
      "lookup": "@theme errors"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "@theme examples"
    },
    {
      "id": "game-themes",
      "heading": "The game's themes",
      "lookup": "@theme/list",
      "aliases": [
        "@theme/add",
        "@theme/remove",
        "@theme/disable",
        "@theme/enable"
      ]
    }
  ]
}
-->
# @theme

- `@theme[/light|/dark] <object>=<theme>`
- `@theme <object>=`
- `@theme/refresh [<player>]`

Sets the theme every layout is drawn in for a player: boxes, tables, gauges and the rest, from any function in [LAYOUT FUNCTIONS]. *<theme>* is anything the `"theme"` option takes (see [LAYOUT THEMES]): a name from [THEMES()], or a theme written out as JSON inside a second pair of braces, as for a layout function's options: `@theme me={{"seed":"#7aa2f7","harmony":"triadic"}}`. With nothing after the `=`, the theme is cleared.

Output: none.

- [@theme light and dark]: a theme made for a light or dark background
- [@theme parents]: themes taken from a parent or the player ancestor
- [@theme code]: code that picks a theme, and `@theme/refresh`
- [@theme precedence]: how a player's theme sits with the game's and a layout's own
- [@theme errors]: themes that do not read
- [@theme examples]: setting, checking and clearing a theme
- [@theme/list]: the themes the game offers, for staff

## Light and dark

`/light` makes the theme for a client with a light background, and `/dark` for a dark one. A theme made from one colour, which includes the genre themes, is made again for that background; a well-known scheme such as `nord`, or one of the portal's themes such as `cyberpunk`, stays as it is. The portal's themes come made for one background already: `daylight` is `phosphor` for a light one, and `deutan-light` is `deutan-dark`'s. Code given with `/light` or `/dark` is worked out first, and the theme it gives is kept, not the code. Code that works out to nothing is refused with them, since there is no theme to keep.

## Parents and the player ancestor

The theme is kept in the `THEME` attribute, and a player without one of their own takes the one their parent has, or their parent's parent, then the player ancestor's (see [ANCESTORS]). Set it on the object a group of players is parented to, a faction for example, and each of them gets it. *<object>* can be any object you control. `@theme <player>=` removes the player's own, so the inherited one shows again.

## Code in a theme

`@theme` keeps what follows the `=` exactly as it is typed, braces and all. Whenever the theme is needed it is evaluated as the player, with `%#` and `%!` the player, and what it works out to is the theme. Evaluating takes off the outer pair of braces round JSON, which is why JSON is typed inside two, and runs anything in `[ ]`, so a list in JSON is typed `\[ \]`. That lets one theme on an ancestor or parent choose by a value:

```
@theme #4=[if(strmatch(get(%#/FACTION),Rebel),horror,nord)]
```

The theme is worked out when the player connects, when `@theme` sets it, and when someone runs `@theme/refresh <player>`. Softcode that changes the value can run `@theme/refresh %#` after it. `/refresh` with no player refreshes your own, and says the theme in use. `@theme` works a theme out for *<object>* itself before it is kept, and refuses one that does not read. Code that works out to nothing is kept: for whoever it works out to nothing for, layouts use the game's theme.

## Which theme wins

A player's theme sits over the game's `layout_theme` and under a theme a layout names itself, so softcode that asks for its own colours still gets them. It applies to telnet and other MU* clients, which are sent each layout drawn again under it. The web portal, its play terminal included, draws layouts in the portal theme chosen there. Each portal theme has a theme of the same name here, in the same colours but for the genre themes, so `@theme me=cyberpunk` matches a portal set to Cyberpunk. You must control *<object>*.

Text written with [TONE()] names a theme colour, so it is drawn in the reader's theme too: their `muted` for `tone(muted,...)`, their `info` for `tone(info,...)`.

## Themes that do not read

`@theme` refuses a theme it cannot read and says why, leaving the old one in place. A `THEME` attribute set some other way that does not read, or that names a theme since removed or disabled, is ignored: layouts use the game's theme, and the player is told at login why theirs was not used.

## Examples

A built-in theme, then the same one for a light background:

```sharp
> @theme me=fantasy
Theme set.
> @theme/light me=fantasy
Theme set.
```

A theme made from one colour, with every colour made to stand out a little more than it must:

```sharp
> @theme me={{"seed":"#d08770","harmony":"split","contrast":0.5}}
Theme set.
```

A scheme copied from an editor theme, as sixteen base16 colours. A theme added with `@theme/add` (see [@THEME/LIST]) saves typing them, and the `\[ \]`, in each `THEME`:

```sharp
> @theme me={{"base16":\["#1d1f21","#282a2e","#373b41","#969896","#b4b7b4","#c5c8c6","#e0e0e0","#ffffff","#cc6666","#de935f","#f0c674","#b5bd68","#8abeb7","#81a2be","#b294bb","#a3685a"\]}}
Theme set.
```

Nord with red borders, and fantasy with plain bullets and round brackets about each title, its list typed `\[ \]`:

```sharp
> @theme me={{"preset":"nord","colors":{"primary":"#bf616a"}}}
Theme set.
> @theme me={{"preset":"fantasy","look":{"bullet":"-","title":\["( "," )"\]}}}
Theme set.
```

Which theme is in use, worked out again. With none set on the player, a parent or the ancestor:

```sharp
> @theme/refresh
No theme is set, so layouts use the game's theme.
```

With one set, it says `Theme in use:` and the theme.

A theme it cannot read is refused, and the old one stays. Clearing it goes back to the game's theme:

```sharp
> @theme me=nosuch
#-1 UNKNOWN THEME
> @theme me=
Theme cleared.
```

Try a theme on one layout with its `"theme"` option, and check its colours with [SWATCH()], before making it yours. A wizard sets the game's own with `@config/set layout_theme=<name>`, and staff choose which themes there are with [@THEME/LIST].

::: seealso
- [@THEME/LIST]
- [LAYOUT THEMES]
- [THEMES()]
- [SWATCH()]
- [ANCESTORS]
:::

## The game's themes

- `@theme/list`
- `@theme/add <name>=<theme>`
- `@theme/remove <name>`
- `@theme/disable <name>`
- `@theme/enable <name>`

The themes a game offers: the built-in ones, less those staff disabled, and those staff added. Every place a theme is named reads this list: [@THEME], the `"theme"` layout option, [THEME()], [SWATCH()], [THEMES()] and the `layout_theme` game option.

`/list` shows every theme, built-in and added, and whether it is offered. Anyone may use it.

`/add` adds a theme under *<name>*, or replaces the one added under that name. *<theme>* is evaluated once, as you, and what it works out to is kept: JSON inside a second pair of braces, or the name of another theme. It can start from any theme with `"preset"`, a disabled one included. An added theme it starts from is copied into it, so it does not change when that one does. *<name>* is lower-case letters, digits and hyphens, up to 32 of them, and cannot be a built-in theme's name.

`/remove` removes an added theme. `/disable` takes a built-in theme off the list, and `/enable` puts it back.

A player whose theme stops reading after a change, or is removed or disabled, sees the game's theme and is told why. Players see added themes and changes at once.

Changing the themes needs the `layout.admin` permission. The themes are kept with the game's data, not in `mush.cnf`.

Output: none.

### Example

Fantasy replaced with a version of the game's own, with red titles:

```
> @theme/disable fantasy
Theme fantasy disabled.
> @theme/add myfantasy={{"preset":"fantasy","colors":{"secondary":"#c0392b"}}}
Theme myfantasy saved.
> @theme me=myfantasy
Theme set.
> @theme me=fantasy
#-1 UNKNOWN THEME
```

::: seealso
- [@THEME]
- [LAYOUT THEMES]
- [THEMES()]
:::
