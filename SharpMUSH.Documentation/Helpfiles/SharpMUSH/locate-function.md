<!-- help-article
{
  "corpus": "help",
  "id": "locate-function",
  "lookup": "locate()",
  "aliases": [
    "locate"
  ],
  "sections": [
    {
      "id": "search-locations",
      "heading": "Search locations",
      "lookup": "locate search locations"
    },
    {
      "id": "matching-examples",
      "heading": "Matching examples",
      "lookup": "locate matching examples"
    }
  ],
  "redirects": {
    "LOCATE2": "locate search locations",
    "LOCATE3": "locate matching examples"
  }
}
-->
# locate()

`locate(<looker>, <name>, <parameters>)`

This function attempts to find an object called `<name>`, relative to the object `<looker>`. It's similar to the num() function, but you can be more specific about which type of object to find, and where to look for it. When attempting to match objects near to `<looker>` (anything but absolute, player name or "me" matches), you must control `<looker>`, have the See_All power or be nearby.

`<parameters>` is a string of characters which control the type of the object to find, and where (relative to `<looker>`) to look for it.

You can control the preferred types of the match with:<br>
    N - No type (this is the default)<br>
    E - Exits<br>
    L - Prefer an object whose Basic @lock `<looker>` passes<br>
    P - Players<br>
    R - Rooms<br>
    T - Things<br>
    F - Return #-1 if what's found is of a different type than the preferred one.<br>
    X - Never return #-2. Use the last dbref found if the match is ambiguous.

If type(s) are given, locate() will attempt to find an object with one of the given types first. If none are found, it will attempt to find any type of object, unless 'F' is specified, in which case it will return #-1.

## Search locations

You can control where to look with:<br>
    a - Absolute match (match `<name>` against any dbref)<br>
    c - Exits in the room `<looker>`<br>
    e - Exits in `<looker>`'s location<br>
    h - If `<name>` is "here", return `<looker>`'s location<br>
    i - Match `<name>` against the names of objects in `<looker>`'s inventory<br>
    l - Match `<name>` against the name of `<looker>`'s location<br>
    m - If `<name>` is "me", return `<looker>`'s dbref<br>
    n - Match `<name>` against the names of objects in `<looker>`'s location<br>
    p - If `<name>` begins with a *, match the rest against player names<br>
    z - English-style matching (my 2nd book) of `<name>` (see [MATCHING])<br>
    * - All of the above (try a complete match). Default when no match parameters are given.<br>
    y - Match `<name>` against player names whether it begins with a * or not<br>
    x - Only match objects with the exact name `<name>`, no partial matches<br>
    s - Only match objects which `<looker>` controls. You must control `<looker>` or have the See_All power.

Just string all the parameters together. Spaces are ignored, so you can use spaces between paramaters for clarity if you wish.

::: seealso
- [NUM()]
- [RNUM()]
- [PMATCH()]
- [ROOM()]
- [WHERE()]
- [RLOC()]
- [FINDABLE()]
:::

## Matching examples

Examples:
```sharp
Find the dbref of the player whose name matches %0, or %#'s dbref if %0 is "me".
think locate(%#, %0, PFym)
'PF' matches objects of type 'player' and nothing else, 'm' checks for the string "me", and 'y' matches the names of players.
```

Find the dbref of an object near %# called %0, including %# himself and his location. Prefer players or things, but accept rooms or exits if no players or things are found.<br>
```sharp
> think locate(%#, %0, PThmlni)
This prefers 'P'layers or 'T'hings, and compares %0 against the strings "here" and "me", and the names of %#'s location, his neighbours, and his inventory.
```
