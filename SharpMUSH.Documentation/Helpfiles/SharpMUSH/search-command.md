<!-- help-article
{
  "corpus": "help",
  "id": "search-command",
  "lookup": "@search",
  "aliases": [],
  "sections": [
    {
      "id": "flags-and-powers",
      "heading": "Flags and powers",
      "lookup": "@search flags and powers"
    },
    {
      "id": "dbref-ranges-and-evaluation",
      "heading": "Dbref ranges and evaluation",
      "lookup": "@search dbref ranges and evaluation"
    },
    {
      "id": "search-permissions",
      "heading": "Search permissions",
      "lookup": "@search permissions"
    },
    {
      "id": "object-search-examples",
      "heading": "Object search examples",
      "lookup": "@search object search examples"
    }
  ],
  "redirects": {
    "@search2": "@search flags and powers",
    "@search3": "@search dbref ranges and evaluation",
    "@search4": "@search permissions",
    "@search5": "@search object search examples"
  }
}
-->
# @search

`@search [<player>] [<classN>=<restrictionN>[,...]][,<begin>,<end>]`

This command searches the database and lists objects which meet user specified search criteria. You can limit the scope of the search by specifying `<begin>` and `<end>` as the first and last dbrefs to search.

Output: the dbrefs found, as `lsearch()` returns them.

If a `<player>` argument is supplied, only objects owned by that player will be listed, or all objects if "all" is used. Mortals attempting to match other players (aside from ZMPs whose @lock/zone they pass) or "all" will only get objects which they can examine.

`<class>` and `<restriction>` arguments can be given to filter the match results. Possible `<class>`es include TYPE, NAME, ZONE, PARENT, EXITS, THINGS (or OBJECTS), ROOMS, PLAYERS, FLAGS, LFLAGS, POWERS, ELOCK, COMMAND, LISTEN, EVAL, EPLAYER, EROOM, EEXIT, and ETHING (or EOBJECT).

If `<class>`=TYPE, possible `<restriction>`s include THING (or OBJECT), ROOM, EXIT, PLAYER, GARBAGE. This shows all objects of the specified type.

If `<class>`=NAME, only objects whose name begin with the string `<restriction>` will be listed. If `<class>`=EXITS, OBJECTS, ROOMS or PLAYERS, only objects of that type whose name begins with `<restriction>` are listed.

If `<class>`=ZONE, only objects in the zone `<restriction>` will be listed.<br>
If `<class>`=PARENT, only children of parent `<restriction>` will be listed.<br>
For ZONE and PARENT, `<restriction>` must be specified as a dbref number.

## Flags and powers

If `<class>`=FLAGS or LFLAGS, only objects with the list of flags specified by `<restriction>` will be listed. For FLAGS, flags to match should be given as a string of single flag letters, with appropriate case. For LFLAGS, flags to match should be given as a space-separated list of flag names.

If `<class>`=POWERS, only objects with the given powers are listed. `<restriction>` should be a space-separated list of power names.

If `<class>`=ELOCK, only objects that pass the given lock string (as in help @lock) are listed. For purposes of indirect locks (@#123), 'search' is the name of the lock.

If `<class>`=EVAL, only objects for which `<restriction>` evaluates to a true boolean value will be listed. The token '##' in `<restriction>`, which is a function, is replaced by each dbref sequentially. Classes EPLAYER, EROOM, EEXIT, and ETHING work like EVAL but are restricted to a single type.

## Dbref ranges and evaluation

If `<class>`=MINDB, only objects with dbrefs of `<restriction>` or higher will be listed. If `<class>`=MAXDB, only objects with dbrefs of `<restriction>` or lower will be listed.

If `<class>`=START, then @search will start returning results at the `<restriction>`th result. The first result is 1.

If `<class>`=COUNT, then @search will only return up to `<restriction>` results.

A START or COUNT below 1 is an error ("Invalid start index" / "Invalid count index"); nothing is searched, and lsearch() returns #-1.

If `<class>`=COMMAND, then @search will only return objects that respond to `<restriction>` as an $-command.

If `<class>`=LISTEN, then @search will only return objects that respond to `<restriction>` through a listen.

## Search permissions

For the class TYPE=PLAYER, and for PLAYER=`<player-name>`, anyone may obtain information on any player. In all other cases, wizards may obtain information about other players, and players who pass a ZMP's zone-lock may obtain information about the ZMP.

If multiple `<class>` and `<restrictions>` are given, objects must meet all criteria in order to match successfully. The exception to this is that if multiple 'type' searches (PLAYER, EROOM, etc) are used, only the last type given is used in the search.

@search is only mildly computationally expensive for most of the search classes. Computationally expensive searches are the evaluating searches (EVAL, EPLAYER, ETHING, EROOM, EEXIT), the attribute pattern searches (COMMAND, LISTEN), and ELOCK searches which perform evaluation searches (attr/value) or indirect locks (@obj/lock). These searches all cost a number of pennies (the exact amount is configurable; see @config find_cost).

::: seealso
- [lsearch()]
- [@find]
:::

## Object search examples

### Examples
```sharp
@search all type=player,flags=W <-- list all Wizard players
@search type=room <-- list all rooms owned by me.
@search zone=#50 <-- list all objects belong to zone #50.
@search Joe eval=1,100,200 <-- list objects from #100-#200 owned by Joe.
@search eval=gt(money(##),10) <-- list all objects owned by me worth more than 10 coins.
@search all elock=FLAG^WIZARD|FLAG^ROYALTY <-- list all objects with wizard or royalty flags.
@search wizard_bc command=+who <-- Forgot what object has your +who?
```
