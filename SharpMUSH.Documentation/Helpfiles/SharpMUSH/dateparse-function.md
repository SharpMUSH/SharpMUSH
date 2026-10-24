<!-- help-article
{
  "corpus": "help",
  "id": "dateparse-function",
  "lookup": "dateparse()",
  "aliases": ["dateparse"],
  "sections": [
    {"id": "forms", "heading": "What it understands", "lookup": "dateparse forms"},
    {"id": "policies", "heading": "How dates are read", "lookup": "dateparse policies"},
    {"id": "examples", "heading": "Examples", "lookup": "dateparse examples"}
  ]
}
-->
# dateparse()

`dateparse(<expression>[, <timezone>[, <reference>[, <output>]]])`

dateparse() reads a date and time written in English, such as `tomorrow 7pm`, and returns it in seconds since 1970.

- `<expression>`: the date and time, at most 512 characters, all of which must be understood. It may name its own zone, which wins over `<timezone>`.
- `<timezone>`: the zone the expression is read in, as a tz database name such as `America/Chicago`, `UTC`, `Z`, or an offset such as `-05:00`. With none, it is the zone in your `TZ` attribute, or UTC.
- `<reference>`: the moment, in seconds, that words such as `tomorrow` count from. It is now unless given; give it to get the same answer every time.
- `<output>`: `s` (whole seconds, the default), `f` (seconds with a fraction), `ms` (milliseconds), or `normalized`: the date, time, offset and zone it read, such as `2026-10-17 19:00:00 -05:00 (America/Chicago)`, to show a player what was understood.

A number given as the expression or the reference is always seconds, whatever `<output>` says.

- [dateparse forms]: the expressions it reads, and its errors
- [dateparse policies]: weekdays, offsets, summer time and what is never guessed
- [dateparse examples]: tomorrow, offsets, the normalized form and a refusal

## What it understands

dateparse() understands, among others, `tomorrow 7pm`, `the day after tomorrow at noon`, `Tuesday 18:30`, `next week Tuesday 18:30`, `in 90 minutes`, `+1h30m`, `in 7 days at 18:30`, `two weeks from now at 7pm`, `October 23 at 7pm`, `23 Oct 2026 19:00`, ISO timestamps, a number of seconds, `a week from Tuesday at 7pm`, `first Saturday of next month at noon` and `last Friday of November at 18:00`. Month and weekday names can be cut to three letters, case and extra spaces do not matter, and `at` before a time is optional.

What it cannot read returns `#-1 INCOMPLETE DATE: ...`, `#-1 AMBIGUOUS DATE: ...`, `#-1 UNSUPPORTED DATE: ...` or `#-1 INVALID DATE: ...`, followed by what to change. convtime() reads dates its own way, as PennMUSH's does.

## How dates are read

A weekday on its own is the next one, today included if its time has not passed yet. `this Tuesday` is the Tuesday of this week, counted from Monday; `next Tuesday` is the next one after today; `next week Tuesday` is the Tuesday of next week. A date with no year is in the reference's year. `a week from Tuesday` is the coming Tuesday, then seven days on. `next month 2027` contradicts itself: name the month and year instead.

A date needs a time. A time alone is today, even when it has passed. dateparse() itself accepts a past moment; `+scene/schedule` refuses one.

Days, weeks and fortnights move along the calendar and keep the reference's time of day unless another is given; hours, minutes and seconds are counted as elapsed time. When both are given, the calendar moves first. A time together with only an hours-or-minutes offset is refused. Months and years cannot be offsets: name the date, or a weekday such as `first Saturday of next month`.

A time that summer time skips or repeats is refused: give another time, or an ISO timestamp with its offset, since `2026-11-01T01:30:00-04:00` and `2026-11-01T01:30:00-05:00` are different moments. When an expression gives both an offset and a zone, they must agree. dateparse() never guesses at numeric dates such as `03/04`, zone abbreviations such as `CST`, vague times such as `tonight` or `tomorrow evening`, ranges, or repeats.

## Examples

With the reference `1792152000`, which is 16 October 2026 at 12:00 UTC:

```sharp
> think dateparse(tomorrow 7pm,America/Chicago,1792152000)
1792281600
> think dateparse(+1h30m,UTC,1792152000,ms)
1792157400000
> think dateparse(tomorrow 7pm America/Chicago,UTC,1792152000,normalized)
2026-10-17 19:00:00 -05:00 (America/Chicago)
> think dateparse(next Tuesday,UTC,1792152000)
#-1 INCOMPLETE DATE: CLOCK TIME REQUIRED; ADD A TIME SUCH AS 19:00 OR NOON
```

::: seealso
- [convtime()]
- [timezones]
:::
