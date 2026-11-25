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
    },
    {
      "id": "accounts-attributes",
      "heading": "Account attributes",
      "lookup": "accounts attributes"
    },
    {
      "id": "accounts-attribute-permissions",
      "heading": "Who reads and writes account attributes",
      "lookup": "accounts attribute permissions"
    },
    {
      "id": "accounts-attribute-flags",
      "heading": "Attribute flags on an account",
      "lookup": "accounts attribute flags"
    },
    {
      "id": "accounts-attribute-locks",
      "heading": "Account attributes in locks",
      "lookup": "accounts attribute locks"
    },
    {
      "id": "accounts-attribute-keys",
      "heading": "Letting someone write one account attribute",
      "lookup": "accounts attribute keys"
    },
    {
      "id": "accounts-time-zone",
      "heading": "A time zone on an account",
      "lookup": "accounts time zone"
    },
    {
      "id": "accounts-examine",
      "heading": "Examining and decompiling an account",
      "lookup": "accounts examine"
    }
  ]
}
-->
# Accounts

An account is one login for the web portal and the account menu at the connect screen. Characters are linked to it: one account can hold many characters, and each character belongs to at most one account. The account's roles come from its characters, and its characters share the account's roles (see [roles]).

A character can reach an account four ways:

- `create <character> <password>` at the account menu, or New character on the portal, creates one already linked. See [create].
- `claim <character> <password>` at the account menu, `@account/claim <character>=<password>` in the game, or Claim an existing character on the portal, links one that already exists. See [accounts claiming].
- Staff link one with `@account/link` or the portal's character page. See [accounts staff].
- The first person to finish the portal's setup claims character #1.

A character on no account still plays the PennMUSH way, with [connect] and its own password. Linking it changes nothing about that: its password, its objects and its dbref stay the same.

## Claiming a character you already have

A character made by staff with [@pcreate], or brought over in a PennMUSH import, has no account. Its holder adds it to theirs by proving they know its password:

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

Softcode reads links with [account()] and [accountchars()], and hears about them through the player\`link and player\`unlink events (see [events]).

## Account attributes

An account keeps attributes of its own on `#acct:<key>`, which `account(<player>, ref)` returns. Use them for what belongs to the player rather than to one character: experience shared between alts, a badge, a staff note. The reference is not an object: it has no dbref, flags, location or owner of its own, and no code runs on it, so `u()`, [@trigger] and `$`-commands never reach it. Only the attribute commands and functions find it: `&`, [@set] (attributes only), [@atrlock], [@wipe], [@cpattr], [@mvattr], [@edit], [@atrchown], [examine], [@decompile], [get()], [xget()], [default()], [lattr()], [nattr()], [hasattr()], [hasflag()] (`#acct:<key>/<attribute>`), [lflags()], [set()], [attrib_set()] and [wipe()].

In the portal's Softcode Editor, search for `account(*<name>, ref)` to open an account's attributes.

Example:
```sharp
> &XP account(*Ariel, ref)=10
> think get(account(*Brand, ref)/XP)
```
Output: 10, when Ariel and Brand are on the same account.

Example:
```sharp
> think lattr(account(me, ref))
```
Output: the names of your account's attributes that you may see, such as XP BADGE.

See [accounts attribute permissions] for who may read and write them, and [preferences] for the player preferences packages keep here.

## Who reads and writes account attributes

Two permissions decide it, and the built-in roles hold them:

```text
Who                                      Reads  Writes
wizard role                              yes    yes
moderator role (players.moderate)        yes    yes
royalty and helper roles (players.view)  yes    no
the account's own characters             yes    yes
objects those characters own             yes    no
anyone else                              no     no
```

A guest reads only `visual` attributes, even on its own account, and writes none. Staff keep an attribute from the account's characters with [@attribute]: `@attribute/lock/write` leaves it to those who pass the key, and `@attribute/access <attribute>=wizard` to `game.wizard`. To let more people read every account, allow `players.view` on a role or on one object; to let them write, allow `players.moderate`. See [@role permissions] and [@permission].

Example:
```sharp
> @role/create xpclerk=Staff/XP clerk
> @role/allow xpclerk=players.moderate
> @role/assign *Morgan=xpclerk
```
Output: Morgan can now set and clear any account's attributes.

Attribute flags narrow this further; see [accounts attribute flags].

## Attribute flags on an account

Attribute flags and the attribute table work as on an object:

- `mortal_dark` keeps an attribute from everyone without `game.wizard`, moderators and royalty included.
- `wizard` lets only holders of `game.wizard` change it, so a moderator cannot.
- `visual` lets anyone read it, characters on other accounts included.
- `nearby` keeps it from everyone but those who may read the account anyway: an account is in no room, so nobody is near it.
- [@atrlock] and the `locked` flag work as they do on an object.
- [@attribute] defaults apply: `@attribute/access WIZNOTE=mortal_dark` makes every new WIZNOTE on an account mortal_dark, and an `@attribute/enum` limits its values.

Example:
```sharp
> &BIO account(*Ariel, ref)=Plays since 2019.
> @set account(*Ariel, ref)/BIO=visual
```
Output: anyone can now read the account's BIO with get().

## Account attributes in locks

A character holds its account's roles. So `@role/assign/account <player>=<role>` reaches every character on the account, and a `role^<role>` lock passes for each of them. A `perm^<permission>` lock works the same way.

Example:
```sharp
> @role/assign/account *Ariel=veteran
> @lock/enter Club=role^veteran
```
Output: Ariel and every other character on Ariel's account may enter the Club.

An attribute lock such as `XP:>5` reads the character trying the lock, not its account. To test an account attribute, use an evaluation lock. The lock runs as the locked object, so give that object `players.view`; without it, [account()] and [get()] refuse it.

Example:
```sharp
> &CANPASS Gate=[gte(get(account(%#, ref)/XP), 5)]
> @lock Gate=CANPASS/1
> @permission/allow Gate=players.view
> think elock(Gate, *Ariel)
```
Output: 1 when Ariel's account has an XP of 5 or more, otherwise 0. A character on no account always fails.

## Letting someone write one account attribute

`players.moderate` lets someone set and clear every attribute on every account. To let them write only one, staff give the attribute a write key in the attribute table (see [attribute keys]):

```sharp
> @role/create xpclerk=Staff/XP clerk
> @role/allow xpclerk=players.view
> @role/assign *Morgan=xpclerk
> @attribute/access XP=none
> @attribute/lock/write XP=role^xpclerk
> &XP account(*Ariel, ref)=15
```
Output: Morgan may now set the XP of every account, and of every object. Morgan may not clear it, change its flags, or set any other account attribute. The key also locks XP to each account's own characters: they set or clear it only by passing the key.

The key covers the write. Naming another character's account with [account()], as Morgan does in the last line, needs `players.view`, which the role's first permission gives. It also lets Morgan read every account attribute.

A read key works the same way for reading. A `role^` or `perm^` key passes for every character holding that role or permission, through its account too. An evaluation key such as `CHECK/1` never passes on an account, which runs no code.

## A time zone on an account

A TZ attribute on an account is the time zone of every character on it that has no TZ of its own: [time()], [timefmt()], [convsecs()], [convtime()] and [dateparse()] read it, and [timezone()] names it. See [timezones accounts].

## Examining and decompiling an account

`examine #acct:<key>` shows the account and then its attributes. The username is shown only to holders of `players.view`, as [account()] gives it. `examine/mortal` leaves out the username, `examine/brief` the attributes, and `examine #acct:<key>/<pattern>` shows only the matching attributes.

Example:
```sharp
> examine #acct:3
```
Output:
```text
Account 3(#acct:3)
Username: ariel-plays
Characters: Ariel(#12Pc), Brand(#40P)
XP [#1]: 10
```

[@decompile] writes only the commands that set the account's attributes, naming it by its reference. Run them with another account's reference to copy them.

Example:
```sharp
> @decompile #acct:3
```
Output:
```text
&XP #acct:3=10
@set #acct:3/XP=locked
```

Anyone who may not read the account is refused both: "Permission denied. #acct:3 is not your account, and you need the players.view permission."

::: seealso
- [claim]
- [create]
- [login]
- [@account]
- [ACCOUNT()]
- [ACCOUNTCHARS()]
- [roles]
:::
