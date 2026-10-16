<!-- help-article
{
  "corpus": "help",
  "id": "account-command",
  "lookup": "@account",
  "aliases": [],
  "sections": [
    {"id": "claim", "heading": "Claiming a character", "lookup": "@account claim", "aliases": ["@account/claim"]},
    {"id": "administration", "heading": "Showing and administering accounts", "lookup": "@account administration", "aliases": ["@account/list", "@account/newpassword", "@account/disable", "@account/enable", "@account/close", "@account/delete"]},
    {"id": "linking", "heading": "Linking and unlinking characters", "lookup": "@account linking", "aliases": ["@account/link", "@account/unlink"]}
  ]
}
-->
# @account

`@account <name>`<br>
`@account/list [<pattern>]`<br>
`@account/newpassword <name>=<password>`<br>
`@account/disable <name>`<br>
`@account/enable <name>`<br>
`@account/close <name>`<br>
`@account/delete <name>`<br>
`@account/link <name>=<player>`<br>
`@account/unlink <name>=<player>`<br>
`@account/claim <character>=<password>`

Administers the web-portal accounts that characters are linked to. Wizard-only, except `/claim`.

Accounts are a SharpMUSH concept; PennMUSH has no equivalent command.

- [@account claim]: linking a character you play to your account
- [@account administration]: showing, listing, suspending and retiring accounts
- [@account linking]: adding characters to an account and taking them off

## Claiming a character

`@account/claim <character>=<password>`

`/claim` is for anyone playing a character that is on an account: it links `<character>` to that same account. The password is `<character>`'s own, or, for a character on another account of yours, that account's password, which moves the character over. See [accounts claiming].

## Showing and administering accounts

`@account <name>`<br>
`@account/list [<pattern>]`<br>
`@account/newpassword <name>=<password>`<br>
`@account/disable <name>`<br>
`@account/enable <name>`<br>
`@account/close <name>`<br>
`@account/delete <name>`

With no switch, shows one account's details. `/list` lists every account, or those whose username contains `<pattern>`, each with its state; a `*` after a name marks an account that must change its password at next login. `/newpassword` sets a password and requires the holder to change it at their next login. `/disable` and `/enable` suspend and restore access, and `/close` and `/delete` retire the account; the record is kept either way, so the characters linked to it are never orphaned. The account that holds God (#1) can never be disabled, closed, deleted or banned.

## Linking and unlinking characters

`@account/link <name>=<player>`<br>
`@account/unlink <name>=<player>`

`/link` adds a character to the account without its password: one made with [@pcreate], imported, or whose holder lost its password. A character already on another account is refused until `/unlink` takes it off that one. Only God links God, and only a wizard links a wizard. `/unlink` takes a character off the account; the character itself is kept. God (#1) is never unlinked from its account. Players link their own characters with [claim]. See [accounts staff].

::: seealso
- [@pcreate]
- [@newpassword]
- [register]
- [accounts]
:::
