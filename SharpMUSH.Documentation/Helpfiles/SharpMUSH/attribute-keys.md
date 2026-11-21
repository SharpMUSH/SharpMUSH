<!-- help-article
{
  "corpus": "help",
  "id": "attribute-keys",
  "lookup": "attribute keys",
  "aliases": ["attribute key", "@atrlock/read", "@atrlock/write"],
  "sections": [
    {"id": "attribute-keys-which", "heading": "Which key applies", "lookup": "attribute keys which"},
    {"id": "attribute-keys-limits", "heading": "What a key does not do", "lookup": "attribute keys limits"},
    {"id": "attribute-keys-showing", "heading": "Showing and copying keys", "lookup": "attribute keys showing"}
  ]
}
-->
# Attribute Keys

`@atrlock/read <object>/<attribute>[=<key>]`<br>
`@atrlock/write <object>/<attribute>[=<key>]`

An attribute can hold a read key and a write key. Each is a lock key, written as [@lock] takes one. Whoever passes the read key may read the attribute; whoever passes the write key may set its value. Neither needs to control the object.

A key only grants: someone who may already read or set the attribute still may, whether they pass the key or not.

With `=<key>`, the command sets the key on each attribute that matches `<attribute>` (a wildcard pattern works) and that you may set. An empty `<key>` clears it. Without `=`, it shows the key to anyone who may examine the object. [atrlock()] does the same from softcode.

Example:
```sharp
> &XP *Ariel=10
> @atrlock/write *Ariel/XP=role^xpclerk
```
Output: "XP - write key set." Anyone holding the xpclerk role may now set Ariel's XP, and nothing else on Ariel.

## Which key applies

The key that applies is the nearest one, from the attribute up its tree (see [attribute trees]). At each level, the attribute's own key comes first, then the key [@attribute] gives that name in the attribute table (`@attribute/lock/read` or `/write`). So a key on STATS covers STATS\`STR and STATS\`DEX unless they have keys of their own.

A table key also covers a copy that does not exist yet: whoever passes it may create the attribute.

Example:
```sharp
> @attribute/access XP=no_command
> @attribute/lock/write XP=role^xpclerk
```
Output: "XP - write key set." The xpclerk role may now set XP on every object and account, unless a copy has a write key of its own.

## What a key does not do

Attribute flags still apply on top of a key: `mortal_dark` and `wizard_dark` still hide, and `wizard`, `locked`, `safe` and `internal` still refuse a write.

A write key sets the value only. It never clears the attribute, which would drop its flags and keys, and never changes its flags or keys. Clearing and [@wipe] still need control of the object.

A key does not let anyone run the attribute. Who may trigger `$`-commands and `^`-listens is unchanged.

An account runs no code, so a key on an account's attribute cannot hold an evaluation lock such as `CHECK/1`. Use `role^<role>` or `perm^<permission>`; see [accounts attribute keys].

## Showing and copying keys

[examine] shows an attribute's own keys after its flags, to whoever may examine the object. Someone let in by a key sees the value, not the key.

Example:
```sharp
> examine Ledger/XP
```
Output:
```text
XP [#3 write:ROLE^xpclerk]: 10
```

[@decompile] writes an `@atrlock/read` or `@atrlock/write` line for each key, and `/skipdefaults` leaves out a key the attribute table already gives. [@cpattr] and [@mvattr] carry keys with the flags, and `/noflagcopy` leaves both behind. Clearing an attribute drops its keys.

::: seealso
- [@atrlock]
- [atrlock()]
- [@attribute lock]
- [locktypes]
- [attribute flags]
:::
