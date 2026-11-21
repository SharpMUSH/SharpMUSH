<!-- help-article
{
  "corpus": "help",
  "id": "casefile-command",
  "lookup": "@casefile",
  "aliases": ["CASEFILES", "CASEFILE"],
  "sections": [
    {"id": "reading", "heading": "Reading a casefile", "lookup": "@casefile reading"},
    {"id": "members", "heading": "Members and levels", "lookup": "@casefile members", "aliases": ["@CASEFILE/ADD", "@CASEFILE/REMOVE"]},
    {"id": "developments", "heading": "Developments", "lookup": "@casefile developments", "aliases": ["@CASEFILE/POST"]},
    {"id": "opening", "heading": "Opening a casefile", "lookup": "@casefile opening", "aliases": ["@CASEFILE/CREATE"]}
  ]
}
-->
# @casefile

- `@casefile`
- `@casefile <casefile>`
- `@casefile/create <title>[=<briefing>]`
- `@casefile/add <casefile>=<player or role:slug>[/<level>]`
- `@casefile/remove <casefile>=<player or role:slug>`
- `@casefile/post <casefile>=<title>/<what happened>`

A casefile is a private set of wiki pages built around a plot. Its briefing is what a newcomer reads first, its files are the pages filed in it, and its developments log records what happened, newest first. Only its members, and holders of `wiki.admin`, can read any of it, on the portal or in-game. Everyone else finds nothing: no listing, search or wiki function shows it, and `@casefile <casefile>` answers "No such casefile".

`@casefile` lists the casefiles you are in. The portal has the same under Wiki, Casefiles.

- [@casefile reading]: showing one casefile, and its pages
- [@casefile members]: who may read, write, and let people in
- [@casefile developments]: posting what happened
- [@casefile opening]: opening a casefile

::: seealso
- [wiki]
- [wiki permissions]
- [wiki categories]
:::

## Reading a casefile

`@casefile <casefile>` shows one. Read its pages with `@wiki`; file a page in it with `@wiki/category <page>=<casefile>`.

```sharp
> @casefile Harbour Fire
+=========================< Casefile: Harbour Fire >=========================+
| Who set the warehouse alight, and why the ledger is missing.               |
| You are:      contributor                                                  |
| Briefing:     @wiki Category:Harbour Fire                                  |
| Developments: @wiki Harbour Fire developments                              |
| Members:      Marta (lead), Ines (contributor), role approved (reader)     |
+=< Files (0) >==============================================================+
| None yet. File a page here with @wiki/category <page>=Harbour Fire.        |
+============================================================================+
```

Output: you contribute to Harbour Fire, which has no files yet; approved characters read it.

## Members and levels

A member is a character or a role. A role lets in every character who holds it, so a casefile needs no role or permission of its own. Each member has a level:

| Level | May |
|---|---|
| reader | read the briefing, files and developments |
| contributor | also write files, edit the briefing and post developments |
| lead | also let people in and out, and change levels |

`@casefile/add` lets someone in, or changes their level; without a level they become a reader. `@casefile/remove` takes them out. Only a lead, or a `wiki.admin` holder, does either. A casefile keeps at least one lead, so name another before the last one steps down.

Members still need the wiki's own permissions: writing needs `wiki.edit` and creating a file `wiki.create`, which approved characters hold. Membership never lets anyone do more than those allow.

```sharp
> @casefile/add Harbour Fire=Ines/contributor
CASEFILE: Ines is now a contributor of Harbour Fire.
> @casefile/add Harbour Fire=role:approved
CASEFILE: Everyone with the approved role is now a reader of Harbour Fire.
```

Output: Ines may now write files and post developments, and every approved character may read them.

## Developments

`@casefile/post` adds a development to the top of the casefile's log, a wiki page named after the casefile, such as "Harbour Fire developments". Each entry has the date, a title, who posted it, and the text. On the portal you can also tick the files it concerns, and the entry links them. It needs a contributor or lead.

Every post is a revision of the log page, so its history shows who posted what and when.

```sharp
> @casefile/post Harbour Fire=The ledger turns up/Two pages are torn out.
CASEFILE: Posted to Harbour Fire developments.
```

Output: a new entry headed with today's date and "The ledger turns up" sits at the top of the log.

## Opening a casefile

`@casefile/create` opens a casefile: its briefing page in the Category namespace, with the text after `=`, and an empty developments log. You become its lead. It needs `wiki.create`.

A casefile is a wiki category with members. Turning a category that already has pages into a casefile needs `wiki.admin`, so nobody can hide pages they did not write.

```sharp
> @casefile/create Harbour Fire=Who set the warehouse alight, and why the ledger is missing.
CASEFILE: Opened Harbour Fire. You are its lead. Let people in with @casefile/add Harbour Fire=<player>.
```

Output: the casefile exists, with you as its only member.

Someone outside a casefile who tries its title is told only "That title is taken. Choose another."
