# ANTLR 4 C# runtime (modified copy)

`src/` is the C# runtime from [ANTLR 4](https://github.com/antlr/antlr4) (`runtime/CSharp/src`),
copied from commit `e750ca2ae5e9341bbd0b7eaf96fc9107ce56f398` of the
`feature/csharp-runtime-perf-optimizations` branch at
https://github.com/HarryCordewener/antlr4, which is upstream pull request
[antlr/antlr4#4938](https://github.com/antlr/antlr4/pull/4938) on top of ANTLR's `dev` branch
at `7d577039`. It is modified from ANTLR's released runtime and is not an ANTLR release.

`Antlr4.csproj` and `Antlr4.snk` were left out; `SharpMUSH.Antlr4.Runtime.csproj` builds the
sources as the assembly `SharpMUSH.Antlr4.Runtime` for SharpMUSH alone. It is never packed or
published.

## Changes from that commit

SharpMUSH parses many short inputs, each with a new token stream, so these changes cut the cost
that every parse pays rather than the cost of long ones. Parse trees are unchanged.

- `Atn/ParserATNSimulator.cs`: the object pools from #4938 are created on first use. A prediction
  the DFA already answers needs none of them.
- `ParserRuleContext.cs`: children are kept in the context itself (one field for a single child,
  an array from the second on) instead of a `List` per rule. `children` is now a property that
  returns a view over them, or `null` when there are none. `Parser.TrimParseTree` trims the array.
- `Atn/LexerATNSimulator.cs`, `Dfa/DFAState.cs`, `Dfa/LexerDFASerializer.cs`: the lexer DFA keeps
  an edge for EOF (shifted to index 0) and up to 256 edges per state for characters above 127,
  so the end of an input and non-ASCII text no longer rebuild config sets from the ATN.
- `CommonToken.cs`: text read from the input stream is kept after the first read.
- `DefaultErrorStrategy.cs`: a conjured token takes its input from the token it stands beside,
  not from the lexer, which may have moved on to another input.

The runtime is licensed under the BSD 3-clause licence in `LICENSE.txt`, which every build that
references this project copies to `licenses/ANTLR-LICENSE.txt` in its output.

## Going back to the package

Once upstream releases these changes in `Antlr4.Runtime.Standard`, replace the
`ProjectReference` in `SharpMUSH.Parser.Generated.csproj` with a `PackageReference` to that
version, remove the project from `SharpMUSH.sln`, and delete this folder.

## Updating the copy

Replace `src/` with `runtime/CSharp/src` from the new commit (without `Antlr4.csproj` and
`Antlr4.snk`), apply the changes listed above again, and update the commit above.
