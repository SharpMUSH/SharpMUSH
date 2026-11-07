<!-- help-article
{
  "corpus": "help",
  "id": "rollback-command",
  "lookup": "@rollback",
  "aliases": [],
  "sections": []
}
-->
# @rollback

`@rollback <count>[/<repeats>][=<arg0>[,...[,<argN>]]]`<br>
`@rollback/retry <boolean>[=<arg0>[,...[,<argN>]]]`

@rollback runs the commands before it in the same action list again. `<count>` is how many of them, counting back from the @rollback, and `<repeats>` how many times (1 unless given). A count of 0, or one with nothing before it, does nothing; a count larger than the list takes the whole list.

@rollback/retry runs every command before it again while `<boolean>` is true.

The arguments become %0-%9 for each run, evaluated before every run with the %0-%9 of the run before. The list then goes on with its own %0-%9. One @rollback runs at most 1,000 times. The RhostMUSH switches /wait and /label are not supported.

```sharp
> &foo me=$foo *:think 1-%0;think 2-%0;think 3-%0;@rollback 2/2=[add(%0,5)];think end-%0
> foo 5
1-5
2-5
3-5
2-10
3-10
2-15
3-15
end-5
```

::: seealso
- [@retry]
- [action lists]
:::
