# ANTLR 4 C# runtime (modified copy)

`src/` is the C# runtime from [ANTLR 4](https://github.com/antlr/antlr4) (`runtime/CSharp/src`),
copied from commit `e750ca2ae5e9341bbd0b7eaf96fc9107ce56f398` of the
`feature/csharp-runtime-perf-optimizations` branch at
https://github.com/HarryCordewener/antlr4, which is upstream pull request
[antlr/antlr4#4938](https://github.com/antlr/antlr4/pull/4938) on top of ANTLR's `dev` branch
at `7d577039`. It is modified from ANTLR's released runtime and is not an ANTLR release.

The files are unchanged from that commit. `Antlr4.csproj` and `Antlr4.snk` were left out;
`SharpMUSH.Antlr4.Runtime.csproj` builds the sources as the assembly `SharpMUSH.Antlr4.Runtime`
for SharpMUSH alone. It is never packed or published.

The runtime is licensed under the BSD 3-clause licence in `LICENSE.txt`, which every build that
references this project copies to `licenses/ANTLR-LICENSE.txt` in its output.

## Going back to the package

Once upstream releases these changes in `Antlr4.Runtime.Standard`, replace the
`ProjectReference` in `SharpMUSH.Parser.Generated.csproj` with a `PackageReference` to that
version, remove the project from `SharpMUSH.sln`, and delete this folder.

## Updating the copy

Replace `src/` with `runtime/CSharp/src` from the new commit (without `Antlr4.csproj` and
`Antlr4.snk`), and update the commit above.
