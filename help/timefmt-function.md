<!-- help-article
{
  "corpus": "help",
  "id": "timefmt-function",
  "lookup": "timefmt()",
  "aliases": [
    "timefmt"
  ],
  "sections": [
    {
      "id": "date-escape-codes",
      "heading": "Date escape codes",
      "lookup": "timefmt date escape codes"
    }
  ],
  "redirects": {
    "TIMEFMT2": "timefmt date escape codes"
  }
}
-->
# timefmt()

`timefmt(<format>[, <secs>[, <timezone>]])`

This function returns the time and date, formatted according to `<format>`. `<secs>` is the time/date to format, as the number of seconds since the epoch (as returned by secs(), convtime(), etc). If no `<secs>` is given, the current date/time of the MUSH host is used. If no `<timezone>` is provided, the MUSH host's timezone is used; see [timezones] for valid formats for `<timezone>`. Note: Using a fractional timezone offset from GMT may result in timefmt() showing the time zone name (if displayed) as GMT. Using a symbolic name on a server that supports them should show the name correctly.

A list of all codes for `<format>` is in [timefmt date escape codes].

Example:
```sharp
think timefmt($A\, the $dth day of $B.)
Monday, the 17th day of July.
```


::: seealso
- [CONVSECS()]
- [etimefmt()]
- [timezones]
:::

## Date escape codes

All escape codes start with a $. To get a literal $, use $$. Invalid codes will return #-1 INVALID ESCAPE CODE. Other text will be passed through unchanged.

$a - Abbreviated weekday name  $p - AM/PM  ($P may also work)<br>
$A - Full weekday name         $S - Seconds after the minute<br>
$b - Abbreviated month name    $U - Week of the year from 1rst Sunday<br>
$B - Full month name           $w - Day of the week. 0 = Sunday<br>
$c - Date and time             $W - Week of the year from 1rst Monday<br>
$d - Day of the month          $x - Date<br>
$H - Hour of the 24-hour day   $X - Time<br>
$I - Hour of the 12-hour day   $y - Two-digit year<br>
$j - Day of the year           $Y - Four-digit year<br>
$m - Month of the year         $Z - Time zone<br>
$M - Minutes after the hour    $$ - $ character.
