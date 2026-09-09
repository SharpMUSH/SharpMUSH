# restrictedexpr()

`restrictedexpr(<allowlist>,<expression>[,<input0>,...,<input9>])`

Evaluate an expression with a smaller set of operations and explicit literal inputs.
The allowlist is a space-separated list of supported function names. An empty list
allows literal text and input substitutions only. Inputs are not evaluated: `%0`
through `%9` insert the corresponding supplied text. Unspecified inputs are empty.

Examples:

    restrictedexpr(add,add(%0,%1),2,3)
    5

    restrictedexpr(trim ucstr,ucstr(trim(%0)),hello world)
    HELLO WORLD

    restrictedexpr(,%0%b%1,hello,world)
    hello world

The initial profile supports `add`, `sub`, `mul`, `div`, `cat`, `strcat`, `strlen`,
`ucstr`, `lcstr`, `trim`, `space`, `fn`, and
`restrictedexpr`. Each must be explicitly allowed. Function aliases and builtin
clones resolve to the original operation before the allowlist is checked. A
nested `restrictedexpr()` intersects its requested operations with the caller's
allowlist; it cannot enable an operation the caller omitted. `fn()` checks the
resolved target under the same restrictions and cannot use its attribute fallback.

Only `%0` through `%9`, `%b` (space), `%r` (newline), `%t` (tab), and `%%` are
available as substitutions. Ordinary quoting and literal text still work. Parent
Q-registers, regex captures, iteration registers, attribute values, and identity
substitutions are not inputs. The expression receives fresh register frames.
Debug and verbose forwarding are suppressed inside restricted evaluation, including
for executors with DEBUG set. Wrapper calls, their aliases, and enclosing debug
traces are also suppressed so literal inputs are not forwarded before the
restricted scope starts. Function-metadata logging and error notifications are
suppressed during restricted calls. Malformed wrapper input is not forwarded by
debug output, and parser tracing is suppressed before the wrapper is parsed.

An unsupported operation or substitution returns `#-1 RESTRICTED EXPRESSION`.
Object reads, attribute evaluation (`u()`), global and local user-defined
functions, plugin functions, commands, SQL, HTTP, queueing, and other side effects
are unavailable in this profile, even when the executor is God. Adding their
names to the allowlist does not grant access. Operation permission and object
access are separate: this profile grants no object-data access. Audited calls do
not resolve the executor. Disabled functions remain disabled; functions requiring
identity-based permissions or an explicit restriction are denied in this profile.

Restrictions apply to this evaluation and its nested calls; they do not modify
server-wide function permissions or other concurrent evaluations. The existing
invocation, output, recursion, and elapsed execution limits remain in force, and
nested restricted calls share the active execution budget. Inputs and expression
text together, and each combined result, must fit the shared 5 MiB character
ceiling. Arguments, expression fragments, wrapper source and inputs, and serialized `fn()`
source retained across
active nested calls also share that ceiling. Each expansion is checked before it
is retained, and `fn()` reserves space before serializing its next call. This also
covers chains of `fn()` calls leading into the wrapper. Expansion
and concatenation are checked before allocation. Restricted
calls accept at most 33 arguments, including the target name supplied to `fn()`,
and retain each target function's smaller argument limit. Excess arguments are
rejected before argument arrays and maps are allocated.

This is a boundary for softcode expressions using the supported core operations.
Native plugins and server code remain trusted code and are not isolated by it.

Call this wrapper with direct function syntax. `#apply` entry, including aliases
and `fn` indirection, is rejected because those arguments have already been
evaluated by the caller and cannot supply the required raw expression and inputs.

List tokenization functions (`first`, `rest`, `extract`, and `words`) are excluded
until their scanning is allocation-bounded and interruptible. Progress and readmission
criteria are tracked in [issue #977](https://github.com/SharpMUSH/SharpMUSH/issues/977).
