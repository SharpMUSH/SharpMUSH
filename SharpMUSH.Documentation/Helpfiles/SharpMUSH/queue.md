<!-- help-article
{
  "corpus": "help",
  "id": "queue",
  "lookup": "queue",
  "aliases": [],
  "sections": [
    {
      "id": "queue-configuration",
      "heading": "Queue configuration",
      "lookup": "queue configuration"
    }
  ],
  "redirects": {
    "QUEUE2": "queue configuration"
  }
}
-->
# queue

QUEUE

The queue is the waiting line for action lists to be executed by the MUSH.  Each time you enter an action list, it goes into the queue and stays there until its turn comes up, at which time the MUSH processes the commands and you see the results. The MUSH can execute thousands of commands every second, so normally you see results right away.

There are actually two distinct queues:
* Incoming socket commands. These are run immediately, so a "look" or "`@ps`" will happen before any other softcode that is waiting to run. A socket may 'burst' up to 100 commands, then 1 per second after that.
* Command queue. These enter the queue via `@trigger`, `@force`, or many other ways.

There is also a Wait queue, where commands queued via the `@wait` command go, and a Semaphore queue, where semaphore `@waits` go. These commands are moved to the command queue at the appropriate time, either when the `@wait` time elapses, or when the semaphore is `@notified`.

You can see which commands are currently queued with the `@ps` command, and with the `getpids()`, `lpids()` and `pidinfo()` functions. Queued commands can be cancelled with `@halt` and/or `@drain`.

## Queue configuration

There are several `@config` options which affect queueing.

The option 'player_queue_limit' controls how many action lists can be queued by one object at any given time. Wizards and objects with the Queue `@power` can queue more commands (equal to the player_queue_limit plus the current number of objects in the database, including garbage). An object that tries to queue past its limit is halted: its pending commands are wiped, it is set HALT, and its owner is told "Runaway object: `<name>`(`<dbref>`). Commands halted." A player's own typed commands are never counted against the limit, so a player whose objects have exhausted it can still act.

Normally each object has its own queue count, but if the 'owner_queues' option is enabled, objects share a queue count with their owner.

'queue_chunk' controls how many commands SharpMUSH runs before checking again for incoming socket commands or connections.

It costs a certain number of pennies to queue an action list; the exact amount is set in the 'queue_cost' `@config` option. These pennies are returned after the action list is run. Sometimes, you'll lose a penny when queueing a command; the chance of this happening is controlled by the 'queue_loss' option.


**See Also:**
- [@ps]
- [LOOPING]
- [action lists]
