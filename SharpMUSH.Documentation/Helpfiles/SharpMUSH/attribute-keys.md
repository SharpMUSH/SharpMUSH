<!-- help-article
{
  "corpus": "help",
  "id": "attribute-keys",
  "lookup": "attribute keys",
  "aliases": ["attribute key", "@attribute/lock/read", "@attribute/lock/write"],
  "sections": [
    {"id": "attribute-keys-which", "heading": "Which key applies", "lookup": "attribute keys which"},
    {"id": "attribute-keys-limits", "heading": "What a key does not do", "lookup": "attribute keys limits"},
    {"id": "attribute-keys-portal", "heading": "Keys in the portal", "lookup": "attribute keys portal"}
  ]
}
-->
# Attribute Keys

`@attribute/lock/read <attrib>[=<key>]`<br>
`@attribute/lock/write <attrib>[=<key>]`

A standard attribute can have a read key and a write key in the attribute table. Each is a lock key, written as [@lock] takes one. Whoever passes the read key may read that attribute on every object; whoever passes the write key may set it, or create it, on every object. Neither needs to control the object.

Keys are global and staff set them: only holders of `config.admin` may change one. A key only grants: someone who may already read or set the attribute still may, whether they pass the key or not.

`<attrib>` must already be in the attribute table (`@attribute/access`). Without `=`, the command shows the key. An empty `<key>` takes it off.

Example:
```sharp
> @attribute/access XP=none
> @attribute/lock/write XP=role^xpclerk
```
Output: "XP - write key set." Anyone holding the xpclerk role may now set XP on any object or account, and nothing else there.

## Which key applies

The key that applies is the nearest one, from the attribute up its tree (see [attribute trees]). So a key on STATS covers STATS\`STR and STATS\`DEX, unless the table gives either a key of its own.

A key is checked against the object holding the attribute, as a lock on that object would be. An account runs no code, so on an account's attribute an evaluation key such as `CHECK/1` never passes; `role^<role>` and `perm^<permission>` do (see [accounts attribute keys]).

## What a key does not do

Attribute flags still apply on top of a key: `mortal_dark` and `wizard_dark` still hide, and `wizard`, `locked`, `safe` and `internal` still refuse a write.

A write key sets the value only. It never clears the attribute, which would drop its flags, and never changes its flags. Clearing and [@wipe] still need control of the object.

A key does not let anyone run the attribute. Who may trigger `$`-commands and `^`-listens is unchanged.

`@attribute <attrib>` shows the keys only to holders of `config.admin`. Someone let in by a key sees the value, not the key.

## Keys in the portal

The portal's Configuration has an Attribute keys page under Security, for holders of `config.admin`. It lists every attribute that has a key, and sets or clears an attribute's two keys at once. When the game refuses a key, the page says why.

::: seealso
- [@attribute lock]
- [locktypes]
- [attribute flags]
- [@attribute]
:::
