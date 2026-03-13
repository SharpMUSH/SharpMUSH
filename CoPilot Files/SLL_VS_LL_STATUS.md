# SLL vs LL Prediction Mode — Current Status

**Date:** 2026-03-13
**Context:** After all ANTLR4 grammar fixes (Fix A, Fix B, inFunction save/restore, braceExplicitEvaluationString)

## Test Methodology

Ran the `ParserPerformanceDiagnosticTests.BBSScript_ParserPerformanceDiagnostics()` test which:
1. Parses all 102 executable lines of the Myrddin BBS v4.0.6 install script
2. Collects metrics in both LL and SLL modes using `DiagnosticErrorListener`
3. Compares syntax errors, full context scans, ambiguities, and context sensitivities

Additionally ran `AntlrParseTreeDiagnosticTests` for parse tree equivalence and
the full test suite (2299 tests) which uses the default SLL mode.

## Results Summary

| Metric | SLL Mode | LL Mode |
|--------|----------|---------|
| Parse time (102 BBS lines) | **8.9ms** | 1531.6ms |
| Avg per line | 87.5µs | 15,015.7µs |
| **Speedup** | **171.68×** | baseline |
| Syntax errors | 2 | 2 |
| Error agreement | ✅ Identical on every line | ✅ Identical |
| Full context scans | 0 | 477 across 97 lines |
| Ambiguity reports | 472 | 5 |
| Context sensitivities | 0 | 472 |
| All 2299 tests pass | ✅ | ✅ |

## Detailed Analysis

### Syntax Errors — Identical in Both Modes

Both modes produce exactly 2 syntax errors on lines 74 and 96. These are the known
orphaned `CBRACK` tokens from escaped bracket sequences (`\[...\]` → `ESCAPE ANY ... CBRACK`).
The Fix A `inBracketDepth` counter was reverted at the grammar level to avoid
AdaptivePredict hangs, so these remain as expected parser errors that are harmless
in practice (the visitor handles them correctly as generic text).

### Full Context Scans (LL Mode Only)

LL mode performs **477 full context scans** across 97/102 lines (95.1%):

- **`explicitEvaluationString`**: 472 scans — triggered by the `{ inFunction == 0 }? CPAREN`
  predicate in `beginGenericText`. ANTLR4's LL prediction must evaluate semantic predicates
  in full context mode to determine if `)` is generic text or a function-closing paren.

- **`function`**: 5 scans — triggered by ambiguity in function argument processing with
  the `inFunctionInsideBrace` predicate.

**These scans are expected and correct behavior**, not bugs. Semantic predicates inherently
require full context evaluation since they depend on parser runtime state.

### Ambiguity Reports

**LL mode: 5 ambiguities** — All in the `function` rule, all inexact (not exact ambiguities).
These occur when ANTLR4 detects multiple viable paths through function arguments but
resolves them correctly via semantic predicates.

**SLL mode: 472 ambiguities** — SLL cannot evaluate semantic predicates during prediction,
so it reports every predicate-dependent decision point as an ambiguity. However, SLL
resolves these by choosing the first alternative (which is the correct choice for this
grammar) — this is why the parse results are identical.

### Context Sensitivities (LL Mode Only)

**472 context sensitivity events** on `explicitEvaluationString`, all resolving to
prediction=1. These confirm the grammar is sensitive to context (parser state) at these
decision points but consistently resolves to the same alternative.

## Why SLL Works Correctly

The grammar's semantic predicates (`{ inFunction == 0 }?`, `{ inBracketDepth == 0 }?`,
`{ inFunctionInsideBrace == 0 }?`) guard alternatives in `beginGenericText` that are
"fallback" cases — they exist to consume tokens like `)`, `,`, `]` as plain text when
they appear outside their normal structural context.

In SLL mode, ANTLR4 cannot evaluate these predicates during prediction and treats them
as `true`. However, because the predicated alternatives appear **last** in the rule
(`beginGenericText`), SLL naturally prefers the earlier, non-predicated alternatives
(structural `)`, `,`, `]`) — which is the same result LL would choose. The predicates
only activate when the structural parse genuinely fails, at which point both modes
would fall back to generic text.

## Conclusion

**SLL is the correct default mode.** It produces identical parse results to LL with a
171.68× performance improvement. The full context scans in LL mode are expected
overhead from semantic predicates that SLL correctly handles through alternative ordering.

The 2 remaining syntax errors (orphaned CBRACK on lines 74 and 96) exist in both modes
and are a known limitation of the current escape handling approach — these are handled
correctly by the visitor regardless of prediction mode.
