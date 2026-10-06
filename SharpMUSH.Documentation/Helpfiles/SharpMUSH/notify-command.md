<!-- help-article
{
  "corpus": "help",
  "id": "notify-command",
  "lookup": "@notify",
  "aliases": [],
  "sections": [
    {
      "id": "updating-waiting-registers",
      "heading": "Updating waiting registers",
      "lookup": "@notify updating waiting registers"
    }
  ],
  "redirects": {
    "@notify2": "@notify updating waiting registers"
  }
}
-->
# @notify

`@notify[/any][/all] <object>[/<attribute>][=<number>]`<br>
`@notify/setq <object>[/<attribute>]=<qreg1>,<qval1>[,...]`

This command notifies a semaphore, allowing commands queued for that semaphore to be executed.

The semaphore counter is stored before waiting commands are released. If the database cannot confirm a counter change, affected work stays reserved and further semaphore changes are refused until reconciliation succeeds. A later attempt verifies the stored count before completing or abandoning the uncertain operation. Conflicting counts or incomplete semaphore flags require administrator repair.

If the `/any` switch is given, then all semaphores associated with `<object>` are @notified. Otherwise, only the specified semaphore `<attribute>` (or SEMAPHORE if no attribute is specified) is @notified.

If the `/all` switch is given, then all queue entries associated with the selected semaphore(s) are executed. Otherwise, only the first `<number>` of queue entries are run. If no `<number>` is given, then only one queue entry is run.

If the `/all` switch was not used, and there were not enough queue entries waiting to satisfy the requested `<number>`, then the semaphore becomes negative, and subsequent @waits will not block until it reaches 0 again.

You may not specify both the `/any` switch and a specific attribute. Similarly, you may not specify both the `/all` switch and a number.

## Updating waiting registers

`@notify/setq` is a special form of @notify: It requires that a queue entry exists and is waiting on `<object>[/<attr>]`. When this is the case, then `@notify/setq` will modify the Q-registers of the extant queue entry.

`/setq` supercedes all other switches: You cannot `@notify/all/setq` or `@notify/any/setq` - it deals with just one queue entry.

### Example
```sharp
> @wait me=think Hello, %q0!
> @notify/setq me=0,Walker
Hello, Walker!
```


::: seealso
- [semaphores]
- [@drain]
- [@wait]
- [@halt]
:::
