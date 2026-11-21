<!-- help-article
{
  "corpus": "help",
  "id": "casefile-command",
  "lookup": "@casefile",
  "aliases": ["CASEFILES", "CASEFILE"],
  "sections": [
    {"id": "reading", "heading": "Reading a case file", "lookup": "@casefile reading"},
    {"id": "members", "heading": "Members and levels", "lookup": "@casefile members", "aliases": ["@CASEFILE/ADD", "@CASEFILE/REMOVE"]},
    {"id": "developments", "heading": "Developments", "lookup": "@casefile developments", "aliases": ["@CASEFILE/POST"]},
    {"id": "opening", "heading": "Opening a case file", "lookup": "@casefile opening", "aliases": ["@CASEFILE/CREATE"]}
  ]
}
-->
# @casefile

- `@casefile`
- `@casefile <case file>`
- `@casefile/create <title>[=<briefing>]`
- `@casefile/add <case file>=<player or role:slug>[/<level>]`
- `@casefile/remove <case file>=<player or role:slug>`
- `@casefile/post <case file>=<title>/<what happened>`

A case file is one wiki page built around a plot, filed in the Case Files category. It opens with a briefing for newcomers, and its Developments section records what happened, newest first. Only its members, and holders of `wiki.admin`, can read it, on the portal or in-game. Everyone else finds nothing: the Case Files category, searches and wiki functions leave it out, and `@casefile <case file>` answers "No such case file".

`@casefile` lists the case files you are in. The portal has the same under Wiki, Case files.

- [@casefile reading]: showing one case file
- [@casefile members]: who may read, write, and let people in
- [@casefile developments]: posting what happened
- [@casefile opening]: opening a case file

::: seealso
- [wiki]
- [wiki permissions]
- [wiki categories]
:::

## Reading a case file

`@casefile <case file>` shows who is in one. Read the page itself with `@wiki <case file>`, and its edits with `@wiki/history`.

```sharp
> @casefile Harbour Fire
+=========================< Case file: Harbour Fire >========================+
| Someone set the Saltmarket warehouse alight on the night of the spring     |
| tide, and the harbourmaster's ledger went missing in the smoke.            |
| You are: contributor                                                       |
| Read it: @wiki Harbour Fire                                                |
| Updated: 2026-11-21                                                        |
| Members: Marta (lead), Ines (contributor), Pip (contributor), role         |
|          city-watch (reader)                                               |
+============================================================================+
```

Output: you contribute to Harbour Fire, and everyone with the city-watch role reads it.

## Members and levels

A member is a character or a role. A role lets in every character who holds it, so a case file needs no role or permission of its own. Each member has a level:

| Level | May |
|---|---|
| reader | read the page |
| contributor | also edit it and post developments |
| lead | also let people in and out, and change levels |

`@casefile/add` lets someone in, or changes their level; without a level they become a reader. `@casefile/remove` takes them out. Only a lead, or a `wiki.admin` holder, does either. A case file keeps at least one character as its lead, so name another before the last one steps down.

Members still need the wiki's own permissions: writing needs `wiki.edit`, which approved characters hold. Membership never lets anyone do more than that allows.

```sharp
> @casefile/add Harbour Fire=Ines/contributor
CASEFILE: Ines is now a contributor of Harbour Fire.
> @casefile/add Harbour Fire=role:approved
CASEFILE: Everyone with the approved role is now a reader of Harbour Fire.
```

Output: Ines may now edit the page and post developments, and every approved character may read it.

## Developments

`@casefile/post` adds a development to the top of the page's Developments section, which the first post adds at the end of the page. Each entry has the date, a title, who posted it, and the text. It needs a contributor or lead.

Every post is a revision of the page, so its history shows who posted what and when.

```sharp
> @casefile/post Harbour Fire=The ledger turns up/Two pages are torn out.
CASEFILE: Posted to Harbour Fire.
```

Output: a new entry headed with today's date and "The ledger turns up" sits first under Developments.

## Opening a case file

`@casefile/create` opens a case file: a page in the Case Files category with the text after `=` as its briefing. You become its lead. It needs `wiki.create`, and a title no other page has.

```sharp
> @casefile/create Harbour Fire=Who set the warehouse alight, and why the ledger is missing.
CASEFILE: Opened Harbour Fire. You are its lead. Let people in with @casefile/add Harbour Fire=<player>.
```

Output: the case file exists, with you as its only member.

Someone outside a case file who tries its title is told only "That title is taken. Choose another."
