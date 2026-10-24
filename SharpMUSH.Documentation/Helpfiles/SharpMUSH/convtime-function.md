<!-- help-article
{
  "corpus": "help",
  "id": "convtime-function",
  "lookup": "convtime()",
  "aliases": ["convutctime()"],
  "sections": [
    {"id": "formats", "heading": "Other date formats", "lookup": "convtime formats"},
    {"id": "examples", "heading": "Examples", "lookup": "convtime examples"}
  ]
}
-->
# convtime()

`convtime(<time string>[, <timezone>[, <precision>]])`<br>
`convutctime(<time string>[, <precision>])`

convtime() converts a time string to the number of seconds since 1 January 1970 UTC. The time string is written the way time() writes it:

`Ddd MMM DD HH:MM:SS YYYY`

Ddd is the day of the week, MMM the month, DD the day of the month, HH the hour on a 24-hour clock, MM the minutes, SS the seconds and YYYY the year. A string convtime() cannot read returns `#-1 INVALID TIME`.

- `<timezone>`: the zone the time is in, such as `America/New_York`, with its summer time. `utc` reads it as UTC. With none, the server's zone is used. convutctime() is convtime() with `utc`.
- `<precision>`: `s` or `seconds` (the default) returns whole seconds, `f` or `fractional` seconds with a fraction, and `ms` or `milliseconds` milliseconds.

- [convtime formats]: ISO dates, offsets, summer time gaps and dates before 1970
- [convtime examples]: converting time() output and a dated time

## Other date formats

convtime() also reads other ways of writing a date and time, such as `2026-10-30 20:00`, in the same zone. A time that names its own offset, such as `2026-10-30T20:00:00Z` or `2026-10-30T20:00:00+02:00`, keeps it.

A time the zone skips when summer time starts, such as `2027-03-14 02:30` in America/New_York, returns `#-1 INVALID TIME`. Written in time()'s format, it is moved on past the gap, as PennMUSH does: `Sun Mar 14 02:30:00 2027` reads as 03:30.

Dates before 1970 return a negative number.

## Examples

```sharp
> think convtime(Wed Jun 24 10:22:54 1992,utc)
709381374
> think convtime(2026-10-30 20:00,America/New_York)
1793404800
> think convtime(2026-10-30T20:00:00.25Z,,f)
1793390400.25
> think convtime(Fri Jan 01 00:00:00 1960,utc)
-315619200
```

::: seealso
- [CONVSECS()]
- [time()]
- [dateparse()]
- [timezones]
:::
