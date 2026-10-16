<!-- help-article
{
  "corpus": "help",
  "id": "lock-evaluation-command",
  "lookup": "@lock-evaluation",
  "aliases": [],
  "sections": [
    {
      "id": "evaluation-lock",
      "heading": "Evaluation context",
      "lookup": "@lock-evaluation evaluation lock"
    },
    {
      "id": "evaluation-lock-example",
      "heading": "Evaluation lock example",
      "lookup": "@lock-evaluation evaluation lock example"
    }
  ],
  "redirects": {
    "@LOCK-EVAL2": "@lock-evaluation evaluation lock example",
    "@LOCK-EVALUATION2": "@lock-evaluation evaluation lock example"
  }
}
-->
# @lock-evaluation

An evaluation lock is set using this format:

`@lock <object>=<attribute>/<value>`

The difference between this and an attribute lock is that the *<attribute>* is taken from *<object>* rather than from the person trying to pass the lock. When someone tries, *<attribute>* is evaluated, and the result is compared to *<value>*. If it matches, then the person passes the lock.

## Evaluation context

The person trying to pass the lock is %# and *<object>* is %! when the evaluation takes place. The evaluation is done with the powers of *<object>*. If you try to do something (like `[get(%#/*<attribute>*)]`) and *<object>* doesn't have permission to do that, the person will automatically fail to pass the lock.


::: seealso
- [@lock-evaluation evaluation lock example]
:::

## Evaluation lock example

```sharp
@lock Thursday Cafe = whichday/Thu
&whichday Thursday Cafe = first(time())
```
This locks the object "Thursday Cafe" (probably an exit) unless today is Thursday.

Whenever someone tries to pass through the exit, the attribute "whichday" will be evaluated, extracting the first word returned from time() (the day of the week). The result is compared with the value in the lock ("Thu"), and the lock will only be passable when the strings match--Only on Thursdays.

If you have an evaluation lock that just does `[hasflag(%#,FLAGNAME)]`, you should probably use a bit lock instead.


::: seealso
- [@LOCK-BIT]
:::
