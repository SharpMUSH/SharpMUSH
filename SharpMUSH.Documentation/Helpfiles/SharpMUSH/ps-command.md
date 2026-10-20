<!-- help-article
{
  "corpus": "help",
  "id": "ps-command",
  "lookup": "@ps",
  "aliases": [],
  "sections": [
    {
      "id": "inspecting-a-queue-entry",
      "heading": "Inspecting a queue entry",
      "lookup": "@ps inspecting a queue entry"
    }
  ],
  "redirects": {
    "@ps2": "@ps inspecting a queue entry"
  }
}
-->
# @ps

See [@ps/history] for recent outcomes and [@profile] for temporary invocation timing.<br>
`@ps[/<switch>] [<object>]`<br>
`@ps/debug <pid>`

@ps lists the commands currently on an object's queue, thus allowing you to identify infinite (or unnecessary) loops without putting in says or poses. It gives a count of the entries in each of its queues (Command, Wait, and Semaphore), then lists up to ten semaphore and ten wait entries.

@ps with no arguments shows your own queue. You may name an object you control; Wizards, Royalty and those with the See_Queue power may name any object, and may use the `/all` switch to see the totals for every queue group. `@ps/quick` and `@ps/summary` display just the counts for the object's queue; `/summary` also prints a load average line, which is always "0.0, 0.0, 0.0".

## Inspecting a queue entry

`@ps/debug <pid>` shows a single queue entry: its owner, the object and attribute used as a semaphore (if any), the command, and the seconds left before it runs (for waits). Without `/debug`, the argument is taken as an object, not a pid. To halt a specific queue entry, use `@halt/pid`.


::: seealso
- [@wait]
- [@halt]
- [@notify]
- [@drain]
- [semaphores]
:::
