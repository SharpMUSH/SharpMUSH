<!-- help-article
{
  "corpus": "help",
  "id": "halt-command",
  "lookup": "@halt",
  "aliases": ["@allhalt"],
  "sections": [
    {"id": "pid", "heading": "Cancelling one queue entry", "lookup": "@halt pid", "aliases": ["@halt/pid"]},
    {"id": "all", "heading": "Halting everything", "lookup": "@halt all", "aliases": ["@halt/all"]}
  ]
}
-->
# @halt

`@halt[/noeval] <object>[=<action list>]`<br>
`@halt/pid <pid>`<br>
`@halt/all`<br>
`@allhalt`

The @halt command removes all queued actions for `<object>`. If given, `<action list>` is placed in the queue for the object instead. If no action list is specified, the object is set HALT.

If `<object>` is a player, it clears the queue for the player and all of his objects. You can use "@halt me" to clear your own queue without setting yourself HALT.

Only wizards and objects with the halt @power can @halt other player's objects. Note that halting an object does NOT affect any objects waiting on it as a semaphore.

- [@halt pid]: cancelling a single queue entry by its pid
- [@halt all]: halting every object in the game, for wizards

## Cancelling one queue entry

`@halt/pid <pid>`

`@halt/pid` will cancel a single queue entry with the given pid (the number in parenthesis before it in @ps). You must control the object that queued the command or have the halt power to do this.

A cancelled delayed entry remains reserved until its scheduled trigger is confirmed removed. If scheduling or cancellation reports a cleanup failure, the command stays cancelled and consumes its queue allowance; retry `@halt/pid <pid>` after the scheduler is available to finish cleanup.

## Halting everything

`@halt/all`<br>
`@allhalt`

`@halt/all` is a synonym for @allhalt, and is a wizard-only command which halts all objects in the game in an effort to free up the queue.

::: seealso
- [@wait]
- [@ps]
- [semaphores]
- [@drain]
- [@notify]
:::
