<!-- help-article
{
  "corpus": "help",
  "id": "pid-function",
  "lookup": "pid()",
  "aliases": [
    "pid"
  ],
  "sections": [
    {
      "id": "pid-types",
      "heading": "pid() types",
      "lookup": "pid types"
    }
  ]
}
-->
# pid()

`pid([<display>[, <separator>[, <type>[, <target>[, <owned>]]]]])`

Returns queue entries, separated by `<separator>` (a space if you give none). With no `<target>`, it returns the entries you may see: those run by you or by anything your owner owns, or every entry if you are a wizard or royalty or have the See_Queue power.

`<display>` is a sum of the fields to return for each entry, joined by `|` in this order:

- 1: the entry's pid. This is what a `<display>` of 0 or none returns.
- 2: the object that runs the entry.
- 4: the object that caused it.
- 8: the seconds until it runs, or 0 if it has no time, such as a semaphore with no timeout.
- 16: `STOPPED` if the entry is paused, otherwise `RUNNING`.
- 32: the commands the entry runs.

Two values stand alone and return only one thing:

- 64: the pid of the queue entry running now, or `#-1` if none is.
- 128: the commands of the queue entry running now, if it was queued by @wait.

See [pid types] for `<type>`, `<target>`, `<owned>` and examples.

This function comes from RhostMUSH.

## pid() types

`<type>` chooses the entries:

- q: waiting entries, then semaphore entries. This is the default.
- w: entries waiting for a time, soonest first.
- s: entries waiting on a semaphore.
- p: entries about to run whose object is a player.
- o: entries about to run whose object is not a player.
- c: the number of waiting and semaphore entries, instead of a list.

`<target>` is a pid, or an object you control. With an object, only the entries that object runs are returned. If the object is a player and `<owned>` is 1, every entry run by something that player owns is returned instead.

```sharp
> @wait 300=think Hello
> @wait me=think Done
> think pid()
14 15
> think pid(19,~)
14|#5|RUNNING~15|#5|RUNNING
> think pid(32,,s)
think Done
> think pid(0,,c)
2
```

::: seealso
- [LPIDS()]
- [PIDINFO()]
- [@ps]
:::
