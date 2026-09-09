# localfun

`localfun(<name>[,<argument>...])` calls an owner-local function. The executing
object's current owner selects the registry scope. Ordinary built-in and global
function calls keep their existing resolution rules; local functions require
this explicit call. Two owners can register the same local name independently.

`@function/local <name>=<object>,<attribute>[,<minimum>,<maximum>]` registers a
function in your executor's owner scope. The object must have that same owner,
you must control it, and you must be able to read the attribute. Names contain
ASCII letters, digits and underscores, up to 64 characters. Built-in names are
reserved, including names contributed by loaded plugins. A temporary softcode
deletion does not remove that reservation; compiled names stay reserved for the
library's lifetime. A live built-in clone also takes precedence over an existing
local name, including calls through a local alias. Argument bounds default to zero
through 32.

For example, when entered as separate client commands:

```mush
&DOUBLE me=mul(%0,2)
@function/local double=me,DOUBLE,1,1
think localfun(double,7)
```

The result is `14`. Arguments become `%0`, `%1`, and so on. They retain their
markup and are not reconstructed as source text. Existing attribute execution,
read permissions, register behavior, invocation limits and shared execution
budgets apply. The body executes as the backing object, following ordinary
attribute evaluation. A privileged caller has no switch for selecting another
owner's scope.

# @function/local

`@function/local` lists your owner's local entries, including disabled entries.
`@function/local <name>` shows one entry. The following operations use that same
scope:

- `@function/local/delete <name>` removes an entry.
- `@function/local/disable <name>` prevents calls.
- `@function/local/enable <name>` enables calls.
- `@function/local/alias <alias>=<name>` creates an alias to a concrete local entry.
- `@function/local/preserve <name>` retains it during a bulk restore, including
  its concrete target when the preserved entry is an alias.
- `@function/local/restore *` removes unpreserved entries in your scope, except
  targets required by preserved aliases.
- `@function/local/restore <name>` removes that entry, including a preserved one.

Aliases stay within the same full owner identity. A disabled target cannot be
called through its alias. Aliases cannot point to other aliases or global
functions. Replacing a concrete entry with an alias is rejected while other
aliases depend on that entry. Local cloning, restriction overlays and built-in
replacement are not supported.

Ownership changes and deletion remove affected local registrations and aliases.
Each call also checks the backing object's full identity and current owner, so
recycled object numbers cannot inherit code. A calling object's new owner
selects the new scope immediately. Definitions are never cached in the shared
built-in function table.

# local function startup

The registry is in memory. Re-register local functions from the backing object's
`STARTUP` attribute after each engine restart, using the same `@function/local`
command. For the example above:

```mush
&STARTUP me=@function/local double=me,DOUBLE,1,1
```

`/preserve` affects a bulk registry restore within the running process. It does
not persist the registration across a restart. Store the startup command as
softcode; normal startup ownership and attribute permissions still apply.
