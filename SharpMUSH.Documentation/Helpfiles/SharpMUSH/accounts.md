<!-- help-article
{
  "corpus": "help",
  "id": "accounts",
  "lookup": "accounts",
  "aliases": ["account"],
  "sections": [
    {
      "id": "accounts-claiming",
      "heading": "Claiming a character you already have",
      "lookup": "accounts claiming"
    },
    {
      "id": "accounts-staff",
      "heading": "Linking a character for someone",
      "lookup": "accounts staff"
    }
  ]
}
-->
# Accounts

An account is one login for the web portal and the account menu at the connect screen. Characters are linked to it: one account can hold many characters, and each character belongs to at most one account. The account's roles come from its characters, and its characters share the account's roles (see [roles]).

A character can reach an account four ways:

- `make <character> <password>` at the account menu, or New character on the portal, creates one already linked. See [make].
- `claim <character> <password>` at the account menu, `@account/claim <character>=<password>` in the game, or Claim an existing character on the portal, links one that already exists. See [accounts claiming].
- Staff link one with `@account/link` or the portal's character page. See [accounts staff].
- The first person to finish the portal's setup claims character #1.

A character on no account still plays the PennMUSH way, with [connect] and its own password. Linking it changes nothing about that: its password, its objects and its dbref stay the same.

## Claiming a character you already have

A character made at the connect screen with `create`, made by staff with [@pcreate], or brought over in a PennMUSH import has no account. Its holder adds it to theirs by proving they know its password:

- At the connect screen, `login` to the account, then type `claim <character> <password>`. See [claim].
- In the game, while playing a character on the account, type `@account/claim <character>=<password>`.
- On the portal, open Account, then Claim an existing character, and give the character's name and password.

The character then appears in your list, and [play] connects to it.

A character already on another account moves only with that account's password, so you can bring a character over from a second account of your own; its own password is not enough. A character with no password on no account cannot be claimed, since nothing proves who owns it: set one with [@password] while connected to it, or ask staff to link it.

## Linking a character for someone

Staff link a character to an account without its password, for a player who lost it or a character made for them:

- `@account/link <account>=<character>` in the game (wizards). `@account/unlink <account>=<character>` takes it off again.
- The portal's character page, under Administration, Characters (`players.moderate`): a character on no account shows a field for the account to link it to. Unlink is on the same page.

A character on another account has to be unlinked from it first. The account takes on the character's roles, so only God links God and only a wizard links a wizard. Both are recorded in the audit log.

::: seealso
- [claim]
- [make]
- [login]
- [@account]
- [roles]
:::
