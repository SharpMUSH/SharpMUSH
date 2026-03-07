# Flat Run Array Efficiency Analysis

## Problem Statement

The `AttributedMarkupString` uses a flat `ImmutableArray<AttributeRun>` parallel to a contiguous `string`. While this provides O(1) plain text access and excellent cache locality for rendering, it introduces specific inefficiencies for **substring extraction** and **chained operation sequences** that are common in MUSH function evaluation.

This document analyzes the costs, identifies the dominant workload patterns, and recommends alternative data structures with trade-off analysis.

---

## 1. Current Cost Model

### 1.1 Substring: O(n) scan + O(k) allocation

```fsharp
let substring (start: int) (length: int) (ams: AttributedMarkupString) =
    // Must scan ALL runs to find overlapping ones — O(n) where n = total runs
    for run in ams.Runs do
        if runEnd > actualStart && run.Start < actualEnd then ...
    // Creates new string via String.Substring — O(length) copy
    // Creates new ImmutableArray via builder — O(k) where k = overlapping runs
```

**Cost**: O(n + length + k) per call, where n = total runs, k = overlapping runs.

For a string with 100 runs, extracting a 3-character substring still scans all 100 runs.

### 1.2 Concat: O(a + b) copy

```fsharp
let concat (a: AttributedMarkupString) (b: AttributedMarkupString) =
    // New string allocation: O(|a| + |b|) character copy
    let combinedText = a.Text + b.Text
    // New runs array: O(a.Runs + b.Runs) with offset adjustment
```

**Cost**: O(|a.Text| + |b.Text| + a.Runs.Length + b.Runs.Length) per call.

### 1.3 Chained Operations: Multiplicative Intermediate Allocations

The dominant MUSH pattern — regex replace in a loop — chains substring+concat:

```csharp
// From AttributeFunctions.cs:960-963 — repeated per match
var before = MModule.substring(0, match.Index, mstr);           // scan all runs
var after  = MModule.substring(match.Index + match.Length, ...); // scan all runs again
mstr = MModule.concat(MModule.concat(before, replacement), after);
// Creates 2 intermediate ImmutableArrays and 2 intermediate strings that are
// immediately discarded when the outer concat runs
```

For a string with 50 regex matches, this creates **~200 intermediate arrays and ~200 intermediate strings**, most of which are GC'd immediately.

### 1.4 Split + Rejoin: O(n²) total work

```csharp
// Common pattern: split list, process, rejoin
var items = MModule.split2(delimiter, list);  // O(n * runs) — n substrings, each scans all runs
// ... process items ...
var result = MModule.multipleWithDelimiter(delimiter, processed);  // O(n²) from left-folded concat
```

`multipleWithDelimiter` uses left-fold concat, so joining n items copies: |item₁| + (|item₁|+|sep|+|item₂|) + ... ≈ O(n² * avg_item_length).

---

## 2. Workload Analysis

Operation frequency from the SharpMUSH codebase (938 total `MModule.*` call sites):

| Operation | Call sites | Notes |
|-----------|-----------|-------|
| `split` / `split2` | 67 | Always followed by iteration; frequently followed by rejoin |
| `multipleWithDelimiter` | 44 | Left-fold concat — O(n²) for n items |
| `substring` | 42 | Often paired with `concat` for replace-in-place |
| `single` (construction) | 39 | Leaf node creation — unavoidable |
| `concat` | 27 | Frequently chained: `concat(concat(a,b),c)` |
| `pad` | 4 | Calls `repeat` + `substring` internally |
| `trim` | 3 | Calls `substring` internally |
| `replace` | 3 | Calls `substring` + `concat` internally |
| `remove` | 1 | Calls `substring` + `concat` internally |

**Key insight**: The hot path is split → process → rejoin, and the secondary hot path is repeated substring+concat for regex replacement. Both suffer from the flat array's inability to share structure between the original and the result.

---

## 3. Alternative Data Structures

### 3.1 Piece Table

**Concept**: Instead of a single contiguous string, maintain a table of "pieces" — each piece references a span within either the original text or an append-only buffer of edits. Runs are associated with pieces rather than absolute positions.

```
Original: "Hello World" (immutable)
Append buffer: "Beautiful " (append-only)

Piece table:
  [0] Original[0..6]   "Hello "    runs: [{0,6,empty}]
  [1] Append[0..10]    "Beautiful " runs: [{0,10,empty}]
  [2] Original[6..11]  "World"     runs: [{0,5,empty}]

Result: "Hello Beautiful World"
```

**Complexity improvements**:
| Operation | Flat array | Piece table |
|-----------|-----------|-------------|
| Substring | O(n) scan | O(log n) binary search on pieces |
| Insert/Replace | O(n) copy | O(1) amortized — add piece + split existing |
| Concat | O(a+b) copy | O(1) — append piece reference |
| Plain text | O(1) | O(n) materialization (or cached) |

**Trade-offs**:
- **Pro**: Substring and insert are dramatically cheaper; no intermediate string copies
- **Pro**: Structural sharing — edits reference the original without copying
- **Con**: Plain text access requires materialization (O(n) first access, cacheable)
- **Con**: Rendering must iterate pieces instead of a single contiguous scan
- **Con**: More complex implementation; piece fragmentation over many edits

**Best for**: Editing-heavy workloads (regex replace loops, repeated insert/remove).

**References**: VS Code uses a piece table for its text buffer. See [VS Code text buffer blog post](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation).

### 3.2 Rope (Balanced Binary Tree of Segments)

**Concept**: A balanced binary tree where leaves hold text segments with their attribute runs. Internal nodes cache the total character count of their subtree, enabling O(log n) positional lookup.

```
         [11]
        /    \
     [6]      [5]
     /  \       |
  "Hel" "lo "  "World"
  [red]  [red]  [blue]
```

**Complexity improvements**:
| Operation | Flat array | Rope |
|-----------|-----------|------|
| Substring | O(n) scan | O(log n) split |
| Concat | O(a+b) copy | O(log n) merge |
| Index-of-char | O(1) | O(log n) |
| Iteration | O(n) | O(n) with higher constant |
| Plain text | O(1) | O(n) materialization |

**Trade-offs**:
- **Pro**: O(log n) substring and concat — eliminates the O(n) scan
- **Pro**: Structural sharing — subtrees can be reused across operations
- **Pro**: Well-studied data structure with known balancing strategies (weight-balanced, AVL)
- **Con**: Higher per-element overhead (tree node allocations, pointers)
- **Con**: Poor cache locality compared to flat array (pointer chasing)
- **Con**: Rendering requires in-order traversal instead of linear scan
- **Con**: Significantly more complex to implement correctly

**Best for**: Deep operation chains where strings undergo many transformations before rendering.

**References**: Xi-editor's [Rope science](https://xi-editor.io/docs/rope_science_00.html) series. Haskell's `Data.Text` uses a similar structure.

### 3.3 Sorted Run Array with Binary Search

**Concept**: Keep the current flat model but use binary search instead of linear scan for positional lookups. Since runs are already sorted by `Start` position, `Array.BinarySearch` or a custom binary search finds the first overlapping run in O(log n).

```fsharp
let substring (start: int) (length: int) (ams: AttributedMarkupString) =
    // Binary search for first run overlapping [start, start+length)
    let firstIdx = binarySearchFirstOverlapping ams.Runs start
    // Scan only overlapping runs from firstIdx forward
    let mutable i = firstIdx
    while i < ams.Runs.Length && ams.Runs[i].Start < actualEnd do
        // ... clip and collect
        i <- i + 1
```

**Complexity improvements**:
| Operation | Current | With binary search |
|-----------|---------|-------------------|
| Substring (find start) | O(n) | O(log n) |
| Substring (collect) | O(k) | O(k) — same |
| Concat | O(a+b) | O(a+b) — same |
| Everything else | Same | Same |

**Trade-offs**:
- **Pro**: Minimal code change — just optimize the scan in `substring`
- **Pro**: No new data structures or allocation patterns
- **Pro**: Maintains all existing cache locality and simplicity benefits
- **Con**: Only helps substring; doesn't help concat or chained operations
- **Con**: Marginal benefit when run count is small (< 20 runs)

**Best for**: Incremental improvement with minimal risk. Recommended as an **immediate first step**.

### 3.4 Deferred / Lazy Operations (Operation Log)

**Concept**: Instead of eagerly computing each operation's result, record the operation in a log and only materialize when the result is read (rendering, plain text access). Similar to how database query optimizers defer execution.

```fsharp
type DeferredOp =
    | Substring of start: int * length: int
    | Concat of left: DeferredMarkupString * right: DeferredMarkupString
    | Replace of index: int * length: int * replacement: DeferredMarkupString

type DeferredMarkupString =
    | Materialized of AttributedMarkupString
    | Deferred of source: DeferredMarkupString * op: DeferredOp
```

When rendering or accessing plain text, the operation chain is **fused** and executed in a single pass, eliminating intermediate allocations.

**Complexity improvements**:
| Operation | Flat array | Deferred |
|-----------|-----------|----------|
| Substring | O(n) | O(1) — record only |
| Concat | O(a+b) | O(1) — record only |
| Replace | O(n) | O(1) — record only |
| Render / materialize | O(n) | O(n) — single fused pass |

**Trade-offs**:
- **Pro**: Eliminates all intermediate allocations from chained operations
- **Pro**: Operation chain can be optimized before materialization (e.g., adjacent substrings merged)
- **Pro**: Natural fit for the MUSH evaluation pipeline where strings undergo many transformations before output
- **Con**: Complexity of the materialization pass
- **Con**: Memory retention — deferred chains hold references to all source strings until materialized
- **Con**: Debugging is harder — inspecting a deferred string doesn't show its content
- **Con**: `Length` and `ToPlainText()` force materialization, losing the deferral benefit for code that checks length between operations

**Best for**: Long operation chains that end with a single render. Less useful if intermediate `Length` checks are frequent.

### 3.5 B-Tree of Runs (NSAttributedString's Internal Approach)

**Concept**: Apple's `NSAttributedString` internally uses a B-tree to store attribute runs, providing O(log n) lookup by position and efficient splitting/merging.

```
B-tree node (order 4):
  [  run₁  |  run₂  |  run₃  ]
  /    |        |        \
 ...  ...      ...       ...

Each node caches the total character count of its subtree.
Position lookup: O(log n) descent from root.
Split at position: O(log n) node splits.
```

**Complexity improvements**: Same as Rope (§3.2) but with better cache locality due to wider nodes.

**Trade-offs**:
- **Pro**: O(log n) for all positional operations
- **Pro**: Better cache locality than a binary tree (wider nodes fit cache lines)
- **Pro**: Proven approach — used by Apple's Foundation framework at scale
- **Con**: Most complex to implement correctly
- **Con**: F# doesn't have a built-in B-tree; would need a custom implementation or adaptation

**Best for**: Long-term investment if the flat model becomes a verified bottleneck.

---

## 4. Operation Ordering Concerns

### 4.1 Left-Fold Concat is O(n²)

`multipleWithDelimiter` currently uses `Seq.fold` with left-associative concat:

```fsharp
items |> Seq.fold (fun acc item -> concat (concat acc delimiter) item) None
```

This produces the classic quadratic left-fold pattern:
```
Step 1: "a" (length 1)
Step 2: "a" + "," + "b" → copy 1 + 1 + 1 = 3 chars
Step 3: "a,b" + "," + "c" → copy 3 + 1 + 1 = 5 chars
Step 4: "a,b,c" + "," + "d" → copy 5 + 1 + 1 = 7 chars
Total copies: 1 + 3 + 5 + 7 + ... ≈ O(n²)
```

**Recommendation**: Use a `StringBuilder`-like approach — collect all text into a single `StringBuilder` and all runs into a single `ImmutableArray.CreateBuilder`, then construct once:

```fsharp
let multipleWithDelimiter (delimiter: AttributedMarkupString) (items: AttributedMarkupString seq) =
    let textSb = StringBuilder()
    let runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>()
    let mutable first = true
    for item in items do
        if not first then
            // Append delimiter
            let offset = textSb.Length
            textSb.Append(delimiter.Text) |> ignore
            for run in delimiter.Runs do
                runsBuilder.Add({ run with Start = run.Start + offset })
        first <- false
        let offset = textSb.Length
        textSb.Append(item.Text) |> ignore
        for run in item.Runs do
            runsBuilder.Add({ run with Start = run.Start + offset })
    AttributedMarkupString(textSb.ToString(), runsBuilder.ToImmutable())
```

**Impact**: O(n) instead of O(n²) for joining n items. This is the **highest-impact single optimization** given that `multipleWithDelimiter` has 44 call sites and `split` has 67 (split results are frequently rejoined).

### 4.2 Replace-in-Loop Creates Cascading Copies

The regex replace pattern (AttributeFunctions.cs:960-963) rebuilds the entire string per match:

```csharp
// Per match iteration:
var before = MModule.substring(0, match.Index, mstr);           // copy before-text
var after  = MModule.substring(match.Index + match.Length, ...); // copy after-text
mstr = MModule.concat(MModule.concat(before, replacement), after);  // copy everything
```

For a string with n matches, total character copies ≈ O(n × string_length).

**Recommendation**: Build the result in a single pass using a builder:

```csharp
// Single-pass approach:
var textSb = new StringBuilder();
var runsBuilder = ImmutableArray.CreateBuilder<AttributeRun>();
int lastEnd = 0;
foreach (var match in matches) {
    // Copy unchanged segment before this match
    appendSegment(mstr, lastEnd, match.Index - lastEnd, textSb, runsBuilder);
    // Append replacement
    appendAms(replacement, textSb, runsBuilder);
    lastEnd = match.Index + match.Length;
}
// Copy remaining text after last match
appendSegment(mstr, lastEnd, mstr.Length - lastEnd, textSb, runsBuilder);
return new AttributedMarkupString(textSb.ToString(), runsBuilder.ToImmutable());
```

**Impact**: O(string_length) instead of O(n × string_length) for n replacements.

### 4.3 Pad Calls Repeat + Substring

`pad` creates a full repeated padding string and then truncates it:

```fsharp
let padding = repeat padStr repeatCount |> substring 0 lengthToPad
```

For padding a string to width 80 with a single-char pad, this creates a ~80-character repeated string with ~80 runs, then immediately substrings it. The repeated runs are created via exponential doubling (O(log n) concats), but each concat copies all text.

**Recommendation**: `pad` could construct the padding directly to the exact needed length without the intermediate full-length repeat:

```fsharp
let padding =
    let fullCopies = lengthToPad / padLen
    let partialLen = lengthToPad % padLen
    let builder = ImmutableArray.CreateBuilder<AttributeRun>()
    let textSb = StringBuilder(lengthToPad)
    for _ in 1 .. fullCopies do
        let offset = textSb.Length
        textSb.Append(padStr.Text) |> ignore
        for run in padStr.Runs do
            builder.Add({ run with Start = run.Start + offset })
    if partialLen > 0 then
        let offset = textSb.Length
        textSb.Append(padStr.Text.Substring(0, partialLen)) |> ignore
        // ... clip runs for partial copy
    AttributedMarkupString(textSb.ToString(), builder.ToImmutable())
```

---

## 5. Recommendation Summary

### Immediate (low-risk, high-impact)

| # | Change | Complexity | Impact |
|---|--------|-----------|--------|
| 1 | **Optimize `multipleWithDelimiter`** to single-pass builder | Low | O(n) vs O(n²) for list join — 44 call sites |
| 2 | **Binary search in `substring`** for first overlapping run | Low | O(log n) vs O(n) run scan — 42 call sites |
| 3 | **Optimize `pad`** to avoid intermediate repeat+truncate | Low | Eliminates wasted allocation for every pad operation |

### Medium-term (moderate complexity, structural improvement)

| # | Change | Complexity | Impact |
|---|--------|-----------|--------|
| 4 | **Single-pass `replaceAll`** operation | Medium | O(length) vs O(n × length) for multi-match regex replace |
| 5 | **Piece table** for editing-heavy paths | Medium | O(1) insert/replace, structural sharing |

### Long-term (high complexity, architectural)

| # | Change | Complexity | Impact |
|---|--------|-----------|--------|
| 6 | **Deferred operations** with fused materialization | High | Eliminates all intermediate allocations in chains |
| 7 | **Rope / B-tree** for runs | High | O(log n) for all positional operations |

### Decision Matrix

```
                    Implementation   Run-scan    Concat    Chained ops   Plain text
                    complexity       cost        cost      intermediates access
────────────────────────────────────────────────────────────────────────────────────
Flat array (current)  Low            O(n)        O(a+b)    Many          O(1)
+ Binary search       Low            O(log n+k)  O(a+b)    Many          O(1)
Piece table           Medium         O(log n)    O(1)*     Few           O(n)**
Rope / B-tree         High           O(log n)    O(log n)  Few           O(n)**
Deferred ops          High           O(1)***     O(1)***   Zero          O(n)**

  *  Amortized    ** Cacheable    *** Deferred until materialization
```

### Recommended Path

1. **Now**: Apply optimizations #1–3 (builder-based `multipleWithDelimiter`, binary search in `substring`, direct `pad` construction). These are purely internal changes with no API impact.

2. **Next**: Add a `replaceAll` operation (#4) that accepts a list of (index, length, replacement) tuples and executes in a single pass. This can coexist with the current `replace` function.

3. **Evaluate**: If profiling shows the flat array is still a bottleneck after #1–4, implement a piece table (#5). The piece table is the best balance of complexity vs. improvement for the MUSH workload pattern (heavy editing, single render at the end).

4. **Defer**: Rope/B-tree (#7) and deferred operations (#6) should only be considered if the piece table proves insufficient, or if the rendering pipeline moves to streaming (`IBufferWriter<char>`) where deferred ops can fuse directly into the output buffer.
