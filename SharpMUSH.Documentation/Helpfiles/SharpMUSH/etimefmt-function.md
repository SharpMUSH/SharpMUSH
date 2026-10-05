<!-- help-article
{
  "corpus": "help",
  "id": "etimefmt-function",
  "lookup": "etimefmt()",
  "aliases": [
    "etimefmt"
  ],
  "sections": [
    {
      "id": "duration-escape-codes",
      "heading": "Duration escape codes",
      "lookup": "etimefmt duration escape codes"
    },
    {
      "id": "duration-examples",
      "heading": "Duration examples",
      "lookup": "etimefmt duration examples"
    }
  ],
  "redirects": {
    "ETIMEFMT2": "etimefmt duration escape codes",
    "ETIMEFMT3": "etimefmt duration examples"
  }
}
-->
# etimefmt()

`etimefmt(<format>, <secs>[, <precision>])`

This function is similar to timestring() - it formats a number of seconds into days, hours, minutes and seconds. However, its formatting is much more versatile than timestring(), as well as being more complex.

Escape codes in `<format>` are replaced by the proper values, and other characters are left unchanged.

A list of all codes is in [etimefmt duration escape codes].

Examples:
```sharp
say etimefmt(I have been connected for $2H:$2M., conn(%#))
You say, "I have been connected for 01:32."
think etimefmt($2mm $2ss, 500) - [timestring(500)]
8m 20s -  8m 20s
```


::: seealso
- [TIMESTRING()]
- [timefmt()]
- [ETIME()]
:::

## Duration escape codes

etimefmt()'s escape codes are similar to timefmt()'s. The time is broken up into days, hours, minutes, and seconds, and each value replaces the matching code.

$s - The number of seconds.    $h - The number of hours.<br>
$m - The number of minutes.    $d - The number of days.<br>
$w - The number of weeks.      $y - The number of 365-day years.<br>
$$ - A literal $.

You can also put a number between the $ and letter to specify a minimum width for the expanded code. The string is padded with spaces by default - use uppercase to pad with 0s instead ($3S, rather than $3s). An 'x' before the code (but after any number) will automatically add a d, h, m, or s suffix to the time, and a 'z' will not display anything if the field's value is 0. x and z can be combined.

Normally, a particular time interval is shown using the remainder of the next largest interval - for example, $s with a time of 65 displays 5, not 65. The exception is $d, which only acts like this if $w or $y is also given. A 't' between the $ and code (But after a width) will print out the total seconds, minutes, etc. instead.

## Duration examples

Examples:
```sharp
think etimefmt($2h:$2M, 3700)
1:01
think etimefmt(You have $m minutes and $s seconds to go, 78)
You have 1 minutes and 18 seconds to go
think squish(etimefmt(Connected for $zxd $xzh $zxm $xzs, conn(me)))
Connected for 5h 24m 45s
think etimefmt($txs is $xm$xs, 75)
75s is 1m15s
```
