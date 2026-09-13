# Locks and PennMUSH compatibility

The compatibility reference is PennMUSH commit `80a1d5b9dffee3587d0110759bdfc5f0f60cfb3f`. Explicit timestamped objids and the zero-argument `locks()` listing are supported SharpMUSH extensions.

Object operands are resolved when a lock is set. `@lock box=me` records the executor's dbref; changing the box's owner or renaming the executor does not retarget it. This applies to bare keys, `=`, `+`, `$`, and `@` operands. Text inside attribute values and name patterns is not an object reference. Bare dbrefs remain bare; explicitly supplied timestamped objids retain their timestamps.

Invalid syntax, missing objects, ambiguous names, and denied writes leave the existing lock unchanged. An empty key removes the lock. `#TRUE` is an explicitly set lock and is distinct from an absent lock.

Commands and functions use the same lock operations:

- `@lock box=me` and `lock(box,me)` set the Basic lock.
- `@unlock box` and `lock(box,)` remove it.
- `@lock/user:Example box=#TRUE` creates a custom lock; later writes may use `Example`.
- `@lset box/Basic=visual` and `lset(box/Basic,visual)` change flags.
- `@lock box/ATTRIBUTE` and `@unlock box/ATTRIBUTE` change attribute locking.

A leading `!` makes a flag operation clear its selected mask. Negated tokens inside that mask exclude flags from the selection: `visual !no_clone` sets visual and leaves an existing no_clone flag unchanged. To set visual and remove no_clone, use separate `visual` and `!no_clone` operations, as in PennMUSH.

`lset()` takes two arguments and changes lock flags. List replacement is `listset(list,position,value[,input delimiter[,output delimiter]])`.

Examine shows each lock's creator, flags, and a viewer-aware rendering of its object references. `lock()` returns numeric references. `lockowner()` returns the setter, rather than the object's owner. `locks()` lists standard lock types; `locks(object)` lists locally set locks, with `USER:` prefixes for custom locks. `lockflags()` uses `v` (visual), `i` (no_inherit), `c` (no_clone), `w` (wizard), and `+` (locked); `llockflags()` returns their names. The internal owner restriction is not a user-settable flag.

Lock reads and evaluation find inherited parent/ancestor locks unless `no_inherit` blocks them. Decompile emits the lock expression and the flag changes required to reproduce it, including clearing default flags. A decompiled reference to its viewer can use `me`; replay it as the intended executor.

`lockfilter(expression,objects[,delimiter])` evaluates one expression against the listed objects. `testlock(expression,victim)` binds the expression in its caller's context.

## Existing development worlds

Old lock records may have no creator. They remain explicitly unknown (`#-1`); no owner is substituted. Canonical expressions with unknown creators can still evaluate. An unknown creator cannot satisfy the creator restriction on a locked lock; a wizard or God must repair it.

Unresolved object names, including literal `me`, or malformed stored expressions fail evaluation as a whole, even inside negation or an otherwise true OR expression. Examine identifies invalid expressions, and decompile omits executable commands for them. Reset the development world or replace the lock explicitly as the intended setter. There is no automatic identity backfill.

## Representative reference transcript

An isolated PennMUSH world with mortal setter `#3` and owned target `#4` produces these results. The lock command, function, and view regressions cover the same behavior with independently created mortal players.

| Input | Result |
|---|---|
| `@lock LockTarget=me` then `lock(LockTarget)` | `#3` |
| `lockowner(LockTarget)` | `#3` |
| `examine LockTarget` | `Basic Lock [#3i]: LockMortal(#3…)` |
| `@lock LockTarget=#TRUE&` then `lock(LockTarget)` | Rejected; remains `#3` |
| `@lset LockTarget/Basic=visual !no_clone` then `lockflags(LockTarget)` | `vi` |
| `@decompile/db LockTarget` | Includes `@lock/Basic #4=me` and restores `visual no_inherit` |
| `@lock LockTarget=` then `lock(LockTarget)` | `*UNLOCKED*` |
| `lockowner(LockTarget)` after unlock | `#-1 NO SUCH LOCK` |
