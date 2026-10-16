<!-- help-article
{
  "corpus": "help",
  "id": "locking",
  "lookup": "Locking",
  "aliases": ["Locks", "@lock"],
  "sections": [
    {"id": "success-failure", "heading": "Passing and failing a lock", "lookup": "locking success and failure"},
    {"id": "inheritance", "heading": "Inherited locks, lock types and keys", "lookup": "locking inheritance"},
    {"id": "switch-spellings", "heading": "RhostMUSH switch spellings", "lookup": "locking switch spellings"}
  ]
}
-->
# Locking

`@lock[/<switch>] <object>=<key>`

This command "locks" the object, specifying a key which determines who or what can do certain things with the object. There are many different types of locks, all of which are described in [locktypes] and which are designated by the switch. The "basic" lock determines, for players and things, who can pick them up. For exits, it determines who can go through the exit. All other locks can be set the same way as the basic lock.

- [locking success and failure]: the messages and actions a lock triggers
- [locking inheritance]: locks from parents, and where lock types and keys are listed
- [locking switch spellings]: `@lock/enterlock` and other RhostMUSH spellings

## Passing and failing a lock

Whenever you "pass" the basic lock, you succeed in doing something with the object. This triggers the @success/@osuccess/@asuccess messages and actions. If you fail to pass the basic lock, you trigger the @failure/@ofailure/@afailure messages and actions. Other locktypes may also have such success/failure messages: see [failure] for info.

## Inherited locks, lock types and keys

Just like attributes, locks can be inherited from parents. By default, locks are set no_inherit, but this flag can be cleared using @lset. More details and a list of flags can be found in [@lset].

A listing of lock types, such as pagelocks, look at [locktypes]. For the available key types, such as how to check an attribute on an object trying to pass a lock, see [lock keys].

## RhostMUSH switch spellings

The switch may also name the lock with "lock" on the end, as RhostMUSH spells its switches: `@lock/enterlock` is `@lock/enter`, `@lock/tportlock` is `@lock/teleport` and `@lock/defaultlock` is `@lock/basic`. @unlock takes the same spellings. This works for every lock type in [locktypes]; RhostMUSH lock types that SharpMUSH does not have, such as `teloutlock` or `darklock`, are still unknown lock types. A user-defined lock whose name ends in "lock" keeps its own name. These spellings come from RhostMUSH.

::: seealso
- [@LOCK-SIMPLE]
- [locktypes]
- [lock keys]
- [@CHANNEL CLOCK]
- [failure]
- [SUCCESS]
- [ELOCK()]
- [LOCK()]
- [@lset]
- [@CHANNEL CLOCK]
- [TESTLOCK()]
- [LLOCKS()]
- [LOCKFLAGS()]
- [LOCKOWNER()]
- [clock()]
- [LLOCKS()]
:::
