# Charset compatibility evidence and release boundary

The production dependency pin is TelnetNegotiationCore 4.5.1. The dependency fix was reviewed in
[PR #159](https://github.com/HarryCordewener/TelnetNegotiationCore/pull/159) and merged at
[`43e5d98059022769caee45e9b26108122026e913`](https://github.com/HarryCordewener/TelnetNegotiationCore/commit/43e5d98059022769caee45e9b26108122026e913).
The implementation reviewed there is `0bf24f58fc1c6d7ac48c34a65a9f03baec8f4537`.
The prior released 4.5.0 package's nuspec pins `0ad3558f2b69d1455948ac0c1e8359b3c734c700`.

Both packages are publicly available from nuget.org. Their public nuspecs report version 4.5.1
and RepositoryCommit `43e5d98059022769caee45e9b26108122026e913`; restored cache metadata identifies
`https://api.nuget.org/v3/index.json` as the source. Normal solution restore resolves both 4.5.1
packages across all 23 dependent projects, including SharpMUSH.LanguageServer.

## Reproduction

Before the SharpMUSH fix, an actual duplex-pipe connection returned this CHARSET REQUEST payload:

```text
;utf-8;iso-8859-1;utf-16;utf-16BE;utf-32;utf-32BE;us-ascii
```

The exact supported-only wire assertion failed. Both non-ASCII GMCP receive tests failed after
Latin-1/ASCII selection. After narrowing the offer with public AllowedEncodings, all six
unsupported/unoffered acceptance cases still failed with released TNC 4.5.0: a smaller offer alone
did not constrain its acceptance handler. Source inspection confirmed that handler commits both
encodings before notification and catches callback exceptions. GMCP receive also used the text
encoding, so a post-change SharpMUSH callback could not repair the protocol contract.

The dependency now validates canonical code pages against a successfully sent, single-use request
snapshot before committing either encoding. Rejection/withdrawal/disposal/disable clear the offer;
replayed, empty and failed offers cannot authorize a selection. Requests use current configured
encodings rather than the first cached list. GMCP receive and string-send paths use UTF-8.
SharpMUSH narrows the public allowed set and explicitly uses the UTF-8 byte-array send overload.

## Acceptance matrix

| Contract | Evidence |
| --- | --- |
| Exact offer and order | `CharsetOffer_ContainsOnlyRendererEncodingsInPreferenceOrder`: UTF-8, ISO-8859-1, US-ASCII, with no extra names |
| UTF-8/Latin-1/ASCII input, metadata, output bytes | Five `CharsetAccepted_InputMetadataAndRenderedWireAgree` cases; captures the registered decoder, metadata updates and actual renderer/transmission bytes |
| Aliases | ISO-8859-1 case folding, iso-ir-100, ANSI_X3.4-1968; dependency compares canonical code pages |
| Unsupported ACCEPTED | UTF-16, unicode alias and UTF-32 return REJECTED; UTF-8 text coalesced behind the frame remains readable |
| No outstanding offer | Supported encoding before an offer, after REJECTED and after prior acceptance is rejected |
| Fragmented frames | REQUEST/ACCEPTED written in fragments through the pipe; dependency additionally feeds and drains every individual byte before the next |
| Peer REQUEST | Chooses supported Latin-1, prefers UTF-8 when offered, rejects UTF-16-only offer |
| GMCP independence | Raw non-ASCII JSON in both directions after Latin-1/ASCII selection |
| Existing behavior | Full `TelnetServerNegotiationTests` and renderer classes below |

## Validation

The initial implementation passed against local packages `4.5.1-charset.2`. Final production-pin
validation uses SDK 11.0.100-rc.2.26504.105 and the public 4.5.1 packages, with no version override
or local feed. Normal solution restore and the focused test build completed without errors; the
build reported zero warnings. The resulting runtime deps.json identifies both packages as 4.5.1.
The focused classes were rerun against those public packages:

- `TelnetServerNegotiationTests`: 47 passed.
- `OutputTransformServiceTests`: 6 passed.
- `TerminalFeatureRenderingTests`: 43 passed.
- `RenderingWorkerMarkupTests`: 5 passed.
- Modified SharpMUSH projects pass `dotnet format whitespace --folder ... --verify-no-changes` after two passes.
- Dependency: 1,384 tests passed on each of .NET 8, .NET 10 and .NET 11; all four library targets
  (including netstandard2.0) built without warnings/errors.

The full SharpMUSH engine suite and real-client/network deployment were not run for this isolated
protocol lane. Pipe tests exercise the real connection handler, interpreter and writes; they replace
only external connection registration and message publication with captured test boundaries.

## Reproduce public-package integration

Choose a short disk-backed scratch directory, set TMPDIR/TMP/TEMP to it, and use the repository SDK.
Normal restore uses the committed 4.5.1 pin; no package-version override or local feed is required.

```bash
export TMPDIR="$CHARSET_SCRATCH" TMP="$CHARSET_SCRATCH" TEMP="$CHARSET_SCRATCH"
dotnet restore SharpMUSH.sln -m:2 -nr:false
dotnet build SharpMUSH.Tests --no-restore -m:2 -nr:false
dotnet run --no-build --project SharpMUSH.Tests -- \
  --treenode-filter '/*/*/TelnetServerNegotiationTests/*'
```

Run each renderer class above separately with the same filter form. The repository does not track
NuGet packages.lock.json files; restored project.assets.json and runtime deps.json identify the
actual resolved versions, including the language server's transitive dependency.

The user separately approved the dependency merge and patch release. This SharpMUSH branch remains
unmerged; deploying or merging it is outside this integration step.
