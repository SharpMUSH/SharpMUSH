# Xi Editor Rope & VS Code Piece Table: Deep Dive

A detailed exploration of the two most influential text-buffer data structures in modern editors, and how they apply to SharpMUSH's `AttributedMarkupString`.

---

## Table of Contents

1. [Xi Editor: Rope-Based Text Buffer](#1-xi-editor-rope-based-text-buffer)
2. [VS Code: Piece Table Text Buffer](#2-vs-code-piece-table-text-buffer)
3. [Mapping to AttributedMarkupString](#3-mapping-to-attributedmarkupstring)
4. [Comparison & Applicability](#4-comparison--applicability)
5. [Scenario Analysis: 32KB Average String Size](#5-scenario-analysis-32kb-average-string-size)

---

## 1. Xi Editor: Rope-Based Text Buffer

### 1.1 What Xi Editor Built

[Xi editor](https://xi-editor.io/) (2016–2021, by Raph Levien at Google) was an experimental text editor that pioneered using **ropes** — balanced binary trees of text segments — as its core text buffer. The design is documented in the [Rope Science](https://xi-editor.io/docs/rope_science_00.html) blog series.

The key insight: traditional editors store text as a single contiguous string or gap buffer. Ropes instead store text as **leaves of a balanced tree**, where internal nodes cache aggregate metrics (character count, line count, UTF-16 offset count). This makes positional operations O(log n) instead of O(n).

### 1.2 Core Data Structure

A rope is a **weight-balanced B-tree** where:
- **Leaves** hold text chunks (typically 511–1024 bytes)
- **Internal nodes** hold cached metrics of their subtree
- The tree is balanced by weight (total leaf bytes per subtree)

```
                    ┌─────────────────────────┐
                    │  Internal Node          │
                    │  chars: 26              │
                    │  lines: 1              │
                    │  weight: 26            │
                    └────────┬────────────────┘
                             │
                 ┌───────────┴───────────┐
                 │                       │
        ┌────────┴────────┐     ┌────────┴────────┐
        │  Internal       │     │  Leaf           │
        │  chars: 13      │     │  "World! Hello" │
        │  lines: 0       │     │  len: 13        │
        │  weight: 13     │     └─────────────────┘
        └────────┬────────┘
                 │
        ┌────────┴────────┐
        │                 │
   ┌────┴────┐    ┌───────┴──────┐
   │  Leaf   │    │  Leaf         │
   │  "Hello"│    │  ", World"   │
   │  len: 5 │    │  len: 8      │
   └─────────┘    └──────────────┘
```

### 1.3 Metrics Caching

Each internal node caches **aggregate metrics** for its subtree. This is the key to O(log n) operations:

```
                 ┌──────────────────────────┐
                 │  Metrics {               │
                 │    chars: 26,            │
                 │    lines: 2,            │
                 │    utf16_len: 26,       │
                 │    newline_offsets: [13] │
                 │  }                       │
                 └──────────┬───────────────┘
                            │
             ┌──────────────┴──────────────┐
             │                             │
   ┌─────────┴──────────┐      ┌──────────┴──────────┐
   │  Metrics {         │      │  Metrics {          │
   │    chars: 13,      │      │    chars: 13,       │
   │    lines: 1,       │      │    lines: 1,        │
   │    newlines: [5]   │      │    newlines: [7]    │
   │  }                 │      │  }                  │
   └─────────┬──────────┘      └──────────┬──────────┘
             │                             │
   ┌─────────┴──┐              ┌───────────┴──┐
   │ "Hello\n"  │              │ "World!\n"   │
   │ len: 6     │              │ len: 7       │
   └────────────┘              └──────────────┘
```

**Finding line 2** (zero-indexed) is O(log n): descend the tree, using cached line counts to pick left or right at each node.

### 1.4 Operation: Substring (Split)

Splitting a rope at position `p` produces two ropes that **share structure** with the original:

```
Original rope (26 chars):

           ┌────[26]────┐
           │             │
      ┌──[13]──┐     ┌──[13]──┐
      │        │     │        │
   "Hello, " "World"  "! " "Have fun"
     [7]      [5]     [2]    [8]

Split at position 12 ("Hello, World"):

Left rope (12 chars):          Right rope (14 chars):

     ┌──[12]──┐                      ┌──[14]──┐
     │        │                      │        │
  "Hello, " "World"              "! " "Have fun"    ← SHARED leaf
    [7]      [5]  ▲              [2]    [8]   ▲
                  │                            │
                  └── This leaf "World" is SHARED (immutable, no copy needed)
                       Only path nodes are re-created
```

**Key property**: Splitting creates O(log n) new internal nodes but **shares all unaffected leaves**. No text is copied — only tree structure is adjusted.

**Cost**: O(log n) time, O(log n) new allocations (just path nodes).

Compare to flat array: O(n) scan of all runs + O(length) string copy.

### 1.5 Operation: Concat (Merge)

Merging two ropes creates a new root with the two ropes as children, then rebalances:

```
Rope A:          Rope B:

  ┌─[5]─┐        ┌─[6]─┐
  │      │        │      │
"Hel"  "lo"    "World" "!"
 [3]   [2]      [5]   [1]

After concat(A, B):

        ┌────[11]────┐
        │             │
   ┌──[5]──┐    ┌──[6]──┐      ← Subtrees A and B are SHARED
   │       │    │       │
 "Hel"  "lo"  "World" "!"
  [3]   [2]    [5]   [1]
```

**Cost**: O(log n) for rebalancing. No text is copied — the new root just points to the existing subtrees.

Compare to flat array: O(|a| + |b|) string copy + O(a.runs + b.runs) run array copy.

### 1.6 Operation: Insert

Inserting text at position `p` is split + concat + concat:

```
Original: "Hello World"     Insert "Beautiful " at position 6

Step 1: Split at 6
  Left:  "Hello "      Right: "World"

Step 2: Create leaf for insertion
  New leaf: "Beautiful "

Step 3: Concat(Left, New leaf)
  Result: "Hello Beautiful "

Step 4: Concat(Result, Right)
  Final: "Hello Beautiful World"
```

```
                ┌───────[21]───────┐
                │                  │
        ┌─────[16]──────┐      "World"    ← SHARED from original
        │               │        [5]
   ┌──[6]──┐      "Beautiful "
   │       │          [10]
 "Hello"  " "
  [5]     [1]
    ↑       ↑
    └───────── SHARED from original split
```

**Cost**: O(log n) — three O(log n) operations (split + concat + concat).

Compare to flat array: O(n) string copy + O(n) run array rebuild.

### 1.7 Xi's Copy-on-Write (COW) Approach

Xi uses **persistent data structures** — every edit creates a new version of the tree while sharing structure with the previous version:

```
Version 1: "Hello World"        Version 2: "Hello Beautiful World"

    ┌──[11]──┐                       ┌─────[21]──────┐
    │        │                       │               │
 "Hello "  "World"              ┌──[16]──┐       "World" ←─── SHARED
   [6]      [5]                 │        │         [5]     between V1 & V2
     ↑                       "Hello " "Beautiful "
     │                         [6]       [10]
     └─── SHARED ────────────────┘
```

Both versions can coexist simultaneously in memory. This is essential for:
- **Undo**: Keep the previous tree version
- **Concurrent access**: Read from old version while writing a new one
- **CRDT**: Xi extended ropes with CRDT metadata for collaborative editing

### 1.8 Xi's Rope with Attributes

Xi extended the rope to carry **styling intervals** alongside text. Each leaf can have associated attribute spans:

```
Leaf node:
  ┌─────────────────────────────────┐
  │ text: "Hello World"             │
  │ len: 11                         │
  │ styles: [                       │
  │   { start: 0, end: 5,          │
  │     attrs: [bold] },            │
  │   { start: 6, end: 11,         │
  │     attrs: [italic] }           │
  │ ]                               │
  └─────────────────────────────────┘

  Visualization:
  H e l l o   W o r l d
  ├─bold──┤   ├italic──┤
```

When a leaf is split, the attribute spans are split and distributed to the new leaves — still O(log n) per operation.

### 1.9 Mermaid: Rope Structure

```mermaid
graph TD
    Root["Internal<br/>chars: 26<br/>lines: 2"]
    L["Internal<br/>chars: 13"]
    R["Internal<br/>chars: 13"]
    LL["Leaf: 'Hello, '<br/>len: 7"]
    LR["Leaf: 'World!'<br/>len: 6"]
    RL["Leaf: ' Hav'<br/>len: 4"]
    RR["Leaf: 'e fun!'<br/>len: 6"]

    Root --> L
    Root --> R
    L --> LL
    L --> LR
    R --> RL
    R --> RR

    style LL fill:#e8f5e9
    style LR fill:#e8f5e9
    style RL fill:#e8f5e9
    style RR fill:#e8f5e9
    style L fill:#fff3e0
    style R fill:#fff3e0
    style Root fill:#e3f2fd
```

### 1.10 Mermaid: Rope Split Operation

```mermaid
graph TD
    subgraph "Before Split at position 13"
        A_Root["[26]"] --> A_L["[13]"]
        A_Root --> A_R["[13]"]
        A_L --> A_LL["'Hello, '<br/>7"]
        A_L --> A_LR["'World!'<br/>6"]
        A_R --> A_RL["' Hav'<br/>4"]
        A_R --> A_RR["'e fun!'<br/>6"]
    end

    subgraph "After Split → Left Rope"
        B_Root["[13]"] --> B_LL["'Hello, '<br/>7 ★shared"]
        B_Root --> B_LR["'World!'<br/>6 ★shared"]
    end

    subgraph "After Split → Right Rope"
        C_Root["[13]"] --> C_RL["' Hav'<br/>4 ★shared"]
        C_Root --> C_RR["'e fun!'<br/>6 ★shared"]
    end

    style A_LL fill:#e8f5e9
    style A_LR fill:#e8f5e9
    style A_RL fill:#e8f5e9
    style A_RR fill:#e8f5e9
    style B_LL fill:#c8e6c9
    style B_LR fill:#c8e6c9
    style C_RL fill:#c8e6c9
    style C_RR fill:#c8e6c9
```

---

## 2. VS Code: Piece Table Text Buffer

### 2.1 History & Motivation

VS Code initially used a **line array** — an array of strings, one per line. In 2018, they replaced it with a **piece table** to solve performance problems with large files and frequent edits. The design is documented in the [VS Code text buffer reimplementation blog post](https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation).

The original piece table concept comes from **Charles Crowley's 1998 paper** "Data Structures for Text Sequences." VS Code's innovation was combining the piece table with a **red-black tree** for O(log n) positional lookups.

### 2.2 Core Concept

A piece table maintains:
1. **Original buffer**: The initial text content (immutable, never modified)
2. **Add buffer**: An append-only buffer for all inserted text (also never modified once written)
3. **Piece table**: An ordered sequence of "pieces", each pointing to a span in either the original or add buffer

```
┌─────────────────────────────────────────────────────────────┐
│                        PIECE TABLE                           │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ Original Buffer (immutable):                           │  │
│  │ "This is a document with some text."                   │  │
│  │  0123456789012345678901234567890123456                  │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │ Add Buffer (append-only):                              │  │
│  │ "wonderful "(empty... grows with edits)                │  │
│  │  0123456789                                             │  │
│  └────────────────────────────────────────────────────────┘  │
│                                                              │
│  Pieces (in order):                                          │
│  ┌──────────────────────────────────────────────────────┐    │
│  │ [0] Original [0..10)   → "This is a "               │    │
│  │ [1] Add      [0..10)   → "wonderful "               │    │
│  │ [2] Original [10..36)  → "document with some text." │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  Logical text: "This is a wonderful document with some text."│
└─────────────────────────────────────────────────────────────┘
```

### 2.3 Step-by-Step Edit Sequence

Let's trace through three edits on the text `"Hello World"`:

#### Initial State

```
Original: "Hello World"
Add:      ""

Pieces: ┌────────────────────────┐
        │ [0] Orig [0..11) = "Hello World" │
        └────────────────────────┘

Logical text: "Hello World"
```

#### Edit 1: Insert "Beautiful " at position 6

The piece at index 0 spans [0..11). Position 6 falls inside it, so we **split** it and insert a new piece:

```
Original: "Hello World"         (unchanged)
Add:      "Beautiful "           (appended)

Pieces: ┌────────────────────────────────┐
        │ [0] Orig [0..6)   = "Hello "   │  ← left half of split
        │ [1] Add  [0..10)  = "Beautiful "│  ← inserted text
        │ [2] Orig [6..11)  = "World"    │  ← right half of split
        └────────────────────────────────┘

Logical text: "Hello Beautiful World"
          positions: 0─────5 6─────────15 16───20
```

**Key**: No text was copied or moved. The original buffer is untouched. We only created metadata (3 small piece descriptors).

#### Edit 2: Delete "Beautiful " (positions 6–15)

Delete piece [1] entirely:

```
Original: "Hello World"         (unchanged)
Add:      "Beautiful "           (unchanged — never deleted from)

Pieces: ┌────────────────────────────────┐
        │ [0] Orig [0..6)   = "Hello "   │
        │ [1] Orig [6..11)  = "World"    │
        └────────────────────────────────┘

Logical text: "Hello World"
```

**Key**: The add buffer still contains "Beautiful " — it's just not referenced by any piece. This append-only design means the add buffer can support undo by re-adding a piece reference.

#### Edit 3: Replace "World" with "MUSH"

Split piece [1] and insert new text:

```
Original: "Hello World"         (unchanged)
Add:      "Beautiful MUSH"      (appended "MUSH")

Pieces: ┌────────────────────────────────┐
        │ [0] Orig [0..6)   = "Hello "   │
        │ [1] Add  [10..14) = "MUSH"     │  ← points into add buffer
        └────────────────────────────────┘

Logical text: "Hello MUSH"
```

### 2.4 VS Code's Red-Black Tree Enhancement

The naive piece table uses a **linked list** of pieces, making positional lookup O(n). VS Code enhances this with a **red-black tree** where each node is a piece, and each node caches:

- **Left subtree size** (total characters in left children)
- **Left subtree line feed count** (for line-based lookup)

```
              ┌──────────────────────────────┐
              │  Root (Black)                │
              │  piece: Add[0..10)           │
              │  text: "Beautiful "           │
              │  left_size: 6                │
              │  left_lf: 0                  │
              └───────────┬──────────────────┘
                          │
            ┌─────────────┴─────────────┐
            │                           │
   ┌────────┴────────┐       ┌──────────┴─────────┐
   │  Left (Red)     │       │  Right (Red)       │
   │  Orig[0..6)     │       │  Orig[6..11)       │
   │  "Hello "       │       │  "World"           │
   │  left_size: 0   │       │  left_size: 0      │
   │  left_lf: 0     │       │  left_lf: 0        │
   └─────────────────┘       └────────────────────┘
```

**Lookup by character position**: Descend the tree, comparing the target position against `left_size + current_piece_length` to decide left/right — O(log n).

**Lookup by line number**: Same descent using `left_lf` + current piece's line feed count — O(log n).

### 2.5 Mermaid: Piece Table Structure

```mermaid
graph TD
    subgraph "Buffers (immutable)"
        OB["Original Buffer<br/>'Hello World'<br/>positions 0-10"]
        AB["Add Buffer<br/>'Beautiful MUSH'<br/>positions 0-13"]
    end

    subgraph "Red-Black Tree of Pieces"
        P1["Piece: Orig[0,6)<br/>'Hello '<br/>left_size: 0"]
        P2["Piece: Add[0,10)<br/>'Beautiful '<br/>left_size: 6"]
        P3["Piece: Orig[6,11)<br/>'World'<br/>left_size: 0"]

        P2 --> P1
        P2 --> P3
    end

    P1 -.-> OB
    P2 -.-> AB
    P3 -.-> OB

    style OB fill:#e3f2fd
    style AB fill:#fff3e0
    style P1 fill:#e8f5e9
    style P2 fill:#ffcdd2
    style P3 fill:#e8f5e9
```

### 2.6 Mermaid: Edit Sequence Flow

```mermaid
graph LR
    subgraph "State 0: Initial"
        S0["Pieces:<br/>[Orig 0..11]<br/>'Hello World'"]
    end

    subgraph "State 1: Insert 'Beautiful ' at 6"
        S1["Pieces:<br/>[Orig 0..6] 'Hello '<br/>[Add 0..10] 'Beautiful '<br/>[Orig 6..11] 'World'"]
    end

    subgraph "State 2: Delete positions 6-15"
        S2["Pieces:<br/>[Orig 0..6] 'Hello '<br/>[Orig 6..11] 'World'"]
    end

    subgraph "State 3: Replace 'World' with 'MUSH'"
        S3["Pieces:<br/>[Orig 0..6] 'Hello '<br/>[Add 10..14] 'MUSH'"]
    end

    S0 -->|"insert"| S1
    S1 -->|"delete"| S2
    S2 -->|"replace"| S3

    style S0 fill:#e8f5e9
    style S1 fill:#fff3e0
    style S2 fill:#e3f2fd
    style S3 fill:#fce4ec
```

### 2.7 Why VS Code Chose Piece Table Over Rope

From the VS Code blog post, the team benchmarked five approaches:

```
┌─────────────────────────────────────────────────────────────────────┐
│              VS Code's Benchmark Results (2018)                     │
│                                                                     │
│  Data Structure        Memory    File Open   Edit Perf   Seek      │
│  ─────────────────────────────────────────────────────────────────  │
│  Line Array (old)      ★★★★☆    ★★★★★       ★☆☆☆☆      ★★★★★    │
│  Piece Table           ★★★★★    ★★★★★       ★★★★★      ★★★★☆    │
│  Rope                  ★★★☆☆    ★★★★☆       ★★★★☆      ★★★★☆    │
│  Gap Buffer            ★★★★☆    ★★★★★       ★★★★☆      ★★★☆☆    │
│  Red-Black + Buffers   ★★★★★    ★★★★★       ★★★★★      ★★★★★    │
│  (piece table + RB)                                                 │
│                                                                     │
│  ★★★★★ = Excellent   ★☆☆☆☆ = Poor                                 │
│                                                                     │
│  Winner: Piece Table with Red-Black Tree (current VS Code impl)     │
└─────────────────────────────────────────────────────────────────────┘
```

Key findings:
- **Rope** had **higher memory overhead** per node (tree pointers + metrics)
- **Piece table** had **better memory efficiency** because buffers are contiguous
- **Piece table with RB tree** matched rope's O(log n) operations with less overhead
- **Gap buffer** was fast for local edits but O(n) for random-position edits

### 2.8 Piece Table: Insert/Delete Complexity

```
┌─────────────────────────────────────────────────────────────┐
│  Insert at position p:                                       │
│                                                              │
│  1. Find the piece containing position p     → O(log n)     │
│  2. Split that piece into two pieces         → O(1)         │
│  3. Append new text to add buffer            → O(k) amort.  │
│  4. Create new piece for the inserted text   → O(1)         │
│  5. Insert new piece into RB tree            → O(log n)     │
│  6. Rebalance RB tree                        → O(log n)     │
│                                                              │
│  Total: O(log n + k)  where k = length of inserted text     │
│                                                              │
│  Compare: Flat array insert = O(|string| + |runs|)          │
├─────────────────────────────────────────────────────────────┤
│  Delete range [start, end):                                  │
│                                                              │
│  1. Find piece containing start              → O(log n)     │
│  2. Find piece containing end                → O(log n)     │
│  3. Split start-piece at start offset        → O(1)         │
│  4. Split end-piece at end offset            → O(1)         │
│  5. Remove pieces fully within range         → O(log n) ea. │
│  6. Rebalance RB tree                        → O(log n)     │
│                                                              │
│  Total: O(m·log n) where m = pieces in range                │
│  Typically m is small (1-3 pieces)                           │
│                                                              │
│  Compare: Flat array delete = O(|string| + |runs|)          │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Mapping to AttributedMarkupString

### 3.1 The Challenge: Markup Runs + Text

`AttributedMarkupString` carries two parallel structures:
1. **Text**: A contiguous .NET `string`
2. **Runs**: `ImmutableArray<AttributeRun>` where each run has `{Start, Length, Markups}`

Any alternative data structure must carry markup information alongside text. Here's how each approach maps:

### 3.2 Rope Approach for AttributedMarkupString

Each leaf holds a text chunk **and** its associated markup runs:

```
┌─────────────────────────────────────────────────────────┐
│  RopeLeaf {                                              │
│    text: "Hello "                                        │
│    runs: [{start:0, len:5, markups:[red,bold]},          │
│           {start:5, len:1, markups:[]}]                   │
│  }                                                       │
│                                                          │
│  Visualization:                                          │
│  H e l l o ·                                             │
│  ├red,bold┤                                              │
└─────────────────────────────────────────────────────────┘
```

Splitting a leaf splits both the text and the runs:

```
Split leaf at offset 3:

Before:  ┌────────────────────────────────────────┐
         │ text: "Hello "                          │
         │ runs: [{0,5,[red,bold]}, {5,1,[]}]      │
         └────────────────────────────────────────┘

After:   ┌────────────────────┐  ┌────────────────────┐
         │ text: "Hel"         │  │ text: "lo "         │
         │ runs: [{0,3,[r,b]}] │  │ runs: [{0,2,[r,b]}, │
         └────────────────────┘  │        {2,1,[]}]     │
                                 └────────────────────┘

The run {0,5,[red,bold]} was split into {0,3,[r,b]} and {0,2,[r,b]}.
Offsets are adjusted to be relative to each new leaf.
```

**Concat** creates a new root; if adjacent leaves have compatible runs at the boundary, they can be merged:

```
Concat:
  Leaf A: text="Hel", runs=[{0,3,[bold]}]
  Leaf B: text="lo",  runs=[{0,2,[bold]}]

  Adjacent runs share [bold] → can merge:
  Result leaf: text="Hello", runs=[{0,5,[bold]}]
```

### 3.3 Piece Table Approach for AttributedMarkupString

Each piece references a text span **and** carries its own markup runs (local to the piece):

```
┌─────────────────────────────────────────────────────────────┐
│  Original Buffer: "Hello World"                              │
│  Add Buffer:      "Beautiful "                               │
│                                                              │
│  Piece Table:                                                │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ Piece 0:                                                │ │
│  │   buffer: Original, offset: 0, length: 6                │ │
│  │   text: "Hello "                                        │ │
│  │   runs: [{start:0, len:5, markups:[red,bold]},          │ │
│  │          {start:5, len:1, markups:[]}]                   │ │
│  ├─────────────────────────────────────────────────────────┤ │
│  │ Piece 1:                                                │ │
│  │   buffer: Add, offset: 0, length: 10                    │ │
│  │   text: "Beautiful "                                    │ │
│  │   runs: [{start:0, len:10, markups:[italic]}]            │ │
│  ├─────────────────────────────────────────────────────────┤ │
│  │ Piece 2:                                                │ │
│  │   buffer: Original, offset: 6, length: 5                │ │
│  │   text: "World"                                         │ │
│  │   runs: [{start:0, len:5, markups:[blue]}]               │ │
│  └─────────────────────────────────────────────────────────┘ │
│                                                              │
│  Logical text: "Hello Beautiful World"                       │
│                ├red,bold┤├─italic──┤├blue┤                  │
└─────────────────────────────────────────────────────────────┘
```

When splitting a piece (for insert/delete), the markup runs within that piece are also split — same as the rope approach, but within a flat piece rather than a tree leaf.

### 3.4 Rendering with Each Approach

```
┌────────────────────────────────────────────────────────────────┐
│  RENDERING COMPARISON                                          │
│                                                                │
│  Current (flat array):                                         │
│    for run in runs do                                          │
│      let segment = text.Substring(run.Start, run.Length)       │
│      // Apply markups to segment                               │
│      sb.Append(rendered) |> ignore                             │
│    → O(n) with excellent cache locality (contiguous array)     │
│                                                                │
│  Rope:                                                         │
│    let rec renderInOrder node =                                │
│      match node with                                           │
│      | Leaf(text, runs) -> renderRuns(text, runs)              │
│      | Internal(left, right) ->                                │
│          renderInOrder left                                    │
│          renderInOrder right                                   │
│    → O(n) but with pointer chasing (worse cache locality)      │
│                                                                │
│  Piece table:                                                  │
│    for piece in rbTreeInOrder do                               │
│      renderRuns(piece.text, piece.runs)                        │
│    → O(n) with decent locality (pieces are larger than         │
│      rope leaves, so fewer indirections)                       │
└────────────────────────────────────────────────────────────────┘
```

---

## 4. Comparison & Applicability

### 4.1 Operation Complexity Comparison

```
┌──────────────────────────────────────────────────────────────────────┐
│                    COMPLEXITY COMPARISON                             │
│                                                                      │
│  Operation          Flat Array    Rope         Piece Table (+ RB)   │
│  ─────────────────────────────────────────────────────────────────── │
│  Substring          O(n+len)      O(log n)     O(log n)             │
│  Concat             O(a+b)        O(log n)     O(log n)             │
│  Insert at pos      O(n)          O(log n)     O(log n)             │
│  Delete range       O(n)          O(log n)     O(log n)             │
│  Index by char      O(1)          O(log n)     O(log n)             │
│  Index by line       —            O(log n)     O(log n)             │
│  Plain text access  O(1)          O(n) *       O(n) *               │
│  Rendering          O(n)          O(n)         O(n)                 │
│  Memory overhead    Low           High         Medium               │
│  Cache locality     Excellent     Poor         Good                 │
│  Structural sharing None          Full         Partial **           │
│  Implementation     Simple        Complex      Medium               │
│  Immutability       Yes ***       Yes          Partial ****         │
│                                                                      │
│  *     Can be cached / lazily materialized                          │
│  **    Buffers are shared; pieces are new per edit                  │
│  ***   Current implementation uses ImmutableArray                   │
│  ****  Buffers are immutable; piece table metadata is mutable       │
│        (but can be made immutable with persistent RB tree)          │
└──────────────────────────────────────────────────────────────────────┘
```

### 4.2 Memory Layout Comparison

```
FLAT ARRAY (current):
┌─────────────────────────────────────────────────┐
│ string: "Hello Beautiful World"                  │  ← one contiguous allocation
│ runs:   [{0,5,[r,b]},{5,1,[]},{6,10,[i]},...]   │  ← one contiguous array
└─────────────────────────────────────────────────┘
Cache-friendly: sequential memory access during rendering

ROPE:
┌──────┐    ┌──────┐    ┌──────┐    ┌──────┐
│Node 1│───→│Node 2│    │Node 3│───→│Node 4│
│"Hello"    │", "  │    │"Beau"│    │"tifu"│
│ runs │    │ runs │    │ runs │    │ runs │
└──────┘    └──────┘    └──────┘    └──────┘
    ↑           ↑           ↑           ↑
    └───────────┴───────────┴───────────┘
    Scattered in memory — pointer chasing during rendering

PIECE TABLE:
┌────────────────────────────────────────┐  ← Original buffer (contiguous)
│ "Hello World"                          │
└────────────────────────────────────────┘
┌────────────────────────────────────────┐  ← Add buffer (contiguous)
│ "Beautiful "                           │
└────────────────────────────────────────┘
┌──────┐  ┌──────┐  ┌──────┐              ← RB tree nodes (small metadata)
│Piece1│  │Piece2│  │Piece3│
│Orig  │  │Add   │  │Orig  │
│0..6  │  │0..10 │  │6..11 │
└──────┘  └──────┘  └──────┘
Good locality: buffers are contiguous; pieces are small metadata
```

### 4.3 Mermaid: Decision Flowchart for SharpMUSH

```mermaid
flowchart TD
    A["Is the flat array a<br/>measured bottleneck?"] -->|No| B["Keep flat array +<br/>apply optimizations §4.1-4.3<br/>from efficiency analysis"]
    A -->|Yes| C{"Primary bottleneck?"}
    C -->|"substring/concat<br/>in loops"| D["Piece Table<br/>(§3.1 of efficiency doc)<br/>Best: edit-heavy, single render"]
    C -->|"deep operation chains<br/>before render"| E["Deferred Operations<br/>(§3.4 of efficiency doc)<br/>Best: chain → render pattern"]
    C -->|"concurrent access<br/>or undo needed"| F["Rope with COW<br/>(§3.2 of efficiency doc)<br/>Best: structural sharing"]

    D --> G["Piece Table with<br/>per-piece markup runs<br/>+ RB tree (VS Code style)"]
    E --> H["Operation log with<br/>fused materialization"]
    F --> I["Persistent rope with<br/>per-leaf markup runs<br/>(Xi editor style)"]

    style B fill:#c8e6c9
    style G fill:#fff3e0
    style H fill:#e3f2fd
    style I fill:#fce4ec
```

### 4.4 SharpMUSH-Specific Applicability

```
┌──────────────────────────────────────────────────────────────────┐
│  SharpMUSH Workload Characteristics:                             │
│                                                                  │
│  1. Strings are typically SHORT (< 8KB, usually < 1KB)           │
│  2. Operations are CHAINED (split → process → rejoin)            │
│  3. Rendering happens ONCE at the end                            │
│  4. Markup runs are usually FEW (< 20 per string)               │
│  5. Strings must be IMMUTABLE (concurrent game state)            │
│  6. No undo needed (one-shot evaluation pipeline)                │
│  7. No collaborative editing (single server)                     │
│                                                                  │
│  Assessment:                                                     │
│                                                                  │
│  Xi Rope:   OVERKILL — Designed for 100MB+ files with           │
│             concurrent editing. The tree overhead exceeds         │
│             the flat-array waste for SharpMUSH's small strings.  │
│             Structural sharing is valuable but the per-node      │
│             cost is too high for strings < 1KB.                  │
│                                                                  │
│  VS Code Piece Table:  GOOD FIT — The "append-only + split"     │
│             model maps well to the MUSH split→process→rejoin     │
│             pattern. Pieces share buffer references, avoiding    │
│             the O(n) copies in substring+concat chains.          │
│             The RB tree is worth it only if piece count > ~20.   │
│                                                                  │
│  Simplified Piece Table (no RB tree):  BEST FIT — For           │
│             SharpMUSH's typical string sizes, a simple           │
│             piece list/array with linear scan is faster than     │
│             an RB tree due to cache locality. Use binary search  │
│             with cached offsets for O(log n) if needed.          │
│                                                                  │
│  RECOMMENDED: Start with flat-array optimizations (#1-3 from    │
│  the efficiency analysis). If profiling confirms a bottleneck,  │
│  implement a simplified piece table without the RB tree.        │
│  The RB tree and rope are both overengineered for typical        │
│  MUSH string sizes.                                              │
│                                                                  │
│  NOTE: If average string sizes reach 32KB (e.g., large          │
│  attribute values, generated maps, accumulated logs), the        │
│  recommendation changes to PIECE TABLE. See §5 for detailed     │
│  analysis of the 32KB scenario.                                  │
└──────────────────────────────────────────────────────────────────┘
```

### 4.5 Mermaid: Piece Table for AttributedMarkupString

```mermaid
graph TD
    subgraph "AttributedMarkupString with Piece Table"
        subgraph "Immutable Buffers"
            OB["Original Buffer<br/>'Hello World'"]
            AB["Add Buffer<br/>'Beautiful '"]
        end

        subgraph "Piece Array (ImmutableArray)"
            P0["Piece 0<br/>buf: Orig, off: 0, len: 6<br/>runs: [{0,5,[red,bold]},{5,1,[]}]"]
            P1["Piece 1<br/>buf: Add, off: 0, len: 10<br/>runs: [{0,10,[italic]}]"]
            P2["Piece 2<br/>buf: Orig, off: 6, len: 5<br/>runs: [{0,5,[blue]}]"]
        end

        P0 -.->|"text: 'Hello '"| OB
        P1 -.->|"text: 'Beautiful '"| AB
        P2 -.->|"text: 'World'"| OB
    end

    subgraph "Rendering"
        R["Single pass:<br/>iterate pieces in order,<br/>render each piece's runs<br/>against its buffer slice"]
    end

    P0 --> R
    P1 --> R
    P2 --> R

    style OB fill:#e3f2fd
    style AB fill:#fff3e0
    style P0 fill:#e8f5e9
    style P1 fill:#e8f5e9
    style P2 fill:#e8f5e9
    style R fill:#f3e5f5
```

### 4.6 Summary Table

| Criterion | Flat Array (Current) | Xi Rope | VS Code Piece Table | Simplified Piece Table |
|-----------|---------------------|---------|--------------------|-----------------------|
| **Complexity** | Simple | High | Medium | Low-Medium |
| **Substring** | O(n) scan | O(log n) | O(log n) | O(n) scan but no copy |
| **Concat** | O(a+b) copy | O(log n) | O(log n) | O(1) amortized |
| **Insert** | O(n) copy | O(log n) | O(log n) | O(n) piece shift |
| **Plain text** | O(1) | O(n) cache | O(n) cache | O(n) cache |
| **Render** | O(n) great cache | O(n) poor cache | O(n) good cache | O(n) good cache |
| **Memory** | Low | High (nodes) | Medium (metadata) | Low-Medium |
| **Immutable** | ✅ ImmutableArray | ✅ Persistent | ⚠️ Needs work | ✅ If using ImmutableArray |
| **Best for** | Small strings, few edits | Large files, undo, collab | Large files, many edits | Medium strings, chain ops |
| **SharpMUSH fit** | ✅ Current | ❌ Overkill | ⚠️ If bottleneck proven | ✅ If flat array insufficient |

---

## 5. Scenario Analysis: 32KB Average String Size

The analysis in §4.4 assumed typical MUSH strings are <1KB with <20 markup runs. But what if MUSH strings averaged **32KB** — e.g., large attribute values storing generated maps, formatted help screens, concatenated room descriptions, or accumulated log buffers?

At 32KB, the cost model shifts dramatically. This section re-evaluates each approach under that assumption.

### 5.1 Why 32KB Changes Everything

```
┌──────────────────────────────────────────────────────────────────────┐
│  FLAT ARRAY COSTS AT 32KB                                            │
│                                                                      │
│  String copy per operation:                                          │
│    32,768 bytes × 2 bytes/char = 65,536 bytes copied per concat     │
│                                                                      │
│  Runs at 32KB (estimated):                                           │
│    Heavy markup: 1 run per ~50 chars ≈ 655 runs                     │
│    Moderate markup: 1 run per ~200 chars ≈ 164 runs                 │
│    Light markup: 1 run per ~1000 chars ≈ 33 runs                    │
│                                                                      │
│  Run scan per substring:                                             │
│    Heavy: scan 655 runs (26KB of AttributeRun structs)              │
│    Moderate: scan 164 runs (6.4KB of structs)                       │
│    Light: scan 33 runs (1.3KB of structs)                           │
│                                                                      │
│  Regex replace loop (50 matches on a 32KB string):                   │
│    50 × (2 substring scans + 2 string copies + 2 run array builds)  │
│    = 50 × (2 × 65KB copy + 2 × 655-run scan)                       │
│    = 6.5 MB of string copies + 65,500 run comparisons               │
│    All intermediate allocations are immediately GC'd                 │
│                                                                      │
│  multipleWithDelimiter (join 100 items averaging 320 bytes each):    │
│    Left-fold: 320 + 960 + 1600 + ... ≈ O(n²)                       │
│    = ~1.6 MB of cumulative string copies                             │
└──────────────────────────────────────────────────────────────────────┘
```

At <1KB, these costs are negligible (microseconds). At 32KB, they become **the dominant cost** in the evaluation pipeline.

### 5.2 Concrete Operation Comparison at 32KB

```
┌──────────────────────────────────────────────────────────────────────┐
│                   COST PER OPERATION (32KB string, ~200 runs)        │
│                                                                      │
│  Operation         Flat Array          Piece Table       Rope        │
│  ────────────────────────────────────────────────────────────────── │
│  Substring         65KB copy           0 bytes copy     0 bytes copy│
│  (extract 500b)    + 200-run scan      + O(log n) find  + O(log n)  │
│                    + new 200-run arr   + 1 piece split   split       │
│                    ≈ 67KB alloc        ≈ 128B alloc     ≈ 256B alloc│
│                                                                      │
│  Concat            65KB + 65KB copy    0 bytes copy     0 bytes copy│
│  (two 32KB strs)   + 400-run merge    + 1 RB insert    + 1 rebal   │
│                    ≈ 137KB alloc       ≈ 64B alloc      ≈ 128B alloc│
│                                                                      │
│  Replace 50×       6.5MB total copy    0 bytes copy     0 bytes copy│
│  (regex matches)   + 20K run scans    + 50 RB ops      + 150 splits │
│                    ≈ 6.7MB alloc       ≈ 7KB alloc      ≈ 19KB alloc│
│                                                                      │
│  Split + Rejoin    ~65KB × n scans     O(log n) per    O(log n) per │
│  (100 items)       + O(n²) concat     + O(n) build     + O(n log n) │
│                    ≈ 1.6MB alloc       ≈ 13KB alloc     ≈ 25KB alloc│
│                                                                      │
│  ALLOCATION RATIO (flat array as baseline):                          │
│  Substring:        1×                  0.002×           0.004×      │
│  Concat:           1×                  0.0005×          0.001×      │
│  Replace 50×:      1×                  0.001×           0.003×      │
│  Split+Rejoin:     1×                  0.008×           0.016×      │
└──────────────────────────────────────────────────────────────────────┘
```

The piece table achieves **500× to 2000× less allocation** than the flat array for editing operations on 32KB strings. The rope is 2-3× worse than piece table (more node allocations) but still orders of magnitude better than flat array.

### 5.3 Memory Layout at 32KB

```
FLAT ARRAY — after 5 substring+concat operations on a 32KB string:

  ┌─ GC Gen 0 pressure ────────────────────────────────────────────┐
  │                                                                 │
  │  [string 65KB] ← immediately dead                              │
  │  [runs[] 6.4KB] ← immediately dead                             │
  │  [string 65KB] ← immediately dead                              │
  │  [runs[] 6.4KB] ← immediately dead                             │
  │  [string 65KB] ← immediately dead                              │
  │  [runs[] 6.4KB] ← immediately dead                             │
  │  [string 65KB] ← immediately dead                              │
  │  [runs[] 6.4KB] ← immediately dead                             │
  │  [string 65KB] ← LIVE (current result)                         │
  │  [runs[] 6.4KB] ← LIVE (current result)                        │
  │                                                                 │
  │  Total allocated:  ~715 KB                                      │
  │  Still live:       ~71 KB                                       │
  │  GC pressure:      ~644 KB of garbage per 5-op chain            │
  └─────────────────────────────────────────────────────────────────┘

PIECE TABLE — same 5 operations:

  ┌─ Persistent buffers ───────────────────────────────────────────┐
  │                                                                 │
  │  [Original buffer 65KB] ← SHARED across all versions           │
  │  [Add buffer ~2KB]      ← append-only, grows with inserts      │
  │                                                                 │
  │  [Piece×7 ~448B]        ← current piece table (RB tree nodes)  │
  │                                                                 │
  │  Total allocated:  ~67.5 KB                                     │
  │  Still live:       ~67.5 KB (buffers are shared)                │
  │  GC pressure:      ~0.5 KB (only old piece table versions)     │
  └─────────────────────────────────────────────────────────────────┘

ROPE — same 5 operations:

  ┌─ Persistent tree with COW ─────────────────────────────────────┐
  │                                                                 │
  │  [Leaf nodes ×~64] ← text stored in leaves (~512B each)        │
  │  [Internal nodes ×~63] ← metrics cache (~64B each)             │
  │  Shared leaves from original: ~80%                              │
  │  New leaves from edits: ~20%                                    │
  │                                                                 │
  │  Total allocated:  ~75 KB (includes all shared structure)       │
  │  Still live:       ~75 KB                                       │
  │  GC pressure:      ~4 KB (only replaced path nodes)            │
  └─────────────────────────────────────────────────────────────────┘
```

### 5.4 Mermaid: Allocation Scaling by String Size

```mermaid
graph TD
    subgraph "Allocation per substring+concat at various string sizes"
        subgraph "1 KB string"
            A1["Flat Array<br/>~4 KB alloc"]
            A2["Piece Table<br/>~0.1 KB alloc"]
            A3["Rope<br/>~0.3 KB alloc"]
        end
        subgraph "8 KB string"
            B1["Flat Array<br/>~32 KB alloc"]
            B2["Piece Table<br/>~0.1 KB alloc"]
            B3["Rope<br/>~0.5 KB alloc"]
        end
        subgraph "32 KB string"
            C1["Flat Array<br/>~137 KB alloc<br/>⚠️ DOMINANT COST"]
            C2["Piece Table<br/>~0.1 KB alloc<br/>✅ 1000× less"]
            C3["Rope<br/>~0.5 KB alloc<br/>✅ 250× less"]
        end
        subgraph "128 KB string"
            D1["Flat Array<br/>~520 KB alloc<br/>❌ GC STORM"]
            D2["Piece Table<br/>~0.1 KB alloc<br/>✅ 5000× less"]
            D3["Rope<br/>~1 KB alloc<br/>✅ 500× less"]
        end
    end

    style A1 fill:#e8f5e9
    style B1 fill:#fff3e0
    style C1 fill:#ffcdd2
    style D1 fill:#ef9a9a
    style A2 fill:#e8f5e9
    style B2 fill:#e8f5e9
    style C2 fill:#c8e6c9
    style D2 fill:#c8e6c9
    style A3 fill:#e8f5e9
    style B3 fill:#e8f5e9
    style C3 fill:#c8e6c9
    style D3 fill:#c8e6c9
```

### 5.5 The Crossover Point

The flat array is faster than piece table/rope below a certain string size due to:
- Lower constant factors (no tree traversal)
- Better cache locality (contiguous memory)
- Simpler code path (fewer allocations per operation)

But above the crossover, the O(n) copy cost dominates:

```
┌──────────────────────────────────────────────────────────────────────┐
│  CROSSOVER ANALYSIS                                                  │
│                                                                      │
│  Cost per substring+concat:                                          │
│                                                                      │
│  Flat array: ~4n bytes allocated (2× string copy + run copy)         │
│  Piece table: ~128 bytes allocated (fixed — 2 piece descriptors)     │
│  Rope: ~512 bytes allocated (fixed — path nodes + possible new leaf) │
│                                                                      │
│  Crossover (flat = piece table): 4n = 128 → n = 32 bytes            │
│  But flat has lower CONSTANT overhead until ~2-4 KB                  │
│                                                                      │
│  Estimated practical crossover:                                      │
│                                                                      │
│  String size    Flat faster?    Why                                   │
│  ──────────────────────────────────────────────────────────────────  │
│  < 256 B       ✅ Yes           memcpy faster than tree setup         │
│  256 B – 2 KB  ≈ Tie           copy cost ≈ tree overhead             │
│  2 KB – 8 KB   ❌ No            copy cost > tree overhead             │
│  8 KB – 32 KB  ❌❌ No           copy cost >> tree overhead            │
│  > 32 KB       ❌❌❌ No          copy cost >>> tree overhead           │
│                                                                      │
│  AT 32 KB AVERAGE:                                                   │
│  The flat array is WELL PAST the crossover point.                    │
│  Every operation wastes 130+ KB of allocation that the piece table   │
│  avoids entirely. With 50+ operations per evaluation, that's         │
│  6+ MB of needless GC pressure per string processed.                 │
└──────────────────────────────────────────────────────────────────────┘
```

### 5.6 Mermaid: Revised Decision Flowchart for 32KB Strings

```mermaid
flowchart TD
    A["Average string size?"] -->|"< 2 KB"| B["Keep flat array +<br/>apply optimizations §4.1-4.3"]
    A -->|"2-8 KB"| C["Flat array with optimizations<br/>OR simplified piece table<br/>(profile to decide)"]
    A -->|"8-32 KB"| D["Piece table recommended<br/>Clear win for edit-heavy paths"]
    A -->|"> 32 KB"| E["Piece table required<br/>Flat array is untenable"]

    D --> F{"Need structural<br/>sharing / undo?"}
    E --> F
    F -->|"No (SharpMUSH)"| G["VS Code-style Piece Table<br/>+ RB tree for O(log n) lookup<br/>+ per-piece markup runs"]
    F -->|"Yes"| H["Xi-style Persistent Rope<br/>+ COW for version history<br/>+ per-leaf markup runs"]

    G --> I["Implementation:<br/>§5.8 shows F# sketch"]
    H --> J["Implementation:<br/>More complex, only if<br/>undo/concurrent access needed"]

    style B fill:#c8e6c9
    style C fill:#fff3e0
    style D fill:#e3f2fd
    style E fill:#ffcdd2
    style G fill:#c8e6c9
    style H fill:#fce4ec
```

### 5.7 Piece Table vs Rope: Head-to-Head at 32KB

At 32KB, both piece table and rope dramatically outperform the flat array. But which is better **for SharpMUSH**?

```
┌──────────────────────────────────────────────────────────────────────┐
│  PIECE TABLE vs ROPE at 32KB (SharpMUSH workload)                   │
│                                                                      │
│  Criterion              Piece Table           Rope                   │
│  ─────────────────────────────────────────────────────────────────── │
│  Memory efficiency      ★★★★★                ★★★☆☆                 │
│  (buffers contiguous)   ~67 KB total          ~75 KB total           │
│                                                                      │
│  Allocation per edit    ★★★★★                ★★★★☆                 │
│  (piece descriptor)     ~64-128 bytes         ~256-512 bytes         │
│                                                                      │
│  Cache locality         ★★★★☆                ★★☆☆☆                 │
│  (during rendering)     pieces → contiguous   leaves → scattered     │
│                         buffer reads          pointer chasing         │
│                                                                      │
│  Concat                 ★★★★★                ★★★★★                 │
│                         O(log n) RB insert    O(log n) rebalance     │
│                                                                      │
│  Substring              ★★★★★                ★★★★★                 │
│                         O(log n) split piece  O(log n) split tree    │
│                                                                      │
│  Structural sharing     ★★★★☆                ★★★★★                 │
│  (across versions)      buffers shared,       full tree sharing,     │
│                         piece table rebuilt   COW path copying       │
│                                                                      │
│  Immutability           ★★★☆☆                ★★★★★                 │
│  (out of the box)       needs persistent      inherently persistent  │
│                         RB tree adaptation    (Xi-style COW)         │
│                                                                      │
│  Implementation effort  ★★★★☆                ★★☆☆☆                 │
│  (in F#)               RB tree available     custom tree + metrics   │
│                         in FSharpx.Collections balancing + leaf mgmt │
│                                                                      │
│  Undo / versioning      ★★★☆☆                ★★★★★                 │
│  (not needed for MUSH) add buffer has history full version history   │
│                                                                      │
│  VERDICT for SharpMUSH: PIECE TABLE WINS                             │
│                                                                      │
│  Rope's advantages (COW versioning, full structural sharing) are     │
│  most valuable for editors with undo and collaborative features.     │
│  SharpMUSH has a one-shot evaluation pipeline: parse → evaluate →   │
│  render. No undo, no concurrent editing, no version history.         │
│                                                                      │
│  Piece table wins on:                                                │
│  ✅ Memory efficiency (contiguous buffers, less overhead)            │
│  ✅ Cache locality during rendering (reads from 2 large buffers)    │
│  ✅ Implementation simplicity (RB tree is well-understood)          │
│  ✅ Lower per-operation allocation                                   │
│                                                                      │
│  Rope wins on:                                                       │
│  ✅ Persistent/immutable by default (F#-friendly)                   │
│  ✅ Better structural sharing across many versions                  │
│  ✅ Natural fit for functional programming                          │
│  ❌ But these advantages aren't needed for MUSH's one-shot pipeline │
└──────────────────────────────────────────────────────────────────────┘
```

### 5.8 F# Sketch: Piece Table AttributedMarkupString at 32KB

A concrete implementation sketch showing how the piece table would integrate with the existing `AttributedMarkupString` API:

```fsharp
/// Buffer identifier — either the original text or the append-only add buffer.
[<Struct>]
type BufferKind = Original | Add

/// A piece references a span within one of the two buffers,
/// along with the markup runs that apply to that span.
[<Struct>]
type Piece =
    {
        Buffer: BufferKind
        /// Offset within the buffer (byte position in the buffer string).
        Offset: int
        /// Length of this piece in characters.
        Length: int
        /// Markup runs local to this piece (offsets relative to piece start).
        Runs: ImmutableArray<AttributeRun>
    }

/// Piece table implementation using an immutable balanced tree.
/// At 32KB with ~200 runs and ~10-50 pieces, the RB tree provides
/// O(log n) lookup while the buffers provide structural sharing.
type PieceTableMarkupString =
    {
        /// The original text (immutable, never modified).
        OriginalBuffer: string
        /// Append-only buffer for all inserted/replaced text.
        AddBuffer: string
        /// Balanced tree of pieces (in-order traversal gives logical text).
        /// Each node caches left-subtree character count for O(log n) lookup.
        Pieces: ImmutableSortedPieceTree  // or ImmutableArray<Piece> for simpler impl
        /// Cached total length (sum of all piece lengths).
        TotalLength: int
    }

    // ── Operations ──────────────────────────────────────────────────

    /// Substring: find the pieces spanning [start, start+length),
    /// split boundary pieces, return new PieceTableMarkupString
    /// sharing the same buffers.
    ///
    /// Cost: O(log n) to find start + O(k) for k overlapping pieces
    /// No text is copied — only piece descriptors are created.
    member this.Substring(start: int, length: int) : PieceTableMarkupString =
        // 1. Binary search the piece tree for the piece containing 'start'
        // 2. Split that piece at the local offset
        // 3. Binary search for the piece containing 'start + length'
        // 4. Split that piece at the local offset
        // 5. Return pieces between the two split points
        // Buffers are SHARED — zero text copy
        ...

    /// Concat: create new piece table with pieces from both operands.
    /// If both share the same original buffer, even the buffers are shared.
    ///
    /// Cost: O(log n) for RB tree merge. No text copy.
    member this.Concat(other: PieceTableMarkupString) : PieceTableMarkupString =
        // 1. Merge piece trees (RB tree join)
        // 2. If buffers differ, the new table references both original buffers
        //    (or materializes — design choice)
        ...

    /// Materialize to plain text (for rendering or API compatibility).
    /// Cost: O(n) — must concatenate all piece texts.
    /// Cached lazily (same as current AttributedMarkupString).
    member this.ToPlainText() : string =
        let sb = StringBuilder(this.TotalLength)
        for piece in this.Pieces.InOrder() do
            let buf = match piece.Buffer with
                      | Original -> this.OriginalBuffer
                      | Add -> this.AddBuffer
            sb.Append(buf, piece.Offset, piece.Length) |> ignore
        sb.ToString()

    /// Render with strategy — iterates pieces in order,
    /// rendering each piece's runs against its buffer slice.
    /// Same IRenderStrategy interface as current implementation.
    member this.RenderWith(strategy: IRenderStrategy) : string =
        let sb = StringBuilder(this.TotalLength * 2)
        for piece in this.Pieces.InOrder() do
            let buf = match piece.Buffer with
                      | Original -> this.OriginalBuffer
                      | Add -> this.AddBuffer
            for run in piece.Runs do
                let segment = strategy.EncodeText(
                    buf.Substring(piece.Offset + run.Start, run.Length))
                let mutable result = segment
                for markup in run.Markups do
                    result <- strategy.ApplyMarkup markup result
                sb.Append(result) |> ignore
        strategy.Optimize(sb.ToString())
```

### 5.9 Migration Path: Flat Array → Piece Table at 32KB

```
┌──────────────────────────────────────────────────────────────────────┐
│  MIGRATION STRATEGY                                                  │
│                                                                      │
│  Phase 1: Internal optimization (no API change)                      │
│  ─────────────────────────────────────────────────────────────────── │
│  Keep AttributedMarkupString as the public API type.                 │
│  Internally, operations like substring+concat can use a piece table  │
│  representation and only materialize to flat array when:             │
│  • .Text is accessed (lazy materialization)                          │
│  • .Runs is accessed (lazy materialization)                          │
│  • RenderWith is called (iterate pieces directly — no materialization│
│    needed)                                                           │
│                                                                      │
│  This means: zero API changes, zero consumer changes.                │
│                                                                      │
│  Phase 2: New type with adapter                                      │
│  ─────────────────────────────────────────────────────────────────── │
│  Introduce PieceTableMarkupString as a separate type.                │
│  Provide conversion functions:                                       │
│  • fromAttributedMarkupString : AMS → PTMS                           │
│  • toAttributedMarkupString : PTMS → AMS (materializes)              │
│  Hot paths (regex replace, split+rejoin) use PTMS internally.        │
│                                                                      │
│  Phase 3: Replace (optional, if warranted)                           │
│  ─────────────────────────────────────────────────────────────────── │
│  Replace AttributedMarkupString with PieceTableMarkupString          │
│  throughout. The IRenderStrategy interface remains unchanged —       │
│  rendering iterates pieces instead of a single run array.            │
│                                                                      │
│  ESTIMATED EFFORT:                                                   │
│  Phase 1: ~2 days — modify substring/concat/replace internals        │
│  Phase 2: ~1 day — new type + conversion functions                   │
│  Phase 3: ~3 days — update all 938 MModule call sites                │
└──────────────────────────────────────────────────────────────────────┘
```

### 5.10 Mermaid: Architecture with Piece Table

```mermaid
graph TD
    subgraph "Current Architecture"
        A1["MModule.substring(...)"] --> A2["Scan all runs O(n)<br/>Copy string O(n)<br/>Build new runs O(k)"]
        A2 --> A3["New AttributedMarkupString<br/>(full copy)"]
    end

    subgraph "Piece Table Architecture (32KB optimized)"
        B1["MModule.substring(...)"] --> B2["Find piece O(log n)<br/>Split piece O(1)<br/>Share buffers O(1)"]
        B2 --> B3["New PieceTableMarkupString<br/>(shared buffers, new piece descriptors only)"]
        B3 --> B4{"Needs materialization?"}
        B4 -->|"RenderWith()"| B5["Iterate pieces in-order<br/>Render directly from buffers<br/>No materialization needed"]
        B4 -->|".Text / .Runs"| B6["Materialize once<br/>Cache result<br/>O(n) one-time cost"]
    end

    style A2 fill:#ffcdd2
    style A3 fill:#ffcdd2
    style B2 fill:#c8e6c9
    style B3 fill:#c8e6c9
    style B5 fill:#c8e6c9
    style B6 fill:#fff3e0
```

### 5.11 Recommendation Summary for 32KB Strings

```
┌──────────────────────────────────────────────────────────────────────┐
│                                                                      │
│  AT 32KB AVERAGE STRING SIZE:                                        │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  ★ RECOMMENDED: VS Code-style Piece Table                     │  │
│  │                                                                │  │
│  │  • O(log n) substring/concat vs O(n) flat array               │  │
│  │  • ~1000× less allocation per editing operation                │  │
│  │  • Buffers provide excellent cache locality for rendering      │  │
│  │  • Implementation complexity is moderate (RB tree is standard) │  │
│  │  • Immutable variant possible via persistent RB tree in F#    │  │
│  │  • No undo/CRDT overhead (unlike rope)                        │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  ACCEPTABLE: Xi-style Rope                                    │  │
│  │                                                                │  │
│  │  • Also O(log n) for all operations                           │  │
│  │  • Natural immutability / persistent data structure in F#     │  │
│  │  • Higher memory overhead (~10-15% more than piece table)     │  │
│  │  • Worse cache locality during rendering (pointer chasing)    │  │
│  │  • More complex to implement correctly                        │  │
│  │  • Overkill: COW/undo/CRDT not needed for MUSH pipeline      │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  ✗ NOT RECOMMENDED: Flat Array (current)                      │  │
│  │                                                                │  │
│  │  • O(n) copy on every editing operation                       │  │
│  │  • 6+ MB GC pressure per 50-match regex replace on 32KB       │  │
│  │  • O(n²) multipleWithDelimiter on large lists                 │  │
│  │  • The optimizations from §4 of the efficiency analysis help  │  │
│  │    but cannot overcome the fundamental O(n) copy cost          │  │
│  └────────────────────────────────────────────────────────────────┘  │
│                                                                      │
└──────────────────────────────────────────────────────────────────────┘
```

---

## References

1. **Xi Editor Rope Science**: https://xi-editor.io/docs/rope_science_00.html — Raph Levien's detailed series on rope data structures, metrics caching, and CRDT extensions.

2. **VS Code Text Buffer Reimplementation**: https://code.visualstudio.com/blogs/2018/03/23/text-buffer-reimplementation — The VS Code team's blog post on replacing their line array with a piece table + red-black tree.

3. **Crowley, "Data Structures for Text Sequences"** (1998): The original piece table paper. Available at https://www.cs.unm.edu/~crowley/papers/sds.pdf

4. **Raph Levien, "Rope Science" talk** (2018): https://www.youtube.com/watch?v=jTBHSMhSl-I — Conference talk covering the design decisions behind Xi's rope.

5. **VS Code Source: pieceTreeTextBuffer**: https://github.com/microsoft/vscode/tree/main/src/vs/editor/common/model/pieceTreeTextBuffer — The actual VS Code piece table implementation in TypeScript.
