# alts

`+alts` lists the characters on your account. The list is private until you make it public, and you can keep
any one character off the public list.

## Commands

| Command | What it does |
|---|---|
| `+alts` | Every character on your account: whether each is online, and whether it is on your public list |
| `+alts <name>` | Someone else's alts, when their list is public and that character is on it |
| `+alts/public on` / `off` | Make your list public, or private again. For the whole account, from any of its characters |
| `+alts/hide <name>` / `+alts/unhide <name>` | Keep one of your characters off the public list, or put it back |

Asked about someone whose list is private, a hidden character, or a character on no account, `+alts` gives the
same answer, that their alts are private, so it never says how many alts there are or whether there are any.
Staff with the `players.view` permission see every character on an account, hidden ones marked. A character
shows as online only when the reader could already see it connected in `WHO`.

## How it is built

- **Alts** (master room, `WIZARD`) holds the command. It reads `account()` and `accountchars()`, which need
  `players.view` unless the asker is on the same account, so the package itself decides who sees what.
  `+alts` is added with `@command/add/noparse/rsnoparse` and hooked, so nothing a player types is evaluated.
- **Alts Store** (inside Alts, `no_command`) keeps each account's choices by account id, never on a character,
  so a choice follows the account: ``ACCOUNT`<key>`PUBLIC`` and ``ACCOUNT`<key>`HIDDEN`<character>``.
- ``PLAYER`UNLINK`ALTS`` on the event handler forgets a hidden character when it leaves its account.
- Help is registered with `+help` (`+help alts`, `+help alts staff`).

Requires `plus-help`, a configured event handler, and the `account()`/`accountchars()` functions and the
``PLAYER`UNLINK`` event.
