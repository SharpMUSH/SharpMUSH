<!-- help-article
{
  "corpus": "help",
  "id": "cache-function",
  "lookup": "cache()",
  "aliases": [
    "cache"
  ],
  "sections": [
    {
      "id": "cache-windows",
      "heading": "Cache windows",
      "lookup": "cache windows"
    },
    {
      "id": "cache-clearing",
      "heading": "Clearing a cache",
      "lookup": "cache clearing"
    },
    {
      "id": "cache-examples",
      "heading": "Cache examples",
      "lookup": "cache examples"
    }
  ]
}
-->
# cache()

`cache(<object>, <attribute>, <value>[, <window>[, <sliding?>]])`

cache() returns `<object>`/`<attribute>` while it was set less than `<window>` seconds ago. Otherwise it evaluates `<value>`, sets the attribute to the result and returns it. `<value>` is only evaluated when the attribute is missing or too old, so slow code whose answer changes rarely, such as a long lsearch() or a sql() query, runs once a window instead of on every call.

You must control `<object>`, or cache() returns #-1 NO PERMISSION TO SET ATTRIBUTE without evaluating `<value>`. Nothing is printed. Where side-effect functions are off, cache() returns #-1 FUNCTION DISABLED.

- [cache windows]: how old a stored answer may be, and sliding windows
- [cache clearing]: making the next call evaluate, by hand or when data changes
- [cache examples]

::: seealso
- [memo()]
- [mtime()]
- [lsearch()]
- [sql()]
:::

## Cache windows

`<window>` is in seconds, and 3600 (one hour) when left out. 0 always evaluates. A `<window>` that is not a whole number returns #-1 ARGUMENT MUST BE INTEGER, and a negative one #-1 ARGUMENT MUST BE POSITIVE INTEGER.

The attribute holds only the value. Its age is its own modification time, which mtime() and msecs() read from `<object>/<attribute>`.

If `<sliding?>` is true, returning the stored value also sets it again, which starts its window over. An answer read at least once a window then never goes stale; one nobody reads for a whole window is evaluated again on the next call.

```sharp
> think cache(me,CACHE`THINGS,words(lsearch(all,type,thing)),300,1)
40
```

Output: the number of things. Called again within five minutes, it returns 40 without searching and keeps it for five minutes more from then.

A value that cannot be stored, such as one too long, is still returned, and the next call evaluates again.

## Clearing a cache

Clear the attribute, with `&<attribute> <object>`, attrib_set() or wipe(), and the next call evaluates `<value>` again. Code that changes what an answer is built from can clear it at the same time.

When that data is a standard attribute, @attribute/watch can clear it instead: the event handler runs its watch attributes whenever any copy of the attribute changes, on any object, by any command or function. The watch is queued after the command that made the change, so a read later in the same command list can still get the old answer; clear the attribute there instead. See [@attribute watch] and the second of the [cache examples].

## Cache examples

```sharp
> think cache(me,CACHE`ROOMS,words(lsearch(all,type,room)),600)
12
> @dig Annex
> think cache(me,CACHE`ROOMS,words(lsearch(all,type,room)),600)
12
```

Output: 12 both times. The second call read CACHE\`ROOMS instead of searching, so Annex is not counted until the 600 seconds have passed.

```sharp
> @attribute/access LEVEL=none
> @attribute/watch LEVEL
> &WATCH`LEVEL`CACHE #9=&CACHE`LEVELS #9
> &LEVEL me=5
> think cache(#9,CACHE`LEVELS,ladd(iter(lsearch(all,type,player),get(##/LEVEL))),86400)
5
> &LEVEL me=7
> think cache(#9,CACHE`LEVELS,ladd(iter(lsearch(all,type,player),get(##/LEVEL))),86400)
7
```

Output: 7. The total is kept for a day, but setting LEVEL ran the watch on the event handler (#9), which cleared CACHE\`LEVELS, so the second call added the levels up again.
