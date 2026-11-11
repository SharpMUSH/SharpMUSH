<!-- help-article
{
  "corpus": "help",
  "id": "exptime-function",
  "lookup": "exptime()",
  "aliases": [
    "WRITETIME()",
    "SINGLETIME()",
    "DIGITTIME()",
    "exptime",
    "writetime",
    "singletime",
    "digittime"
  ],
  "sections": []
}
-->
# exptime()

`exptime(<seconds>)`<br>
`writetime(<seconds>)`<br>
`singletime(<seconds>)`<br>
`digittime(<seconds>)`

These write a number of seconds as a length of time.

- exptime() gives each unit that is not zero by its letter: `y` years, `M` months, `w` weeks, `d` days, `h` hours, `m` minutes, `s` seconds. The seconds are always given.
- writetime() gives the same units by name: `3 hours 25 minutes 45 seconds`.
- singletime() gives only the largest unit, rounded down.
- digittime() gives hours and minutes as `HH:MM`, after the number of days when there are any.

A year is 31,556,926 seconds and a month 2,629,743, their average lengths. *<seconds>* is read up to the first character that is not a digit, and a negative number counts as 0.

These functions come from TinyMUX.

```sharp
> think exptime(12345) / [writetime(123456)]
3h 25m 45s / 1 day 10 hours 17 minutes 36 seconds
> think [singletime(12345)] / [digittime(123456)] / [exptime(3600)]
3h / 1d 10:17 / 1h 0s
```

::: seealso
- [ETIMEFMT()]
- [TIMESTRING()]
- [CONVSECS()]
:::
