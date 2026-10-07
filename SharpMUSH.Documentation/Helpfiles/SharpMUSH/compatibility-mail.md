<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-mail",
  "lookup": "compatibility mail",
  "aliases": [],
  "sections": [
    {
      "id": "a-mailfilter-that-mails-its-owner-does-not-recurse",
      "heading": "A `MAILFILTER` that mails its owner does not recurse",
      "lookup": "compatibility mail a mailfilter that mails its owner does not recurse"
    },
    {
      "id": "folders-are-named-freely-and-created-on-delivery",
      "heading": "Folders are named freely and created on delivery",
      "lookup": "compatibility mail folders are named freely and created on delivery"
    },
    {
      "id": "an-empty-mailforwardlist-is-no-list",
      "heading": "An empty `MAILFORWARDLIST` is no list",
      "lookup": "compatibility mail an empty mailforwardlist is no list"
    },
    {
      "id": "mail-listings-are-drawn-as-titled-sections",
      "heading": "Mail listings are drawn as titled sections",
      "lookup": "compatibility mail mail listings are drawn as titled sections"
    }
  ]
}
-->
# Compatibility Mail

`@mail` matches PennMUSH's commands and switches. Four differences are deliberate. Three are about
what happens to a message between the sender's `@mail` and the recipient's folder; the fourth is how
the listings look.

## A `MAILFILTER` that mails its owner does not recurse

**A choice.**

**PennMUSH** runs `filter_mail` (`extmail.c:3289`) on every delivery with no reentrancy guard, so a
filter that sends mail to its own owner filters that message too, and so on.<br>
**SharpMUSH** delivers mail sent from inside a filter unfiltered: the message lands in the inbox and
no filter runs for it.<br>
**Why.** The captured PennMUSH run crashed the server. Nothing useful depends on the recursion.<br>
**Workaround.** None needed. A filter that files its own notifications must do so by sending to the
folder it wants rather than by expecting its own filter to run again.<br>
**No example.** PennMUSH's side crashed the server when it was captured, and a crash would end the
parity run.

## Folders are named freely and created on delivery

**A choice.**

**PennMUSH** routes a filter's answer through `do_mail_file` (`extmail.c:616-630`), whose
`parse_folder` (`extmail.c:2839-2855`) accepts a digit `0` to `MAX_FOLDERS` or the name of a folder the
player has already used, and answers `MAIL: Invalid folder specification` otherwise.<br>
**SharpMUSH** accepts any alphanumeric name (`extmail.c:333`'s own rule for one) and makes it one of
the player's folders as the message is filed, exactly as `@mail/file` does. The new folder takes the
lowest folder number from 1 to 15 not already in use, so `maillist()` and `<folder>:<message>` name it
by number as they do any other; when all fifteen are taken, the name is refused as PennMUSH refuses it.<br>
**Why.** A filter is written before the folder it files into exists; requiring the player to create it
first means the first message that matches is the one that goes astray.<br>
**Workaround.** Filters written for PennMUSH keep working. A filter that returns a name PennMUSH would
have rejected files here instead of erroring, so check the spelling: a typo makes a folder.<br>
**Example.** The parity case `choice.mail-folder` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## An empty `MAILFORWARDLIST` is no list

**A choice.**

**PennMUSH** reads `&MAILFORWARDLIST me=` as a forward list naming nobody, and `empty_attrs`
(`extmail.c:1479`, `:1483`) means every message to that player is then dropped.<br>
**SharpMUSH** treats a whitespace-only value as no list at all, and mail is delivered normally. The
list is read without parents either way, so a parent's list never forwards a child's mail.<br>
**Why.** One `&MAILFORWARDLIST me=` should not silently stop a player's mail.<br>
**Workaround.** Nothing to change in code that never wrote an empty list. Do not reach for an empty
`MAILFORWARDLIST` as a way to stop receiving mail: it stops nothing here.<br>
**Example.** The parity case `choice.mail-forwardlist` in `tools/parity/scenarios/40-compat-choices.scn` runs it on both servers.

## Mail listings are drawn as titled sections

**A choice.**

**PennMUSH** prints the message list, a read message, `@mail/folder`, `@mail/stats` and `@malias`
as lines of its own (`MAIL: 2 messages in folder 0 [INBOX] (2 unread, 0 cleared).`) between rows of
dashes.<br>
**SharpMUSH** draws each between two double rules, the first carrying the title: a table of columns
for a list, labelled fields for a message's header, and a summary line under a divider. There are no
side borders, so a copied line is only its text. A client without UTF-8 gets the rules in `=` and `-`,
and a narrow client gets the section laid out again at its own width.<br>
**Why.** The same listing reads as a table on a terminal, in the web portal and to a screen reader;
padded lines only line up on the terminal they were padded for.<br>
**Workaround.** Code that reads mail goes through `mail()`, `mailstats()`, `folderstats()` and
`maillist()`, which answer as PennMUSH's do. Do not parse what `@mail` prints.<br>
**Example.** The parity case `mail.folder-numbers` in `tools/parity/scenarios/50-mail.scn` runs
`@mail/folder` on both servers.
