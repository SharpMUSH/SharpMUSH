<!-- help-article
{
  "corpus": "help",
  "id": "named-arguments",
  "lookup": "named arguments",
  "aliases": [
    "named args"
  ],
  "sections": [
    {
      "id": "passing-named-arguments",
      "heading": "Passing named arguments",
      "lookup": "named arguments passing"
    },
    {
      "id": "presence-and-migration",
      "heading": "Presence and migration",
      "lookup": "named arguments presence"
    },
    {
      "id": "naming-a-functions-arguments",
      "heading": "Naming a function's arguments",
      "lookup": "named arguments functions"
    },
    {
      "id": "the-rest-of-the-arguments",
      "heading": "The rest of the arguments",
      "lookup": "named arguments rest"
    },
    {
      "id": "defaults-and-caller-names",
      "heading": "Defaults, caller names and errors",
      "lookup": "named arguments rest defaults"
    },
    {
      "id": "naming-a-commands-arguments",
      "heading": "Naming a command's arguments",
      "lookup": "named arguments commands"
    },
    {
      "id": "arguments-the-game-names",
      "heading": "Arguments the game names",
      "lookup": "named arguments engine"
    },
    {
      "id": "formats-and-queries",
      "heading": "Arguments formats and queries name",
      "lookup": "named arguments engine formats"
    }
  ]
}
-->
# Named Arguments

A named argument is an argument passed under a name alongside its position. Code reads it with %`\<name\>`, or with r(`<name>`, args), where it would read %0 with %0 or r(0, args).

```sharp
&FUN`ROW me=%<name> has %<count> posts.
think uargs(me/FUN`ROW, name, Bob, count, 3)
```
Output: `Bob has 3 posts.`

- Names are case-insensitive: %`\<count\>` and %`\<COUNT\>` are the same argument.
- The name inside %`\<...\>` is evaluated, so %`\<item[add(1,1)]\>` reads `item2`.
- %`\<0\>` is %0, so a number reads the argument in that position.
- A name nobody passed is empty.
- Named arguments belong to one call, as %0-%9 do. The attribute's own u() calls don't see them, and nothing is left behind when it returns. Q-registers (setq()) stay set for the rest of the queue entry, so a value handed to a helper is better passed by name.

An unclosed %`\<` is a parse error, as an unclosed %q`\<` is.

## Passing named arguments

| Where | Passes |
| --- | --- |
| uargs(`<obj>/<attr>`, `<name>`, `<value>`, ...) | names and ordered numbered value aliases |
| @trigger/args `<obj>/<attr>`=`<name>`,`<value>`,... | names and ordered numbered value aliases |
| @include/args `<obj>/<attr>`=`<name>`,`<value>`,... | names and numbered aliases overlaid on the caller's arguments |
| stepargs(`<obj>/<attr>`, `<list>`, `<names>`) | each run's elements under `<names>` and numbered slots |

```sharp
&FUN`PAIR me=%<name>=%<count>
think stepargs(me/FUN`PAIR,Bob 3 Alice 1,name count,,|)
```
Output: `Bob=3|Alice=1`

```sharp
&DO`GREET me=&LAST`GREETING me=Welcome, %<who>.
@include/args me/DO`GREET=who,Bob
think get(me/LAST`GREETING)
```
Output: `Welcome, Bob.`

## Presence and migration

Includes replace numbered slots supplied by the overlay and retain untouched inherited slots, including `/chain/args`. For example, including `who,Bob` replaces inherited %0 with Bob while retaining inherited %1. This changes the earlier names-only behavior. Repeated names retain ordered numbered values; their named value is last-wins. Numeric names must match their supplied position after leading-zero normalization.

`hasarg(name)` checks visible keys, including empty values. `hasarg(name,supplied)` checks inputs supplied to this invocation. Inherited include keys and generated count keys are visible but not supplied. A nested attribute call starts its own supplied set; built-in wrappers preserve the surrounding set.

With `@trigger/match/args`, the shared command/listen matcher supplies captures first, then named values overlay their numbered aliases. Untouched captures stay available. The match string itself does not occupy a numbered slot. Both captured inputs and explicit overrides are supplied to that invocation.

## Naming a function's arguments

A global function made with @function gets its arguments as %0, %1 and so on. `@function/args <function>=<names>` also passes them under space-separated names, in order. Needs the config.admin permission. Owner-local functions use `@function/local/args <name>=<names>` with the same signature grammar and their existing owner scope. Redefining the function clears its names, so set them after it in the same @startup.

```sharp
&FN`GREET me=Hello, %<who>. You have %<count> new posts.
@function greet=me,FN`GREET
@function/args greet=who count
think greet(Bob,3)
```
Output: `Hello, Bob. You have 3 new posts.`

%0 and %1 still hold `Bob` and `3`. `@function/args greet=` clears the names, and `@function greet` lists them.

## The rest of the arguments

The last name can take every argument after the ones before it. These are the shapes the built-in functions' own parameter lists use, as an editor shows them (switch() reads `string`, `expression...|list...`, `default`):

| Last name | The rest arrive as |
| --- | --- |
| `items...` | `items1`, `items2`, ..., and their number as `itemscount` |
| `key...\|value...` | dealt out in turn: `key1`, `value1`, `key2`, `value2`, ..., each with its count (`keycount`, `valuecount`) |
| `case...\|result... default` | as above, and `default` takes the argument left over after whole groups, as switch() does |
| `...` | name and value pairs the caller names |

```sharp
&FN`GLUE me=[iter(lnum(1,%<itemscount>),%<items##>,%b,%<sep>)]
@function glue=me,FN`GLUE
@function/args glue=sep items...
think glue(-,a,b c,d)
```
Output: `a-b c-d`

```sharp
&FN`LABELS me=[iter(lnum(1,%<keycount>),%<key##>: %<value##>,%b,%b|%b)]
@function labels=me,FN`LABELS
@function/args labels=key...|value...
think labels(Name,Bob,Rank,Captain)
```
Output: `Name: Bob | Rank: Captain`

## Defaults, caller names and errors

A `default` after the groups takes the argument left over:

```sharp
&FN`PICK me=[firstof(trim(iter(lnum(1,%<casecount>),if(strmatch(%<value>,%<case##>),%<result##>))),%<default>)]
@function pick=me,FN`PICK
@function/args pick=value case...|result... default
think pick(b,a,1,b,2,none)
think pick(c,a,1,b,2,none)
```
Output: `2`, then `none`.

With a bare `...`, the caller picks the names, as with uargs():

```sharp
&FN`CARD me=%<title>: %<name>, %<rank>
@function card=me,FN`CARD
@function/args card=title ...
think card(Crew,name,Bob,rank,Captain)
```
Output: `Crew: Bob, Captain`

A call that leaves a pair unfinished returns `#-1 NAMED ARGUMENTS COME IN PAIRS`. A pair whose name is empty, a number, or one of the function's own names returns `#-1 ARGUMENT NAME INVALID`.

`@function/args` returns `#-1 ARGUMENT NAME INVALID` for repeated/numeric names, multiple rest groups, names after `items...`, or collisions with generated slots (`key1`, `keycount`). Rest prefixes must also produce distinct slots.

## Naming a command's arguments

An @hook reads its command's arguments as `LS`, `RS`, `LSA1`, `LSA2` and the others in [@hook registers]. `@command/args <command>=<names>` names them as well, in order: the left side, then each right-side argument. The rest shapes above work here too. It needs the config.admin permission and lasts until the server restarts, like the other @command switches.

```sharp
&HOOK`CHOWN me=[attrib_set(me/LAST`CHOWN,%<attribute> to %<owner>)]
@command/args @atrchown=attribute owner
@hook/before @atrchown=me,HOOK`CHOWN
&NOTE me=hello
@atrchown me/NOTE=me
think get(me/LAST`CHOWN)
```
Output: `me/NOTE to me`

A hook can't refuse its command, so when a command named with `...` is given an unfinished pair, its hook gets none of the pairs.

## Arguments the game names

- @hook attributes: `ARGS`, `LS`, `RS`, `EQUALS`, `SWITCHES`, `LSAC` and `LSA<n>` (see [@hook registers]).
- @http callbacks: %`\<status\>` and %`\<content-type\>`, with the body in %0 (see [@http]).
- @input callbacks: %`\<reason\>` (see [@input]).
- Event handlers: each argument by the name the event shows, such as %`\<destination\>` for object\`move (see [event list]).
- @feed tie-ins and taps: %`\<id\>`, %`\<key\>`, %`\<speaker\>`, %`\<style\>`, %`\<text\>`, %`\<name\>` and the rest of their table (see [@feed tie-ins]).
- The http_handler: %`\<path\>`, %`\<body\>`, %`\<viewer\>`, %`\<headers\>` and %`\<hdr.<name>\>` for each header (see [http requests]).
- @chatformat and MOGRIFY\`FORMAT: %`\<type\>`, %`\<channel\>`, %`\<message\>`, %`\<name\>`, %`\<title\>`, %`\<default\>`, %`\<says\>`, %`\<options\>` (see [@chatformat], [mogrifier]).
- The other MOGRIFY\` attributes: the same names, and %`\<value\>` for the part being rewritten (see [mogrifier]).

## Arguments formats and queries name

- @pageformat and @outpageformat: %`\<message\>`, %`\<type\>`, %`\<alias\>`, %`\<recipients\>`, %`\<default\>` (see [@pageformat]).
- @speechmod: %`\<message\>`, %`\<type\>` (see [@speechmod]).
- @mailfilter: %`\<sender\>`, %`\<subject\>`, %`\<body\>`, %`\<flags\>` (see [@mailfilter]).
- Look formats: @nameformat's %`\<object\>` and %`\<name\>`, @descformat's and @idescformat's %`\<description\>`, @conformat's %`\<contents\>` and %`\<names\>`, @exitformat's %`\<exits\>` (see [@nameformat]).
- rendermarkdowncustom() templates: %`\<text\>`, %`\<url\>` and the other names in their list (see [rendermarkdowncustom()]).
- Regexp $-commands: each named capture.
- mapsql(): each column, by its name.

::: seealso
- [uargs()]
- [stepargs()]
- [@function]
- [@command]
- [@trigger]
- [@include]
- [r()]
- [substitutions]
:::
