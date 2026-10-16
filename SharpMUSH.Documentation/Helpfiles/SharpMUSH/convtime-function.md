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

  This functions converts a time string to the number of seconds since Jan 1, 1970 GMT. A time string is of the format:<br>
      Ddd MMM DD HH:MM:SS YYYY<br>
  where Ddd is the day of the week, MMM is the month, DD is the day of the month, HH is the hour in 24-hour time, MM is the minutes, SS is the seconds, and YYYY is the year. If you supply an incorrectly formatted string, it will return #-1.

  convutctime() and convtime() with a second argument of 'utc' assume the timestring is based on UTC time. Other time zones can be specified too, such as America/New_York, with their summer time. If no `<timezone>` is given, the server's timezone is used.

- [convtime formats]: ISO dates, offsets, summer time gaps and the extended form
- [convtime examples]: converting time() output and a dated time

## Other date formats

  convtime() also reads other ways of writing a date and time, such as 2026-10-30 20:00, in the same zone. A time that names its own offset, such as 2026-10-30T20:00:00Z or 2026-10-30T20:00:00+02:00, keeps it. A time the zone skips when summer time starts, such as 2027-03-14 02:30 in America/New_York, returns #-1 INVALID TIME; written in the format above, it is moved on past the gap, as PennMUSH does.

  If the extended convtime() is supported (See @config compile), more formats for the date are enabled, including ones missing the day of week and year, and a 'Month Day Year' format. In this case, convtime() can also handle dates prior to 1970 (in which case a negative number will be returned).

## Examples

  Example:
```sharp
say time()
You say, "Wed Jun 24 10:22:54 1992"
```

    > say convtime(Wed Jun 24 10:22:54 1992)<br>
    You say, "709395774"

    > say convtime(2026-10-30 20:00,America/New_York)<br>
    You say, "1793404800"


::: seealso
- [CONVSECS()]
- [time()]
- [timezones]
:::
