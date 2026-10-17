<!-- help-article
{
  "corpus": "help",
  "id": "dateparse-function",
  "lookup": "dateparse()",
  "aliases": ["dateparse"],
  "sections": [
    {"id": "policies", "heading": "Calendar and ambiguity policies", "lookup": "dateparse policies"},
    {"id": "examples", "heading": "Examples", "lookup": "dateparse examples"}
  ]
}
-->
# dateparse()

`dateparse(<expression>[, <timezone>[, <reference>[, <output>]]])`

Reads one complete English date expression and returns epoch seconds. `output` is `s` (whole seconds, default), `f` (fractional seconds), or `ms` (milliseconds). Use `normalized` to return the resolved local date, exact clock, numeric UTC offset and zone instead of an epoch value. This preserves a zone explicitly written in the expression for confirmations. Numeric expression and reference values always mean epoch **seconds**, irrespective of output precision. `reference` defaults to the instant of the call; provide it for repeatable calculations. The zone defaults to the executor's readable, valid `TZ`, then UTC. A zone in the expression overrides it. Use canonical IANA names such as `America/Chicago`, `UTC`, `Z`, or a numeric offset such as `-05:00`.

Supported families include `tomorrow 7pm`, `the day after tomorrow at noon`, `Tuesday 18:30`, `next week Tuesday 18:30`, `in 90 minutes`, `+1h30m`, `in 7 days at 18:30`, `two weeks from now at 7pm`, `October 23 at 7pm`, `23 Oct 2026 19:00`, ISO timestamps, epoch seconds, `a week from Tuesday at 7pm`, `first Saturday of next month at noon`, and `last Friday of November at 18:00`. English month and weekday names accept usual three-letter forms. English word case and repeated spaces are ignored; IANA names retain their canonical spelling, and an optional `at` precedes clocks.

Input is limited to 512 characters and must be consumed completely. Refusal returns `#-1 INCOMPLETE DATE: ...`, `#-1 AMBIGUOUS DATE: ...`, `#-1 UNSUPPORTED DATE: ...`, or `#-1 INVALID DATE: ...` with guidance. `convtime()` retains its existing compatibility behavior.

## Calendar and ambiguity policies

A bare weekday means its next occurrence, including today if its clock has not passed. `this Tuesday` means Tuesday of the current Monday-based week; `next Tuesday` is strictly later than today; `next week Tuesday` means Tuesday of the following Monday-based week. Missing years use the reference year. `a week from Tuesday` anchors that bare weekday and then adds seven calendar days. A relative month with an explicit year (`next month 2027`) is contradictory; use a month name and year. Date-only inputs require a clock. A clock alone uses today, even when past. General parsing permits past instants; scene scheduling refuses them.

Days, weeks and fortnights advance the local calendar and retain the reference clock unless another clock is supplied. Hours, minutes and seconds are elapsed time. For mixed quantities, calendar adjustments happen before elapsed adjustments. A clock combined with only an elapsed offset is refused as contradictory. Months and years are not offset units; give a named date or an ordinal weekday instead.

DST gaps and repeated clocks are refused; give another clock or an ISO timestamp with an explicit offset. If a numeric offset and named zone are both written, they must agree. `2026-11-01T01:30:00-04:00` and `2026-11-01T01:30:00-05:00` select different instants. Numeric dates such as `03/04`, zone abbreviations such as `CST`, vague dayparts such as `tonight` or `tomorrow evening`, ranges, and recurrences are not guessed.

## Examples

At reference `1792152000` (2026-10-16 12:00 UTC):

```sharp
dateparse(tomorrow 7pm,America/Chicago,1792152000)
1792281600

dateparse(+1h30m,UTC,1792152000,ms)
1792157400000

dateparse(next Tuesday,UTC,1792152000)
#-1 INCOMPLETE DATE: CLOCK TIME REQUIRED; ADD A TIME SUCH AS 19:00 OR NOON
```

See also [convtime()] and [timezones].

```
dateparse(tomorrow 7pm America/Chicago,UTC,1792152000,normalized)
2026-10-17 19:00:00 -05:00 (America/Chicago)
```
