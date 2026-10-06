<!-- help-article
{
  "corpus": "help",
  "id": "time-function",
  "lookup": "time()",
  "aliases": [
    "UTCTIME()",
    "time"
  ],
  "sections": [
    {
      "id": "timezone-examples",
      "heading": "Timezone examples",
      "lookup": "time timezone examples"
    }
  ],
  "redirects": {
    "TIME2": "time timezone examples"
  }
}
-->
# time()

`time()`<br>
`time(<timezone>)`<br>
`time(<dbref>)`

time() gives you the current time on the MUSH. By default this is the time on the server the MUSH is running on, and not the time of the caller.

With an argument, time() returns the time in the specified timezone, or in the timezone set in the specified object's TZ attribute; for more information, see [timezones].

utctime() is an alias for time(utc).

## Timezone examples

Examples (Assuming the server is the USA's Pacific timezone):

```sharp
> think utctime()
Fri Mar 02 03:19:54 2012
> think time(utc)
Fri Mar 02 03:19:54 2012
> think time()
Thu Mar 01 19:19:54 2012
> think time(-8)
Thu Mar 01 19:20:25 2012
> think time(US/Pacific)
Thu Mar 01 19:20:25 2012
```


::: seealso
- [timefmt()]
- [TIMESTRING()]
- [CONVSECS()]
- [CONVTIME()]
- [timezones]
:::
