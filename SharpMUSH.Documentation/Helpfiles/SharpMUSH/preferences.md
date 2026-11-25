<!-- help-article
{
  "corpus": "help",
  "id": "preferences",
  "lookup": "preferences",
  "aliases": ["preference groups"],
  "sections": [
    {"id": "storage", "heading": "Where a preference is kept", "lookup": "preferences storage"},
    {"id": "softcode", "heading": "Preferences in softcode", "lookup": "preferences softcode"}
  ]
}
-->
# Preferences

A package can let each player choose settings of their own: keyboard shortcuts, a client's sounds, how a sheet is shown. It declares them as preference groups. Each group has an id, `<package>.<group>`, and a set of keys, each with a type, a default and the values it allows.

Players change them with [@preference] or on the portal's Game preferences page, and a portal application reads and writes its own group through `api/preferences`. Whichever way a value is set, every other way sees it.

- [preferences storage]: account and character groups, and the attributes that hold them
- [preferences softcode]: reading a preference, and writing one from a package

## Where a preference is kept

Each group says where its values live:

- `account`: on the player's account, `#acct:<key>`. Every character on the account, and every computer, sees the same value.
- `character`: on the character, so each has its own.

A value is the attribute ``PREF`<PACKAGE>`<GROUP>`<KEY>`` there, set NO_COMMAND. A character on no account keeps no account preferences: it has their defaults.

Uninstalling a package leaves its players' values in place, and installing it again picks them up.

## Preferences in softcode

[pref()] reads a player's value, or the default when they set none. It reads the attribute as [get()] would, so it refuses another player's unless you may read their attributes.

A package that writes a preference for a player, such as from a `+command`, can set the attribute directly; a value the declaration does not allow reads as the default.

Example:
```sharp
> think pref(client-prefs.keyboard, volume)
```
Output: "70" while the player has set nothing.

::: seealso
- [@preference]
- [PREF()]
- [accounts attributes]
:::
