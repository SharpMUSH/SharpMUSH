# Charset compatibility evidence and release boundary

**Draft dependency boundary:** the committed production pin remains TelnetNegotiationCore 4.5.0.
This branch's integration tests require the dependency fix below. It is not ready to merge or deploy
until a reviewed package containing that fix is released, pinned, restored normally, and verified.
No unpublished package version or local feed is committed as a production dependency.

Dependency source: [TelnetNegotiationCore 0bf24f58fc1c6d7ac48c34a65a9f03baec8f4537](https://github.com/HarryCordewener/TelnetNegotiationCore/commit/0bf24f58fc1c6d7ac48c34a65a9f03baec8f4537),
[dependency draft PR #159](https://github.com/HarryCordewener/TelnetNegotiationCore/pull/159),
branch `codex/charset-offer-validation`, based on main `84e6958834cc73c8d60e0259f28906429ecab76b`.
The released 4.5.0 package's nuspec pins `0ad3558f2b69d1455948ac0c1e8359b3c734c700`.

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

SharpMUSH built with SDK 11.0.100-rc.2.26504.105, zero warnings/errors, against local-only packages
`TelnetNegotiationCore` and `TelnetNegotiationCore.Gmcp` version `4.5.1-charset.2` from the dependency
commit above. The resulting test executable's deps.json identifies those exact package versions.

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

## Reproduce the local integration

Choose a short disk-backed scratch directory, set TMPDIR/TMP/TEMP to it, and use the repository SDK.
Check out the exact dependency commit above in a separate checkout. These packages stay local:

```bash
export TMPDIR="$CHARSET_SCRATCH" TMP="$CHARSET_SCRATCH" TEMP="$CHARSET_SCRATCH"
# In the dependency checkout:
dotnet build TelnetNegotiationCore/TelnetNegotiationCore.csproj -c Release \
  -p:Version=4.5.1-charset.2 -p:GeneratePackageOnBuild=false -m:2 -nr:false
dotnet pack TelnetNegotiationCore/TelnetNegotiationCore.csproj -c Release --no-build \
  -p:Version=4.5.1-charset.2 -o "$CHARSET_SCRATCH/feed" -m:2 -nr:false
dotnet pack TelnetNegotiationCore.Gmcp/TelnetNegotiationCore.Gmcp.csproj -c Release --no-build \
  -p:Version=4.5.1-charset.2 -o "$CHARSET_SCRATCH/feed" -m:2 -nr:false
# In SharpMUSH; command-line override only:
dotnet build SharpMUSH.Tests -m:2 -nr:false \
  -p:TelnetNegotiationCoreVersion=4.5.1-charset.2 \
  -p:RestoreAdditionalProjectSources="$CHARSET_SCRATCH/feed"
dotnet run --no-build --project SharpMUSH.Tests -- \
  --treenode-filter '/*/*/TelnetServerNegotiationTests/*'
```

Run each renderer class above separately with the same filter form. The local feed does not make
normal CI green: released 4.5.0 still fails the acceptance and incoming-GMCP regressions. TNC's PR
workflow publishes no preview packages; release requires an explicit workflow dispatch or tag.
After the reviewed release, change only TelnetNegotiationCoreVersion in Directory.Build.props to
the actual published version, restore/build without the local override, rerun these classes and CI,
and only then remove draft status. Publishing the dependency is a separate approval boundary.
