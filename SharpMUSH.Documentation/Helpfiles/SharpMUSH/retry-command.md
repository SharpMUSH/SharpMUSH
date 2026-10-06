<!-- help-article
{
  "corpus": "help",
  "id": "retry-command",
  "lookup": "@retry",
  "aliases": [],
  "sections": [
    {
      "id": "retry-examples",
      "heading": "Retry examples",
      "lookup": "@retry examples"
    }
  ],
  "redirects": {
    "@retry2": "@retry examples"
  }
}
-->
# @retry

`@retry <boolean>`<br>
`@retry <boolean>=<arg0>[,...[,<argN>]]`

The @retry command restarts the current queue entry, enabling people to loop their command without requiring a wait for the next queue entry. It can be a little tricky to understand at first. It basically tells the parser: "If `<boolean>` is true, then go back to the beginning." It can also replace %0-%9 with the arguments passed to it. (`<arg0>`,...).

Output: the output of the last command run. See [command output].

Please note: @retry only restarts the action list it is currently in. If you have: "`@break 1=@retry 1=hello`", then the action list is only "`@retry 1=hello`" - which would thus keep retrying until a limit intervenes.

Each `@retry` invocation has an independent hard cap of 1,000 retry iterations. If the condition remains true, it stops at that cap without a separate limit notification. Execution budgets and function-invocation limits can stop evaluation sooner. Write a terminating condition rather than relying on the cap.


::: seealso
- [action lists]
- [boolean values]
- [@break]
- [@include]
:::

## Retry examples

### Example: 'while'
```sharp
> &sing me=$sing *:say %0 bottles of beer! ; @retry gt(%0,0)=dec(%0) ; say Go get some more!
> sing 3
You say, "3 bottles of beer!"
You say, "2 bottles of beer!"
You say, "1 bottles of beer!"
You say, "0 bottles of beer!"
You say, "Go get some more!"
```

Implementing a folding algorithm:<br>
(Yes, I know lmath is better, but this is just an example! :D)
```sharp
> &add me=$add *:@retry words(%0)=rest(%0),add(first(%0),0%1) ; think %1
> add 4 3 2 1
10
```
