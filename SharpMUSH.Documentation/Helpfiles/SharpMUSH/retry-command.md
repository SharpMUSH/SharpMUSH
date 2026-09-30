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

Please note: @retry only restarts the action list it is currently in. If you have: "`@break 1=@retry 1=hello`", then the action list is only "`@retry 1=hello`" - which would thus create an infinite loop.

Watch out for infinite loops! @retry does respect all the limits (cpu_limit, function_invocation_limit, etc). But because @retry causes the queue parser to repeat itself _without_ invoking a new function, it doesn't risk hitting any issues other than infinite loops.


**See Also:**
- [action lists]
- [boolean values]
- [@break]
- [@include]

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
