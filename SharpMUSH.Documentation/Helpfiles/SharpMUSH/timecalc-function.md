<!-- help-article
{
  "corpus": "help",
  "id": "timecalc-function",
  "lookup": "timecalc()",
  "aliases": [
    "SECSCALC()"
  ],
  "sections": [
    {
      "id": "time-modifiers",
      "heading": "Time modifiers",
      "lookup": "timecalc time modifiers"
    }
  ],
  "redirects": {
    "TIMECALC2": "timecalc time modifiers"
  }
}
-->
# timecalc()

`timecalc(<timestring>[, <modifier>, ... ])`<br>
`secscalc(<timestring>[, <modifier>, ... ])`


Takes a time and returns the resulting time after applying any modifiers. timecalc() returns a time is the same format as time(), and secscalc() as the seconds since the epoch. These functions can deal with a much broader range of times than the other time functions.

`<timestring>` can be in the following formats:

YYYY-MM-DD<br>
YYYY-MM-DD HH:MM<br>
YYYY-MM-DD HH:MM:SS<br>
YYYY-MM-DD HH:MM:SS.SSS<br>
HH:MM<br>
HH:MM:SS<br>
HH:MM:SS.SSS<br>
now (Current time in UTC)<br>
DDDDDDDDDD (Julian day, or seconds if followed by a unixepoch modifier)

## Time modifiers

`<modifier>`s can be in the following formats:

NNN days<br>
NNN hours<br>
NNN minutes<br>
NNN.NNNN seconds<br>
NNN months<br>
NNN years<br>
start of month<br>
start of year<br>
start of day<br>
weekday N<br>
unixepoch<br>
localtime (Converts a UTC time to local time)<br>
utc (Converts a local time to UTC)

For details about what these formats and modifers mean, see https://www.sqlite.org/lang_datefunc.html

Examples:
```sharp
think timecalc(now, +100 years, localtime)
Mon May 09 03:57:31 2118
think timecalc(secs(), unixepoch)
Wed May 09 12:19:21 2018
```
