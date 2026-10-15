<!-- help-article
{
  "corpus": "help",
  "id": "stopevent-function",
  "lookup": "stopevent()",
  "aliases": [
    "stopevent"
  ],
  "sections": []
}
-->
# stopevent()

`STOPEVENT()`

Called from an event handler attribute of a stoppable event, keeps the attributes after it from running. The attribute that calls it runs to the end. It returns nothing.

An event runs the handler's attribute named after it, then each attribute one level below it in name order (see [events]). On a stoppable event they run in turn, so a package whose attribute comes first can claim the event. [event list] marks the stoppable events.

Called anywhere else, including from an event that is not stoppable, it returns `#-1 NOT HEARING A STOPPABLE EVENT` and stops nothing.

```sharp
> &SOCKET`GMCP`A #9=think [if(strmatch(%<package>,Core.Hello),stopevent())]
> &SOCKET`GMCP`B #9=@pemit #1=GMCP: %<package>
> think stopevent()
#-1 NOT HEARING A STOPPABLE EVENT
```

A client's Core.Hello now reaches only SOCKET\`GMCP\`A; every other package reaches both.

::: seealso
- [events]
- [event socket]
:::
