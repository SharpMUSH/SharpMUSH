# Function flag audit (#858)

The 32 numeric declarations were checked against their parsing code before enabling dispatch validation.

| Contract | Functions | Decision |
| --- | --- | --- |
| Signed 64-bit integers | div, floordiv, modulo, remainder | Keep IntegersOnly; empty arguments remain zero. Arithmetic overflow and zero-divisor checks stay in the function. |
| Unsigned 64-bit integers, including zero | band, bnand, bnot, bor, bxor, shr, shl | Keep PositiveIntegersOnly; preserve ulong range and singular bnot error. |
| Iteration level | ibreak | Keep PositiveIntegersOnly; check range before narrowing to an index. Omitted level is zero. |
| Decimal | eq, gt, gte, lt, lte, neq, add, sub, mul, max, min, abs, sign, trunc | Keep DecimalsOnly, matching existing decimal parsers and empty-as-zero behavior. |
| Decimal with special error precedence | fdiv | Keep validation local: a zero divisor currently takes precedence over other invalid operands. Remove the blanket flag. |
| Floating point | ceil, e, floor, ln, sqrt | Replace DecimalsOnly with NumbersOnly to retain exponent notation and double range. Fix e's incorrect argument index. |

Global side-effect checks belong in dispatch via HasSideFX. Mixed getter/setter functions retain their conditional checks. Q-register operations, including formq, are expression state and remain usable with global side effects disabled, like setq in PennMUSH.

Disabled, NoFixed, Localize, LogName, LogArgs and Deprecated describe dispatch behavior. BuiltIn, Override, UserFunction and Clone duplicate function-library/registry structure and have no declarations or readers; remove these unused enum members, retaining all other bit values. Arg_Mask is NoParse | Literal, not a standalone bit. Numeric enum values are not a PennMUSH interchange format.

The existing @function/disable registry path hides disabled user definitions during resolution; its existing command regression remains applicable. It is separate from Disabled on a registered C# function.
