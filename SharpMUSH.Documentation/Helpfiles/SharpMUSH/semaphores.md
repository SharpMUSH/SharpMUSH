<!-- help-article
{
  "corpus": "help",
  "id": "semaphores",
  "lookup": "semaphores",
  "aliases": [],
  "sections": [
    {
      "id": "notifying-waiters",
      "heading": "Notifying waiters",
      "lookup": "semaphores notifying waiters"
    },
    {
      "id": "execution-identity",
      "heading": "Execution identity",
      "lookup": "semaphores execution identity"
    },
    {
      "id": "mutual-exclusion",
      "heading": "Mutual exclusion",
      "lookup": "semaphores mutual exclusion"
    },
    {
      "id": "named-semaphore-attributes",
      "heading": "Named semaphore attributes",
      "lookup": "semaphores named semaphore attributes"
    },
    {
      "id": "named-semaphore-example",
      "heading": "Named semaphore example",
      "lookup": "semaphores named semaphore example"
    }
  ],
  "redirects": {
    "SEMAPHORES2": "semaphores notifying waiters",
    "SEMAPHORES3": "semaphores execution identity",
    "SEMAPHORES4": "semaphores mutual exclusion",
    "SEMAPHORES5": "semaphores named semaphore attributes",
    "SEMAPHORES6": "semaphores named semaphore example"
  }
}
-->
# semaphores

The most complicated thing about semaphores is their name. Before you try to use semaphores, you should first be familiar with the "`@wait`" command. If you are, then you know that normally, you type:

    `@wait` `<number of seconds>`=`<action>`

and the action takes place after that number of seconds has passed. With a semaphore, you instead type:

    `@wait` `<object>`=`<action>`<br>
    `@wait` `<object>`/`<number of seconds before timeout>`=`<action>`

and the action takes place after the object has been "notified" that its time for it to happen. You can also set a timeout -- if the object hasn't been notified by the time that number of seconds has passed, the action will take place. Any object (player, thing, exit, room) that you control or that is set LINK_OK can be used to wait actions on.

## Notifying waiters

An object is notified using the "`@notify`" command. When you type "`@wait` `<object>`=`<action>`", you are adding one to the SEMAPHORE attribute on the object. When you type "`@notify` `<object>`", you are decreasing the SEMAPHORE attribute on the object by one. Whenever the attribute decreases, one of the actions waiting on the object takes place. The actions occur in the order they were added.

You can make the semaphore attribute of an object negative by `@notify`-ing it more times than things have been `@wait`-ed on it. If you do so, anything `@wait`-ed on the object will add one to the SEMAPHORE attribute and the action will take place immediately. You can also make all the actions waiting on an object take place right away by using "`@notify`/all", or wipe all the commands out and clear the SEMAPHORE attribute by using "`@drain`". Please note that all SEMAPHORE attributes are cleared out whenever the MUSH is restarted.

Semaphores can be used to make sure that events occur in the right order, or to make sure that two players can't use the same object at the same time.

## Execution identity

It's important to remember that the actions will be carried out NOT by the object that they are being `@waited` on, but by whichever object entered the `@wait`.


Examples:
```sharp
    > @wait semaphore=:tests.
    > @notify semaphore
    Wizard tests.
```
```sharp
    > @wait timer/30=:waits 30 seconds.
    [ 30 seconds passes. ]
    Wizard waits 30 seconds.
```

## Mutual exclusion

Semaphores can be used to enforce mutual exclusion - to prevent the same object from being used simultaneously by two players. The basic strategy is to ensure that the object always has a SEMAPHORE of -1, to enclose commands in an `@wait`, and to conclude the set of commands with an `@notify` me:

```sharp
    > &doit obj=$doit: @wait me={&doer me = %n; @trigger me/report}
    > &report obj=say [v(doer)] did it!; @notify me
    > @startup obj=@drain me; @notify me
    > @notify obj
    > ex obj/SEMAPHORE
    SEMAPHORE [#1ic+]: -1
    > doit
    obj says, "Talek did it!
    > ex obj/SEMAPHORE
    SEMAPHORE [#1ic+]: -1
```

If a second player types doit as well, the second player's command is put on the semaphore queue and not run until the `@notify` me at the end of the REPORT attribute. Note the STARTUP attribute - because semaphores are cleared when the MUSH starts up, you must insure that the object gets `@notify`'d once when it starts up.

## Named semaphore attributes

Normally, semaphores use the SEMAPHORE attribute. However, other attributes can be used, as long as they follow a few simple rules: If the attribute is already set, it has to have the same owner (God) and flags as the SEMAPHORE attribute would (typically no_inherit, no_clone, and locked - see [@set] and '`@atrlock`'), and have a numeric or empty value. If it's not set, it can't be one of the built in attributes (See `@list` attribs) unless, naturally, it is SEMAPHORE.

See the help on `@wait`, `@notify` and `@drain` for details, but, briefly, you can use named semaphores with `<object>`/`<attribute>` where you would normally just use `<object>` in those commands. This means you can't have an untimed semaphore on an attribute with a numeric name.


::: seealso
- [@wait]
- [@drain]
- [@notify]
:::

## Named semaphore example

An example:

```sharp
  > @wait me/semtest=think blah
  > ex me/semtest
  SEMTEST [#1ic+]: 1
  > @ps
  ...
  Semaphore Queue:
  [#8/SEMTEST]Raevnos(#8P):think blah
  ...
  > @notify me/semtest
  Notified.
  blah
```

This allows you to use one object to control many different things -- for example, fights in a turn-based combat sytem.
