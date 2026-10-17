# Cache tag coherence acceptance — 2026-10-17

The engine's existing before/after key and tag invalidations do not by themselves prevent a late,
older FusionCache tag-marker commit from reviving an invalidated fill. This acceptance checks the
engine correction through real caching behaviors and through a real isolated world's parser and
provider. FusionCache remains pinned at 2.5.0; no private dependency state is changed.

## Deterministic real behavior regression

`CacheTagOrderingTests` schedules ordinary `IMemoryCache` entry commits through a forwarding
decorator. It holds an older command's **final** tag-marker commit while a stream captures 49 and a
newer command commits 50 with both normal invalidation passes intact. The older marker then commits,
followed by the captured 49 fill. Before the correction, the independent later reader receives 49
with source 50 and zero fresh factory calls. Sequential control passes. The original racing reader
is allowed its actual pre-write snapshot.

The correction's 12 focused cases cover scalar/stream races and joined consumers, dynamically
discovered object tags with object-key-only invalidation, all four stale cached-consumer shapes,
exactly one uncached fallback, no stale-reference resolution or recaching, unrelated-tag hit
preservation and concurrent invalidation counts/epochs. Existing `CacheCoherenceTests` also pass.

## Matched real parser/provider comparison

`ParserCacheFreshnessTests` uses `UsesOwnWorld`, the ordinary engine/parser, and a forwarding
`IAttributeStore` proxy. Every call delegates to the real provider. One selected inherited-attribute
stream is held **after** materializing its real 49 snapshot. A normal, unrelated typed attribute write
has its final `attribute-inheritance` tag-marker commit paused through public `IMemoryCache` entry
operations. A literal typed `&CNT ...=50` then completes through the normal engine; it avoids joining
the held `get()` as an `inc(get())` expression would.

After the older marker and held read finish, the test reads the real provider for diagnosis and
requires an independent parser `get()` to return 50. The diagnostic provider read is test-only;
production parser reads continue through the Mediator and the normal cache behaviors.

For the matched baseline, only `QueryCachingBehavior.cs` and `StreamQueryCachingBehavior.cs` were
restored from 2a76c2be595d6d589c318808c3f9837dfb2129aa. The stream constructor retained a no-op
`ObjectVersions` parameter solely for fixture compatibility. The same fixture, provider, scheduling,
and strict independent 50 assertion were used. A guarded runner restored the corrected files
byte-for-byte in `finally` before the final build.

| Run | Captured | Original reader | Provider | Independent parser reader | Result |
|---|---:|---:|---:|---:|---|
| Previous read behaviors |49|49|50|49|RED, expected50 |
| Corrected read behaviors |49|49|50|50|GREEN |

The baseline built with zero warnings/errors and failed its one strict parser assertion in 17.330s.
The corrected parser probe first passed alone and then passed in the concurrent acceptance below.

## Final verification

Final build: zero warnings/errors, 58.69s. The verified selection ran 54/54 tests with no failures or
skips in 24.148s, maximum parallelism 8, with a minimum expected count 54:

```sh
dotnet run --no-build --project SharpMUSH.Tests -- \
  --treenode-filter '/*/*/(ParserCacheFreshnessTests)|(CachingBehaviorTests)|(InplaceDepthLimitTests)|(CacheTagOrderingTests)|(CacheCoherenceTests)/*' \
  --maximum-parallel-tests 8 --minimum-expected-tests 54 --output detailed
```

This includes the full 20-case real-world `CachingBehaviorTests`, all four original
`InplaceDepthLimitTests`, 12 new ordering/certificate tests, 17 existing coherence tests and the
real-parser regression. The original depth-limit tests and their strict 50 assertions were unchanged.

This establishes an actual stale inherited-attribute/parser-read race that the correction prevents.
It does **not** establish that this exact interleaving caused the historical issue #2013 CI failure,
or reproduce a lost increment in storage. No production access or rollout was performed.
