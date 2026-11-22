<!-- help-article
{
  "corpus": "help",
  "id": "wiki-administration",
  "lookup": "wiki administration",
  "aliases": ["WIKI-ADMIN", "@WIKI/DELETE", "@WIKI/PROTECT", "@WIKI/UNPROTECT", "@WIKI/PUBLISH", "@WIKI/UNPUBLISH"],
  "sections": [
    {"id": "revision-history", "heading": "Revision history", "lookup": "wiki revision history", "aliases": ["@WIKI/HISTORY"]},
    {"id": "permissions", "heading": "Wiki permissions", "lookup": "wiki permissions", "aliases": ["WIKI-PERMISSIONS"]},
    {"id": "combining", "heading": "How requirements combine", "lookup": "wiki requirements combined"},
    {"id": "requirements", "heading": "Wiki requirements", "lookup": "wiki requirements", "aliases": ["@WIKI/REQUIRE", "@WIKI/ACCESS"]},
    {"id": "private-pages", "heading": "Private pages", "lookup": "wiki private pages", "aliases": ["@WIKI/MEMBER", "@WIKI/PRIVATE", "WIKI-MEMBERS"]}
  ],
  "redirects": {"wiki administration commands": "wiki administration"}
}
-->
# Wiki Administration

- `@wiki/delete <page>`
- `@wiki/protect <page>` and `@wiki/unprotect <page>`
- `@wiki/publish <page>` and `@wiki/unpublish <page>`
- `@wiki/history <page>`

Deleting needs `wiki.delete` and whatever the page requires to edit and delete it; protecting and publishing need `wiki.admin`. Deletion removes the page and its entire revision history. Protecting a page is a page requirement of `wiki.admin` to edit and delete it, in-game and on the web portal alike; it shows in `@wiki/access` and is cleared by `@wiki/unprotect`. Unpublished pages are drafts: hidden from anonymous web visitors and from the sitemap, and in-game their body and revision history are shown only to their author or a `wiki.drafts` holder who asks for them with `/draft`. See [wiki reading drafts].

- [wiki revision history]: revisions, numbered per locale
- [wiki permissions]: what each wiki permission allows
- [wiki requirements combined]: how requirements add up
- [wiki requirements]: setting and checking requirements
- [wiki private pages]: pages only their members read

## Revision history

@wiki/history lists every revision with its editor, date, and edit summary, for the revision stream of your own locale. Each locale is numbered independently starting from 1, so "rev 3" of a French translation is unrelated to "rev 3" of the source; `@wiki/history/source` shows the source locale's stream. An edit summary is prose about unpublished content, so a draft's revisions are withheld exactly as its body is: `@wiki/history/draft` shows them, to a `wiki.drafts` holder or the author.

::: seealso
- [wiki]
- [wiki editing]
- [wiki permissions]
:::

## Wiki permissions

Every wiki action has a permission everybody needs for it: `wiki.read`, `wiki.create`, `wiki.edit` and `wiki.delete`. On top of that, a namespace, a category or a single page can require more, as MediaWiki's namespace and page protections do. A target is `namespace <name>`, `category <name>`, or a page title as `@wiki` takes it.

The wiki's own permissions:

| Permission | Lets you |
|---|---|
| `wiki.read` | read pages |
| `wiki.create` | create pages |
| `wiki.edit` | edit and translate pages |
| `wiki.delete` | delete pages |
| `wiki.drafts` | read other people's drafts (see [wiki reading drafts]) |
| `wiki.admin` | protect, publish and set requirements; skips namespace, category and page requirements |

## How requirements combine

A page requires everything its namespace, each of its categories and the page itself require, for the action and for reading it; deleting also requires what editing does. Requirements only add: no level lifts what another requires. A player needs every permission listed, and a permission comes from their roles, like any other (see [roles]). `wiki.admin` skips namespace, category and page requirements, though not the four global permissions.

Filing a page in a category is an edit under its current categories and under the new ones, so a category that requires `lore.edit` to edit can only gain pages from someone holding it. Creating a page needs what its namespace requires to create it and what the categories it starts in require.

New games start with the system namespace requiring `wiki.admin` to create, edit and delete its pages.

A page can also be private, with members instead of a permission. It is read only by its members, and written only by its contributors and leads, on top of everything above. See [wiki private pages].

## Wiki requirements

- `@wiki/require <target>=<action> [<permission> ...][, <action> ...]`
- `@wiki/access <target>`
- `@wiki/access <page>=<player>`

`@wiki/require` replaces what the actions it names require and leaves the rest alone; an action with no permissions after it requires nothing again. The permissions must be built-in or defined with `@permission/define` (see [@permission define]). It needs `wiki.admin`.

`@wiki/access <target>` lists what a target requires; for a page, also what it inherits. `@wiki/access <page>=<player>` says, action by action, whether that player may act on the page and which requirement stops them. Asking about anyone but yourself needs `wiki.admin`. A page you may not read answers as one that does not exist.

```sharp
> @wiki/require category lore=edit lore.edit, read
WIKI: Category lore now requires:
    edit    lore.edit
> @wiki/access combat_primer=*Alice
WIKI: What Alice may do with 'Combat Primer':
  read    allowed
  edit    category lore requires lore.edit
  delete  needs wiki.delete
```

From softcode, `wikiaccess(<page>, <action>[, <player>])` returns 1 or 0.

::: seealso
- [wiki administration]
- [WIKIACCESS()]
- [roles]
- [@permission]
:::

## Private pages

- `@wiki/create/private <title>=<markdown>`
- `@wiki/member <page>[=<player or role:slug>[/<level>]]`

A private page is read only by its members and `wiki.admin` holders; to anyone else it does not exist. A member is a character, or everyone holding a role (`role:<slug>`). A `reader` reads, a `contributor` also edits, and a `lead` also lets people in and out. Membership never lifts other requirements: a contributor still needs `wiki.edit`.

`/create/private` makes you the page's lead. `@wiki/member <page>` lists the members; `=<who>/<level>` lets someone in or changes their level, and `=<who>` takes them out. Only a lead does these, and the page always keeps one character as its lead. Only `wiki.admin` makes a public page private. Someone who may not see a private page is told only that its title is taken.

```sharp
> @wiki/member Harbour Fire=Ines/contributor
WIKI: Ines is now a contributor of 'Harbour Fire'.
```

Output: one line. The `casefiles` package builds its case files on private pages.

::: seealso
- [WIKIMEMBERS()]
- [WIKIMEMBER()]
:::
