# Native cursor error after a copied row: qualified runtime evidence

Date: 2026-10-17. The isolated [B1 native foundation](../plans/2026-10-17-semantic-wiki-phase-b1.md) now has an actual mid-iteration native-error witness through the compiled bounded reader. On the pinned Linux-x64 encrypted fixture, a supported page-decryption callback failure produced native `CryptoFail` after the reader copied its first row. `ReadRange` threw, returned no batch and released its working reservation; a fresh healthy environment read both complete original records.

**Final acceptance remains OPEN.** The implementation received independent pre-execution safety/spec review and scoped review of its scratch-layout correction, and the focused runtime test passed. Affected regressions passed 105 tests with zero failures/skips; final native PR/whole-branch review and CI remain pending. This is candidate evidence for the missing native-error gate, not completion of all B1 or Phase B work.

## Actual observed sequence

The focused parent `NativeSemanticCursorErrorTests.SupportedCipherFailureAfterFirstRowThrowsWithoutPartialBatchOrLeakedReservation` passed: **1 passed, 0 failed, 0 skipped**, total test run **443 ms**, individual test **184 ms**. The owned child **1470291 exited 0**, with empty stderr and all six PID/world/scenario-validated phase markers in order:

1. `cipher-seeded`: ordinary encrypted writes committed a one-byte key/value first row and a second one-byte key with an 8,192-byte value; the environment closed.
2. `cipher-control-closed`: a new healthy environment verified both full key/value pairs and terminal `NotFound`. Public statistics reported **3 overflow pages** on **4,096-byte pages**. Advancing to the second row invoked **1 later decryption callback**.
3. `cipher-raw-failure-closed`: another fresh environment verified the first row with `First` and `GetCurrent`, then armed the supported callback failure. Actual native `Next` returned **CryptoFail (-30777)**, with **1 selected failure**. The cursor, transaction, wrapper and environment closed without another read or retry.
4. `cipher-bounded-failure-closed`: another fresh environment invoked the real private `LightningStore.Tx` through the existing `IBoundedReadTx.ReadRange`, using a fresh zero budget and actual captured table flags. The callback and unwind observations were:

| Observation | Actual result |
| --- | ---: |
| Callback native probes | 3 |
| Callback inspected bytes | 2 |
| Initial range working reservation | 1,312 bytes |
| Callback working reservation | 1,456 bytes |
| Selected bounded callback failures | 1 |
| Actual thrown exception | `LightningStoreException` |
| Retained native error | `CryptoFail (-30777)` |
| Returned batch | false |
| Working bytes before / after call | 0 / 0 |
| Cumulative probes / bytes after failure | 3 / 2 |

5. `cipher-healthy-reopen-closed`: only after failed owners closed, a new healthy environment verified **both complete original key/value pairs**, overflow statistics and terminal `NotFound`.
6. `cipher-cursor-error-complete`: compact evidence was written after all native scopes closed. The parent validated it, printed it to its detailed log, joined the child and both readers, then removed its owned scratch.

The trace is tied to the reviewed reader order: probe 1 precedes `First`, probe 2 precedes `GetCurrent`, and probe 3 follows copying and storing the first entry and precedes `Next`. The initial reservation is `64 + 3*64 + (32 + 8*128) = 1312`; the first copied entry adds `64 + 40 + 40 = 144`. The actual callback saw 1,456 working bytes and exactly 3 probes/2 inspected bytes. These observations establish a populated first entry before native failure, rather than only an opening/first-row failure or return-code classifier test.

The full-value comparisons were performed before publishing evidence. The recorded value SHA-256 digests were `4a64a107f0cb32536e5bce6c98c393db21cca7f4ea187ba8c4dca8b51d4ea80a` for the first value and `25df2449b2e5a35fea14e02a7158e283801a1069c9f84631b9a9dacb2f809a7f` for the second. No encryption key or full payload is logged.

## Mechanism and ownership

The test-only `tools/WikiSemantics.NativeProbe/CipherCursorErrorScenario.cs` delegates ordinary encryption/decryption to the package's real `AesGcmCipher`. Its selected `Decrypt` call records bounded in-memory counters and returns the documented `false` result, without throwing, I/O, buffer changes, budget mutation or native reentry. The pinned callback adapter converts false to a callback failure; LMDB itself returns `MDB_CRYPTO_FAIL` through page fetch and cursor advancement. No native file corruption, invalid handle, pointer manipulation, shim library or production fault hook is involved.

`ActualBoundedReader.cs` reflects only the actual private managed `TableSnapshot.Capture` and `Tx` constructor, using the same live read transaction and its environment's DBI. It creates no second store/environment and accesses no native handle. A healthy read-only setup transaction commits DBI publication; wrappers retain the package default `closeOnDispose=false`, so the environment closes its DBI once. This follows the pinned native contract that abort automatically closes a newly opened uncommitted DBI. Cipher/key ownership survives native environment closure. Neither failed environment is read or retried; each later pass opens a wholly new environment.

Admission still requires a new GUID-owned empty fixture directory and nonexistent sibling world/evidence paths. A separate owned sibling directory receives child TMPDIR/TMP/TEMP, so runtime temporary files cannot occupy the fixture directory. The parent has a 40-second test deadline; the child has its existing 90-second watchdog. Cleanup joins actual child exit and observes both reader tasks before filesystem deletion, retaining primary and secondary failures. Cleanup does not promise a hard timeout that abandons a live native owner.

## Pinned provenance

Runtime verified the actual loaded managed assembly and mapped native library, rather than a package label alone:

| Asset | Identity |
| --- | --- |
| Package / assembly | LightningDB 0.23.1 / assembly version 0.23.1.0 |
| Managed informational version | `0.23.1+46f8ca97ef9eb992db93e493b29493179d2e5343` |
| net10.0 managed DLL SHA-256 | `79f70654594367b7388a23a49923856ee8b8a88f8f70e03f53ca7f1039e81854` |
| Runtime native version | LMDB 1.0.1 |
| Mapped Linux-x64 liblmdb.so SHA-256 | `e518ad9bec8fdbc3e856f993edc17e6d476e04a44797d75d87280a400346fcb4` |

The managed source review used commit `46f8ca97ef9eb992db93e493b29493179d2e5343`. These asset identities and runtime checks do not establish reproducible compilation of that native binary. The parent explicitly skips other OS/process architectures; no cross-platform proof is claimed.

## RED, failed setup and corrected run

| Stage | Observed result |
| --- | --- |
| Test-only normal build | Passed, 0 warnings/errors, 48.69 s |
| Intentional runtime RED | 1 failed / 0 skipped, 305 ms; child 1440252 exit 134, `Unsupported scenario`, before cipher implementation |
| First implemented normal build | Passed, 0 warnings/errors, 43.77 s |
| First implemented runtime attempt | 1 failed / 0 skipped, 352 ms; child 1452850 exit 134, `Fixture parent must be empty`, no phase markers and no opened fixture environment |
| Corrected normal build | Passed, 0 warnings/errors, 34.70 s |
| Corrected focused runtime | 1 passed / 0 skipped, 443 ms; actual qualified proof above |

The failed setup assigned child temporary-directory variables to the same directory required to remain empty until admission. Its exact unexpected entries were not retained. Separate read-only runtime inventories showed normal diagnostic pipes/sockets under TMPDIR, consistent with this startup collision. The correction separated runtime scratch and fixture ownership, moved directory creation inside cleanup scope, and retained the strict empty-directory guard. Independent scoped review confirmed that the cipher scenario, managed reader bridge and dispatch were unchanged by this correction. The setup failure supplied no native-error evidence.

## Reproduction and remaining checks

Use the SDK required by `global.json`, restored dependencies and Linux-x64. Set `DOTNET_ROOT`/PATH to that SDK, choose disk-backed scratch such as `/temp/codex/semantic-native-cipher-root`, set TMPDIR/TMP/TEMP to it, and disable core dumps with `ulimit -c 0`. Run from the isolated checkout; serialize all commands through the existing `/temp/codex/sharpmush-build.lock`. Root executed these workload commands:

```bash
for pass in 1 2; do dotnet format whitespace --folder SharpMUSH.Tests --exclude "**/bin/**" "**/obj/**"; done
for pass in 1 2; do dotnet format whitespace --folder tools/WikiSemantics.NativeProbe --exclude "**/bin/**" "**/obj/**"; done
dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj --no-restore
dotnet run --project SharpMUSH.Tests --no-build --no-restore -- --treenode-filter "/*/*/NativeSemanticCursorErrorTests/*" --output detailed
```

Root then ran the affected regression classes against the same built source, using `dotnet run --project SharpMUSH.Tests --no-build --no-restore -- --treenode-filter "/*/*/<Class>/*"` for each class below. All runs completed successfully, with zero failures and zero skips:

| Class | Passed | Run duration |
| --- | ---: | ---: |
| `CursorFailureTests` | 17 | 363 ms |
| `BoundedReadTests` | 21 | 600 ms |
| `LightningStoreTests` | 13 | 577 ms |
| `NativeSemanticKeyTests` | 3 | 220 ms |
| `NativeSemanticPreparationTests` | 18 | 245 ms |
| `NativeSemanticCommitTests` | 23 | 19.244 s |
| `NativeSemanticRecoveryTests` | 10 | 3.562 s |

That is **105 affected regression tests**, plus the new focused proof, **106 passed in total**. This is not a whole-suite or new full-corpus run. Final native PR/whole-branch review and CI remain pending; prior corpus measurements are unchanged.

This witness covers the selected supported encrypted-page callback failure through the actual bounded forward range reader. It does not demonstrate spontaneous/default unencrypted I/O failures, store scheduler or admission-gate concurrency, every cursor direction, prior-result reservation isolation, full B1/Phase B acceptance, or production readiness. Existing B2/C/D integration, authorization, query, interference and rebuild obligations remain open. No production activation or deployment follows from this result.
