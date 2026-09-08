# Function flag audit (#858)

The 32 numeric declarations were checked against their parsing code before enabling dispatch validation.

| Contract | Functions | Decision |
| --- | --- | --- |
| Signed 64-bit integers | div, floordiv, modulo, remainder | Keep IntegersOnly; empty arguments remain zero. Arithmetic overflow and zero-divisor checks stay in the function. |
| Unsigned 64-bit integers, including zero | band, bnand, bnot, bor, bxor, shr, shl | Keep PositiveIntegersOnly; preserve ulong range and singular bnot error. |
| Iteration level | ibreak | Use IntegersOnly with a signed range check. PennMUSH defaults to one level, zero is a no-op, and n breaks the n innermost frames. |
| Decimal | eq, gt, gte, lt, lte, neq, add, sub, mul, max, min, abs, sign, trunc | Keep DecimalsOnly, matching existing decimal parsers and empty-as-zero behavior. |
| Decimal with special error precedence | fdiv | Keep validation local: a zero divisor currently takes precedence over other invalid operands. Remove the blanket flag. |
| Floating point | ceil, e, floor, ln, sqrt | Replace DecimalsOnly with NumbersOnly to retain exponent notation and double range. Fix e's incorrect argument index. |

Global side-effect checks belong in dispatch via HasSideFX. Mixed getter/setter functions name, powers, parent and zone use HasSideFX with SideEffectMinArgs = 2, so getters remain available. The source generator carries this metadata into the runtime table. Q-register operations, including formq, are expression state and remain usable with global side effects disabled, like setq in PennMUSH.

Disabled, NoFixed, Localize, LogName, LogArgs and Deprecated describe dispatch behavior. BuiltIn, Override, UserFunction and Clone duplicate function-library/registry structure and have no declarations or readers; remove these unused enum members, retaining all other bit values. Arg_Mask is NoParse | Literal, not a standalone bit. Numeric enum values are not a PennMUSH interchange format.

The existing @function/disable registry path hides disabled user definitions during resolution; its existing command regression remains applicable. It is separate from Disabled on a registered C# function.

Live PennMUSH 1.8.8 checks confirmed `pemit()` with global side effects off returns `#-1 FUNCTION DISABLED`, localized q-register changes restore the caller, and the ibreak defaults above. `e()` and `e(null())` both default to exp(1); `e(0)` is 1. SharpMUSH direct-output extensions wsjson, wshtml and oob deliberately obey the same side-effect switch as pemit.

## Dispatcher contract

- Permission flags are checked before normal argument evaluation, preserving SharpMUSH's existing denied-call behavior. AdminOnly admits wizard or royalty, GodOnly only God, and NoFixed rejects FIXED executors.
- Arity and parity are checked before invoking the function. Numeric validation sees evaluated, ANSI-stripped arguments; empty arguments retain existing zero conversion. Function-specific domain and division checks remain local.
- HasSideFX is checked after argument evaluation and before invoking the function. A side-effecting argument still encounters its own gate. SideEffectMinArgs defaults to zero and is two on the mixed getters/setters above.
- Localize copies incoming q-register frames and discards writes on return, including exceptions. It applies to localize, ulocal and uldefault. Lazy localized defaults evaluate against the localized parser rather than a captured caller frame.
- LogName records the function and executor; LogArgs takes precedence and includes arguments. Deprecated notifies the executor's owner.
- #apply uses the same permission, numeric, parity, side-effect, diagnostic and register-scope policy, including built-in restriction overlays. namelist callbacks evaluate expressions with %0/%1 instead of running command text.
- @function/info displays all active flags, including the zero-valued Regular default.
