<!-- help-article
{
  "corpus": "help",
  "id": "preference-command",
  "lookup": "@preference",
  "aliases": ["@preferences"],
  "sections": [
    {"id": "showing", "heading": "Showing preferences", "lookup": "@preference showing"},
    {"id": "setting", "heading": "Setting a preference", "lookup": "@preference setting"},
    {"id": "reset", "heading": "Going back to the defaults", "lookup": "@preference reset", "aliases": ["@preference/reset"]}
  ]
}
-->
# @preference

`@preference`<br>
`@preference <group>[/<key>]`<br>
`@preference <group>/<key>=<value>`<br>
`@preference/reset <group>[/<key>]`

Shows and changes your own preferences: the settings the game's packages let each player choose, such as keyboard shortcuts or how a sheet is laid out. They are the same values the portal's Game preferences page shows. See [preferences].

- [@preference showing]: listing the groups, and one group's values
- [@preference setting]: changing a value
- [@preference reset]: going back to the defaults

## Showing preferences

`@preference`<br>
`@preference <group>[/<key>]`

Alone, lists every preference group the installed packages declare, with where each is kept: on your `account`, shared by all your characters, or on each `character`. Given a group, shows each preference in it, your value, and what it allows; given `<group>/<key>`, only that one. A value marked `(default)` is one you have not set.

Example:
```sharp
> @preference client-prefs.keyboard
```
Output: a table with a row for each key, such as "volume  70 (default)  integer, 0 to 100".

## Setting a preference

`@preference <group>/<key>=<value>`

Sets one preference. The value is checked as the package declares it: a number within its range, `on` or `off` (or 1, 0, yes, no, true, false) for a switch, one of a choice's values, or JSON for a JSON preference. A refused value changes nothing and says why, as does a value staff have protected (a wizard or locked attribute). The value is not evaluated, so brackets and `%` are kept as typed.

An account group needs a character that is on an account. A guest has no preferences.

Example:
```sharp
> @preference client-prefs.keyboard/volume=35
```
Output: "client-prefs.keyboard/volume is now 35."

## Going back to the defaults

`@preference/reset <group>[/<key>]`

Removes your value for `<key>`, or for every key in `<group>`, so the package's default applies again.

Example:
```sharp
> @preference/reset client-prefs.keyboard
```
Output: "client-prefs.keyboard is back to its defaults."

::: seealso
- [preferences]
- [PREF()]
:::
