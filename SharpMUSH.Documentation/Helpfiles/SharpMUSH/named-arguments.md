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
      "id": "naming-a-commands-arguments",
      "heading": "Naming a command's arguments",
      "lookup": "named arguments commands"
    },
    {
      "id": "arguments-the-game-names",
      "heading": "Arguments the game names",
      "lookup": "named arguments engine"
    }
  ]
}
-->
# Named Arguments

A named argument is an argument passed under a name instead of a position. Code reads it with %`\<name\>`, or with r(`<name>`, args), where it would read %0 with %0 or r(0, args).

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
| uargs(`<obj>/<attr>`, `<name>`, `<value>`, ...) | the pairs, and no %0-%9 |
| @trigger/args `<obj>/<attr>`=`<name>`,`<value>`,... | the pairs, and no %0-%9 |
| @include/args `<obj>/<attr>`=`<name>`,`<value>`,... | the pairs, beside the caller's own arguments |
| stepargs(`<obj>/<attr>`, `<list>`, `<names>`) | each run's elements under `<names>` |

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

## Naming a function's arguments

A global function made with @function gets its arguments as %0, %1 and so on. `@function/args <function>=<names>` also passes them under space-separated names, in order. Needs the config.admin permission. Redefining the function clears its names, so set them after it in the same @startup.

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

`@function/args` refuses a list with `#-1 ARGUMENT NAME INVALID` when a name is repeated or a number, when more than one name takes the rest, or when a name follows `items...` (there is never anything left over for it).

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
