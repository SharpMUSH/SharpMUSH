# Authoring shared help articles

Help has one authoritative Markdown source. The engine indexes it on startup and
`@readcache`, then shares the same article identity with terminal help, the portal,
search, and documentation exports. It never truncates a lookup to fit a screen.

One lookup should answer one recognizable question. A small command or function
usually needs only purpose, syntax, essential behavior, an example, and related
topics. A substantial overview should introduce the topic and leave specialized
tasks to sections. Aim for a comfortable terminal screenful or two, allowing
complete examples and caveats to stay together. These are editorial suggestions,
not character or line limits.

Sections usually contain two to four paragraphs or a compact list and one to
three examples. Three to seven immediate children are usually easy to scan;
group a larger directory by reader intent when useful. Prefer shallow lookup
depth. Split when a reader moves from syntax to options, examples, permissions,
or compatibility; never replace `ALIGN4` with `examples 2`.

## Schema and example

Use one `.md` file per article in `SharpMUSH.Documentation/Helpfiles/SharpMUSH/`
(general help), `ahelp/` (administrator help), or `news/`. File order has no
meaning. Metadata is a leading `help-article` HTML comment containing JSON:

```markdown
<!-- help-article
{
  "corpus": "help",
  "id": "align",
  "lookup": "align()",
  "aliases": ["lalign()"],
  "sections": [
    {"id": "columns", "heading": "Column specifications", "lookup": "align columns"},
    {"id": "examples", "heading": "Basic examples", "lookup": "align examples"}
  ],
  "redirects": {"ALIGN2": "align columns"}
}
-->
# align()

Creates wrapped columns of text.

`align(<widths>, <column>[, <columnN>])`

## Column specifications

Explain justification and options here.

## Basic examples

Put complete commands and expected output in a fenced sharp block.
```

`corpus`, `id`, `lookup`, `aliases`, and `sections` are required. `redirects` and
`links` are optional. `links` declares additional help targets consumed by an
external presentation; ordinary authored shortcut links are checked automatically.
Unknown metadata fields are reserved for future extensions; do not rely on them.

Article IDs and section IDs use lowercase letters, digits, and hyphens, beginning
with a letter. Keep them stable when changing display titles or translating text.
Article IDs are unique within a corpus. Section IDs are unique within their
article. Canonical lookups, aliases, and redirects share one case-insensitive
namespace within a corpus. Aliases never carry their own text or web article.

H1 is the display title; an article has exactly one. Every H2 is an explicitly
declared lookup. H3 is a local subsection by default. To expose a narrower H3,
declare its unique heading and identity in `sections`; surrounding local H3s
stay in the enclosing section. Declarations identify headings by their exact
source spelling, including any inline code. The sections array is reading order,
independent of filenames, heading position, or numeric suffixes. Hierarchy is
contained in the article and section records, not reconstructed from names.
This deliberately shallow schema cannot represent parent cycles. Redirects must
point directly to canonical lookups in their article, never to another redirect.

## Presentation and links

Terminal overview = introduction before the first indexed section, plus a
generated directory of `help ...`, `ahelp ...`, or `news ...` commands. A section
lookup includes its local subsections and a parent command, excluding siblings.
No output depends on terminal hyperlink support. Related `[topic]` references
render as usable `help topic` command labels as well as command links.

Web article = overview plus each section once, in declared order. The TOC links
to IDs, not generated heading slugs. Topic and alias resolution uses the same
service as telnet, then deliberately selects the complete article for the web.
The entry API returns `articleId`, `sectionId`, and `canonicalHref` so clients
and search presentations can retain the matching section identity.

Follow existing `/help/{topic}` and `/help/admin/{topic}` routes. Section links
use `/help/align%28%29#examples`, for example. Do not introduce a second slug
system. Authored help links should use semantic canonical lookups such as
`[align examples]`; portal rendering resolves them to article and section URLs.
Use normal Markdown links for external URLs and wiki links for wiki content.

The entry API's `canonicalHref` retains the matching section fragment for deep
links. HTML `rel="canonical"` uses the article URL: every section route displays
the same complete article. Crawlers receive that complete article with the same
stable section IDs and TOC links.

There is no separate public docs build system in this repository. An external
generator can consume `GET /api/help/articles`: its article manifest is built
from the same indexed records. `GET /api/help/admin/articles` has the existing
Wizard/God account-session gate. Export each article once, retaining IDs and
section order; do not import engine help into the editable game wiki.

The PennMUSH parity harness also consumes the declared sections of compatibility
articles when validating its known-differences allowlist. Keep those section
headings synchronized with the allowlist's `profile` references.

The softcode editor's generated help drawer also consumes complete articles from
the shared model. The portal's build writes its `data/mush-*.json` files with
`tools/ClientData`, so a help change reaches the editor on the next build with
nothing to regenerate or commit.

## Formatting

- Use real section headings instead of bold paragraphs acting as headings.
- Put signatures and placeholders in code. `[placeholder]` outside code is a help link.
- Use fenced `sharp` blocks for commands with their expected output. Preserve
  significant whitespace. A heading inside a fence is ordinary code.
- Use paragraphs and lists instead of manual prose indentation and `<br>` layouts.
- Prefer compact lists for terminal references; use readable tables when useful.
- Let metadata generate directories and parent links. Authored previous/next
  chains are unnecessary; any future traversal must follow declared order.
- Keep legal notices intact. A historical catalog must be labeled as historical;
  current catalogs should come from their authoritative data source.

## Legacy sources, migration, and localization

The structural adapter accepts aggregate files with adjacent H1 aliases. The
first H1 is canonical; consecutive empty H1s point at the same body. Legacy IDs
are `legacy:<canonical lookup>` until the article receives an explicit stable ID.
Fenced and indented code cannot create entries. Legacy duplicate definitions
keep the first definition in deterministic file order and are logged; declared
article collisions fail indexing rather than silently overwriting content.

Numbered continuations are never canonical, visible directory entries, or new
authored links. Preserve only actual historical lookups as hidden redirects.
This migration retains them indefinitely as compatibility lookups; removal
requires a separately documented compatibility change. Do not invent variants
such as `align()2`. Ordinary aliases participate in prefix and wildcard matching,
but search returns the canonical entry once. Redirects participate only in exact
lookup. Content search indexes overview and section bodies separately, excluding
generated navigation and complete-article duplication.

Use the existing localized category directories (`help.fr`, `ahelp.fr`, etc.) and
`LocalizedTextFileService` fallback. Metadata corpus remains `help` or `ahelp`;
preserve article/section IDs and translate titles, text, and lookups as appropriate.
A missing translated category is an empty scoped lookup, never permission to
search another corpus. Administrator access gates remain unchanged.

`help-migration.json` accounts for old headings, semantic destinations, and source
body hashes. `help-inventory.json` records the resulting canonical entries,
aliases, and deprecated redirects. Intentional edits include the contradictory
attribute flag propagation text, the Unicode claim, removal of duplicate channel
function aliases, and moving drop-cap prose out of its code fence. Review corrections
also distinguish C# hardcode from PennMUSH's C, explain recycled dbrefs and durable
objids, label inert queue economy settings, repair JSON modification examples, and
give the economy and evaluation-lock articles useful overviews. Building costs now
describe quota slots rather than pennies; the executor-selection example has balanced
parentheses, and render compatibility is a declared profile decision. Related API and
listener lists retain distinct function/attribute names beside their shared canonical
help link instead of repeating an indistinguishable link. Mail examples use actual
fences, regex guidance distinguishes substring search from anchored validation, and
TOC labels come from parsed heading content rather than raw Markdown. The regex guides
now describe .NET syntax, Unicode/ASCII class choices, strict anchors, parser escaping,
and the distinction between PennMUSH-compatible softcode captures and .NET replacements;
the former PCRE manual attribution is retained. Further review corrections cover Unicode
accent display, escaped table pipes and lowercase thorn, the recycle command spelling,
quota-only destruction accounting, supported quota command forms, actual uptime output,
balanced filter notation, foreach markers, distinct listening/pose labels, the retry cap,
and Unix-second numeric timecalc input. The IANA 2011n
catalog is retained as a complete, explicitly historical section.

## Validation

Run `dotnet run --project SharpMUSH.Tests -- --treenode-filter
"/*/*/HelpArticleTests/*"` and the help service/command tests. The normal CI test
suite and the dedicated `help-integrity` CI job run the shipped-corpus check. Invalid declarations, missing section
targets, duplicate declared IDs or lookups, unresolved/deprecated declared links,
redirect chains, and numbered continuations fail. Existing unresolved links in
unmigrated articles remain legacy debt; unrelated edits are not blocked by them.
Size and screen readability remain editorial checks, never CI length gates.
