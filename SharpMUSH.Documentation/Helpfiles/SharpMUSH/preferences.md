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

Players change them on the portal's Game preferences page, and a portal application reads and writes its own group through `api/preferences`. Both check each value against the declaration. The values are ordinary attributes, so softcode reads and writes them with the attribute functions and commands.

- [preferences storage]: account and character groups, and the attributes that hold them
- [preferences softcode]: reading and writing a preference in softcode

## Where a preference is kept

Each group says where its values live:

- `account`: on the player's account, `#acct:<key>`. Every character on the account, and every computer, sees the same value.
- `character`: on the character, so each has its own.

A value is the attribute ``PREF`<PACKAGE>`<GROUP>`<KEY>`` there, set NO_COMMAND. A character on no account has no account preferences, and `account()` returns nothing for it.

Uninstalling a package leaves its players' values in place, and installing it again picks them up.

## Preferences in softcode

A preference is an attribute, so [get()] reads it and [default()] reads it with a fallback while the player has set none. An account group's values are on `account(<player>, ref)`; a character group's are on the character.

Example:
```sharp
> think default(account(%#, ref)/PREF`CLIENT-PREFS`KEYBOARD`VOLUME, 70)
```
Output: "70" while the player has set nothing.

Example:
```sharp
> think default(%#/PREF`SHEET-DISPLAY`NOTICES`TAGLINE, none)
```
Output: the tagline the character set, or "none".

Example:
```sharp
> think if(account(%#), default(account(%#, ref)/PREF`CLIENT-PREFS`KEYBOARD`VOLUME, 70), 70)
```
Output: "70" for a character on no account too, for which `account()` returns nothing.

The usual attribute permissions apply: a player reads their own account's attributes, and another player's only with `players.view`. A character sets its own character preferences with `&`, and its account's on `account(me, ref)`, unless staff locked the attribute with [@attribute]. A package's command object that sets an account's value for a player needs `players.moderate`. Softcode setting an attribute directly is not checked against the declaration, so code reading it should cope with a value outside it.

::: seealso
- [accounts attributes]
- [accounts attribute permissions]
- [default()]
:::
