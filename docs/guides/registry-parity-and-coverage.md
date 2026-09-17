# Registry parity and coverage

Three things about the function and command surface used to be true only by accident: that every
registered name had a help topic, that the signature in that topic matched the arity the parser
enforces, and that some test somewhere had called it. Each is now a test. This is what they check,
where their data comes from, and how to change them when the answer is meant to change.

## What is checked

| Test | Asks | Lives in |
|---|---|---|
| `HelpRegistryParityTests` | every registered function and command — aliases included — has a help topic; every `# X()` topic names a registered function; every signature's arity matches `MinArgs`/`MaxArgs` | `SharpMUSH.Tests/Documentation/` |
| `FunctionArityParityTests` | every declared arity matches PennMUSH's, except for a table of deliberate differences | `SharpMUSH.Tests/Functions/` |
| `RegistryCoverageInventoryTests` | some test source calls every registered function by name | `SharpMUSH.Tests/Functions/` |

Each carries its exceptions as a dictionary from name to reason, and each has a companion test that
fails when an exception stops applying. An excuse that has quietly become false is worse than no
excuse, because the next reader believes it.

## The PennMUSH arity table

`SharpMUSH.Tests/PennMUSH/function-arity.tsv` is PennMUSH's own declaration of how many arguments
each function takes: one row per entry in `FUNTAB flist[]`, from `src/function.c`. PennMUSH is not
vendored in this repository, so the table is a copy, taken from 1.8.8.

To regenerate it, from a PennMUSH checkout:

```bash
python3 - <<'PY' > SharpMUSH.Tests/PennMUSH/function-arity.tsv
import re
src = open('<pennmush>/src/function.c', encoding='utf-8', errors='replace').read()
body = src[src.index('FUNTAB flist[] = {'):]
body = body[:body.index('\n};')]
print('# PennMUSH function arity, extracted from pennmush/src/function.c FUNTAB flist[].')
for m in re.finditer(r'\{\s*"([^"]+)"\s*,\s*\w+\s*,\s*(-?\w+)\s*,\s*(-?\w+)\s*,', body):
    name, lo, hi = m.group(1).lower(), m.group(2), m.group(3)
    if lo == 'INT_MAX' or not re.fullmatch(r'-?\d+', lo): continue
    hi = 'INF' if hi == 'INT_MAX' else (abs(int(hi)) if re.fullmatch(r'-?\d+', hi) else None)
    if hi is None: continue
    print(f'{name}\t{lo}\t{hi}')
PY
```

Two conventions in that table are worth knowing before reading a row:

- **`INT_MAX` means unbounded.** SharpMUSH spells the same thing as the `MaxArgs` attribute default,
  32, so the test treats any declared maximum at or above 32 as satisfying an unbounded PennMUSH
  entry rather than demanding a particular large number.
- **A negative maximum means "the last argument swallows the rest".** `parse.c:2980` enforces
  `abs(maxargs)`, which is the number the table records. SharpMUSH reaches the same result with the
  literal flag on the function instead, so `ansi()` and `lit()` are listed as deliberate differences
  rather than made to match a number that means something else here.

## Changing an arity

The declared arity is three statements that have to agree, and the tests fail on whichever pair
disagrees:

1. `MinArgs`/`MaxArgs` on the `[SharpFunction]` attribute — what the parser enforces.
2. The signature line in the helpfile — what a player reads.
3. `function-arity.tsv` — what PennMUSH does.

Where SharpMUSH deliberately differs from (3), add the name to `DeliberateDivergences` in
`FunctionArityParityTests` with the reason. The time family's trailing `<precision>` argument and the
vector family's trailing `<osep>` are the two large groups there; both are additive, so a call
written for PennMUSH keeps working.

Where a signature cannot be compared at all — a literal function, where the declared maximum is never
reached — add it to `SignatureExceptions` in `HelpRegistryParityTests` instead.

## Coverage

`RegistryCoverageInventoryTests` asks whether any test source contains `name(`. That is a floor, not
a guarantee: a mention inside an unrelated assertion string counts. It is still the difference
between a function nobody has ever run and one somebody has, which is the distinction that matters —
of the twenty-six functions that had never been called when the inventory was regenerated, writing
the first call against them found the whole vector family returning the empty string, `isword()`
matching only single letters, `checkpass()` refusing player names and `render()` bound to a different
function than the one its help describes.

Do not keep a list of uncovered functions in an issue. The 44-name list #974 shipped with was wrong
in both directions within weeks. Regenerate it.

## Provider coverage

CI runs the Lightning provider only; the unit, integration and scene legs exclude the embedded
SurrealDB fixtures (#1019). A green run therefore says nothing about SurrealDB parity, and the
storage-layer tests are lopsided to match — several thousand lines for Lightning against a few
hundred for Surreal.

Locally, `SHARPMUSH_DATABASE_PROVIDER` selects the provider; with the variable unset only Lightning
runs. Anything asserted about storage behaviour on one provider has to be re-run against the other
before it can be described as a SharpMUSH guarantee rather than a Lightning one.
