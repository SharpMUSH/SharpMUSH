# NSubstitute Verification Strictness Upgrade

## Objective

Upgrade every NSubstitute `.Received()` / `.DidNotReceive()` call-verification assertion across `SharpMUSH.Tests` to satisfy three strict requirements per verified call:

1. **Receiver** — verified via `TestHelpers.MatchingObject(dbRef)` (DBRef equality). Already correct in most files; confirm it is present everywhere.
2. **Sender** — the `AnySharpObject? sender` third parameter of `INotifyService.Notify` must be explicitly matched via `TestHelpers.MatchingObject(senderDbRef)`. Never omitted or left as un-asserted `null` unless the sender is genuinely `null` for that code path.
3. **Unique string** — the message argument must be checked with `TestHelpers.MessagePlainTextEquals` using a **globally unique exact string** so that `Received(1)` is sound across a shared parallel test session without any call-clearing.

All call counts must use `Received(1)` (exact).

The mechanism that makes this safe without clearing calls is **uniqueness** — applied through one of three patterns in priority order.

---

## Uniqueness Pattern Decision Tree

Apply the first pattern that is achievable for each assertion:

### Pattern A — Unique String via Token (preferred)
Use when the test controls the notification text (e.g., `@pemit`, `@emit`, `@pemit/silent`, user-defined `$`-commands).

Embed `TestIsolationHelpers.GenerateUniqueName("…")` in the command body **at setup time** (not only in the assertion), so the exact emitted string is unique per test run:

```csharp
var token = TestIsolationHelpers.GenerateUniqueName("MyCmd");
// At setup: @pemit me={token}: result text
await NotifyService
    .Received(1)
    .Notify(
        TestHelpers.MatchingObject(executor),
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"{token}: result text")),
        TestHelpers.MatchingObject(sender),
        INotifyService.NotificationType.Announce);
```

### Pattern B — Unique String via Server-Generated Full Text (second choice)
Use when the server emits a fixed, complete string that no other test in the session can plausibly trigger for the same receiver. Use the full verbatim string from the implementation — not a substring. If the message embeds an object name or DBRef, create the test object with `GenerateUniqueName` so the full expected string is unique:

```csharp
var objName = TestIsolationHelpers.GenerateUniqueName("ZonedObj");
// After @chzone {objDbRef}=none, server emits: "Zone of {objName} cleared."
await NotifyService
    .Received(1)
    .Notify(
        TestHelpers.MatchingObject(executor),
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"Zone of {objName} cleared.")),
        TestHelpers.MatchingObject(executor),
        INotifyService.NotificationType.Announce);
```

### Pattern C — Unique Receiver (fallback)
Use **only** when the message string is fully fixed by the server (no dynamic field, no embedded object name) **and** cannot be modified by the test. Create a fresh isolated player via `TestIsolationHelpers.CreateTestPlayerWithHandleAsync`, execute the command as that player, and assert on the unique player's DBRef as the receiver. The combination of unique receiver + full exact message string guarantees the assertion matches exactly one call in the entire session:

```csharp
var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
    WebAppFactoryArg.Services, Mediator, ConnectionService, "WarnTest");

await Parser.CommandParse(testPlayer.Handle, ConnectionService,
    MModule.single("@warnings me=normal"));

await NotifyService
    .Received(1)
    .Notify(
        TestHelpers.MatchingObject(testPlayer.DbRef),          // unique receiver
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "Warnings set to normal.")),
        TestHelpers.MatchingObject(testPlayer.DbRef),          // sender (same player for self-directed commands)
        INotifyService.NotificationType.Announce);
```

---

## Implementation Plan

### Phase 1 — Count Strictness (Global)

- [ ] Task 1.1. Replace every bare `Received()` (no argument — means ≥ 1) with `Received(1)` in all files under `SharpMUSH.Tests/Commands/`. Files affected include `FlagWildcardMatchingTests.cs`, `UserDefinedCommandsTests.cs`, `DebugVerboseTests.cs`, and all files listed in Phase 2k.
- [ ] Task 1.2. Replace every `Received(Quantity.AtLeastOne())` with `Received(1)` in `ZoneCommandTests.cs`, `WarningCommandTests.cs`, `SystemCommandTests.cs`, `AtListCommandTests.cs`, and any other file importing `NSubstitute.ReceivedExtensions`.
- [ ] Task 1.3. Remove `using NSubstitute.ReceivedExtensions;` from any file where it was only needed for `Quantity.AtLeastOne()` after those references are replaced.

---

### Phase 2 — Unique String + Equality, by File

#### 2a — Commands/ZoneCommandTests.cs

- [ ] Task 2a.1. `ChzoneClearZone` (line 104-107): Determine the exact verbatim string `@chzone …=none` emits (look up in `ChzoneCommand`). If the message includes the object name, the object already has a `GenerateUniqueName`-prefixed name → Pattern B. If the message is fixed, apply Pattern C (fresh player). Replace `MessageContains` → `MessagePlainTextEquals`. `Received(1)`.
- [ ] Task 2a.2. `ChzoneInvalidObject` (line 154-157): Three OR'd `Contains` (`"don't see"` / `"can't see"` / `"NO SUCH OBJECT"`) → determine the single exact error string `LocateService` emits for `#99999` and use Pattern B (message is fixed but receiver is already `executor`; if another test may trigger the same error to `#1`, switch to Pattern C). `Received(1)`.
- [ ] Task 2a.3. `ChzoneInvalidZone` (line 175-178): Same fix as 2a.2 for the zone-not-found error. `Received(1)`.
- [ ] Task 2a.4. `ZMRUserDefinedCommandTest` (line 279-282): Pattern A. Change `@pemit` body at line 270 to embed the unique token (`@pemit #{testPlayer.Number}={token}: ZMR command executed`), then assert `MessagePlainTextEquals(msg, $"{token}: ZMR command executed")`. `Received(1)`.
- [ ] Task 2a.5. `PersonalZoneUserDefinedCommandTest` (line 341-344): Pattern A. Embed token in `@pemit` at line 322. Assert exact equality. `Received(1)`.
- [ ] Task 2a.6. `ZMRDoesNotMatchCommandsOnZMRItself` (line 383, `DidNotReceive`): Pattern A. Embed token in `@pemit` at line 383. Assert `DidNotReceive().Notify(…, MessagePlainTextEquals(msg, $"{token}: This should not execute"), …)`.

#### 2b — Commands/FlagWildcardMatchingTests.cs

- [ ] Task 2b.1. `UnsetFlag_PartialMatch_NoCommand` (lines 58-64): Determine the full exact unset notification string from `@set` implementation. If fully fixed → Pattern C (create unique player, run `@set` as that player). Replace complex inline `.Match(…Contains("Unset")…)` → `MessagePlainTextEquals`. `Received(1)`.
- [ ] Task 2b.2. `UnsetFlag_PartialMatch_Visual` (lines 98-104): Same fix as 2b.1. `Received(1)`.

#### 2c — Commands/WarningCommandTests.cs

All `@warnings` messages are server-generated fixed strings with no embedded dynamic field. The shared `#1` executor means the receiver is not unique. **Apply Pattern C to all** — create a fresh player per test, run `@warnings me=…` as that player, assert on the unique player DBRef.

- [ ] Task 2c.1. `WarningsCommand_SetToNormal`: Pattern C. Fresh player. `MessagePlainTextEquals(s, "<full exact text>")`. `Received(1)`.
- [ ] Task 2c.2. `WarningsCommand_SetToAll`: Pattern C. Fresh player. Full exact text. `Received(1)`.
- [ ] Task 2c.3. `WarningsCommand_SetToNone`: Pattern C. Fresh player. Replace OR'd Contains with single `MessagePlainTextEquals`. `Received(1)`.
- [ ] Task 2c.4. `WarningsCommand_WithNegation`: Pattern C. Fresh player. Full exact text. `Received(1)`.
- [ ] Task 2c.5. `WarningsCommand_WithUnknownWarning`: Pattern C. Fresh player. Full exact unknown-warning error text. `Received(1)`.
- [ ] Task 2c.6. `WarningsCommand_NoArguments_ShowsUsage`: Pattern C. Fresh player. Full exact usage string. `Received(1)`.
- [ ] Task 2c.7. `WCheckCommand_SpecificObject`: Pattern C. Fresh player. Replace OR'd Contains → single exact completion string. `Received(1)`.
- [ ] Task 2c.8. `WCheckCommand_NoArguments_ShowsUsage`: Pattern C. Fresh player. Full exact usage string. `Received(1)`.

#### 2d — Commands/UserDefinedCommandsTests.cs

All 11 calls already embed the unique `token` in the expected substring. The token makes the string globally unique — use Pattern A for all of them.

- [ ] Task 2d.1. `WildcardEqSplitCommandPassesArgsToEmit`: `MessagePlainTextEquals(s, $"{token} Boo! a - b")`. `Received(1)`.
- [ ] Task 2d.2. `Wildcard_Single_SubstitutesArg`: `MessagePlainTextEquals(s, $"{token} Hello, World!")`. `Received(1)`.
- [ ] Task 2d.3. `Wildcard_TwoCaptures_WithLiteralBetween_SubstitutesBothArgs`: `MessagePlainTextEquals(s, $"{token}: Message from Alice to Bob")`. `Received(1)`.
- [ ] Task 2d.4. `Wildcard_ExactMatch_NoWildcards_FiresCommand`: `MessagePlainTextEquals(s, $"{token} Pong!")`. `Received(1)`.
- [ ] Task 2d.5–2d.11. Apply the same Pattern A fix to the remaining 7 calls. `Received(1)` for each.

#### 2e — Commands/AtListCommandTests.cs

- [ ] Task 2e.1. Lines 46, 60, 74, 88, 102, 116, 130, 144: Listing header strings (`"OBJECT FLAGS:"`, `"Object Flags:"`, `"LOCK TYPES:"`, etc.) are fixed but are likely unique per test because each `@list` switch produces a different header. Confirm no two tests send the same header to `#1` in the same session. If unique → Pattern B (`MessagePlainTextEquals` with full header string). If collision possible → Pattern C. `Received(1)` for each.
- [ ] Task 2e.2. Line 32: Already uses `MessagePlainTextEquals`. Confirm count is `Received(1)`.

#### 2f — Commands/SystemCommandTests.cs

- [ ] Task 2f.1. Lines 36 and 51 (`MessagePlainTextContains`): If header strings (`"Object Flags:"`, `"Object Powers:"`) are unique per test → Pattern B. Otherwise → Pattern C. `Received(1)`.

#### 2g — Commands/MiscCommandTests.cs

- [ ] Task 2g.1. Lines 125, 139, 153 (`MessagePlainTextStartsWith`): Determine the full first output line of each command. If the line embeds the unique object name → Pattern B. If fully fixed → Pattern C. Replace `StartsWith` → `MessagePlainTextEquals`. `Received(1)`.
- [ ] Task 2g.2. Line 167 (`MessagePlainTextEquals(msg, "GOODBYE.")`): Fixed string, but `@disconnect` is unlikely to be triggered by any other test. Pattern B. Confirm count is `Received(1)`.

#### 2h — Commands/UtilityCommandTests.cs (28 calls)

- [ ] Task 2h.1. `@examine` sub-verifications (all 28 calls): For each individual output line, determine the full exact string. Lines that embed the object's name (e.g., `"God(#1P…"`) or DBRef are naturally unique → Pattern B. Lines that are generic labels (e.g., `"Owner: "` prefix only) require the object to have a `GenerateUniqueName` name embedded → Pattern B with unique name. Only fall to Pattern C if no dynamic field exists in any line. `Received(1)` for each.

#### 2i — Commands/DebugVerboseTests.cs (28 calls)

- [ ] Task 2i.1. All 28 inline `Contains` predicates: The debug format strings (e.g., `"! add(123,456) :"`, `"! add(123,456) => 579"`) already embed the literal expression and result — these are unique by construction for each distinct expression. Replace `Contains` → `==` (full equality). `Received(1)` for each.
- [ ] Task 2i.2. Sender argument: Currently `null` on line 50. Trace the debug-output code path to confirm whether the sender is genuinely `null` or is the enactor. If non-null, replace with `TestHelpers.MatchingObject(expectedSender)`.

#### 2j — Commands/GeneralCommandTests.cs (54 calls)

- [ ] Task 2j.1. Apply the decision tree to all 54 calls: Pattern A where the test controls the message, Pattern B where the server message embeds a unique object name, Pattern C where the message is fully fixed and the receiver is shared. `Received(1)` for each.

#### 2k — All remaining command test files

Apply the decision tree (Pattern A → B → C) and `Received(1)` to every remaining file:

- [ ] Task 2k.1. `Commands/DatabaseCommandTests.cs` (26 calls)
- [ ] Task 2k.2. `Commands/ControlFlowCommandTests.cs` (22 calls)
- [ ] Task 2k.3. `Commands/CommunicationCommandTests.cs` (15 calls)
- [ ] Task 2k.4. `Commands/HttpCommandTests.cs` (14 calls)
- [ ] Task 2k.5. `Commands/WizardCommandTests.cs` (13 calls)
- [ ] Task 2k.6. `Commands/BuildingCommandTests.cs` (19 calls)
- [ ] Task 2k.7. `Commands/ConfigCommandTests.cs` (7 calls)
- [ ] Task 2k.8. `Commands/HelpCommandTests.cs` (6 calls)
- [ ] Task 2k.9. `Commands/NetworkCommandTests.cs` (6 calls)
- [ ] Task 2k.10. `Commands/SocialCommandTests.cs` (5 calls)
- [ ] Task 2k.11. `Commands/NewsCommandTests.cs` (5 calls)
- [ ] Task 2k.12. `Commands/NotificationCommandTests.cs` (6 calls)
- [ ] Task 2k.13. `Commands/AdminCommandTests.cs` (10 calls)
- [ ] Task 2k.14. `Commands/ChannelCommandTests.cs` (9 calls)
- [ ] Task 2k.15. `Commands/CommandUnitTests.cs` (3 calls)
- [ ] Task 2k.16. `Commands/CommandFlowUnitTests.cs` (3 calls)
- [ ] Task 2k.17. `Commands/SemaphoreCommandTests.cs` (3 calls)
- [ ] Task 2k.18. `Commands/ObjectManipulationCommandTests.cs` (4 calls)
- [ ] Task 2k.19. `Commands/FlagAndPowerCommandTests.cs` (6 calls)
- [ ] Task 2k.20. `Commands/AttributeCommandTests.cs` (3 calls)
- [ ] Task 2k.21. `Commands/MovementCommandTests.cs` (2 calls)
- [ ] Task 2k.22. `Commands/MailCommandTests.cs` (2 calls)
- [ ] Task 2k.23. `Commands/VerbCommandTests.cs` — uses `ReceivedCalls()` manual inspection + `Contains`; migrate to `Received(1)` + `MessagePlainTextEquals`. Pattern A: embed unique token in the `@verb` attribute body.
- [ ] Task 2k.24. `Commands/GuestLoginTests.cs` (1 call): Three OR'd Contains → single `MessagePlainTextEquals`. If the guest availability message is fixed → Pattern C with fresh guest session context.
- [ ] Task 2k.25. `Commands/LogCommandTests.cs` (1 call), `Commands/QuotaCommandTests.cs` (1 call), `Commands/PlayerDestructionTests.cs` (1 call). Apply decision tree. `Received(1)`.

---

### Phase 3 — Commands/MessageCommandTests.cs (Manual ReceivedCalls API)

- [ ] Task 3.1. `MessageBasic`: Replace `ReceivedCalls()` + `Contains("MessageBasic_UniqueValue_93751")` with `Received(1).Notify(receiver, Arg.Is<…>(msg => MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751")), sender, type)`. The hardcoded `_93751` suffix already makes this unique (Pattern B). Assert sender explicitly.
- [ ] Task 3.2. `MessageWithAttribute`: `Received(1)` + `MessagePlainTextEquals(msg, "MessageWithAttribute_Result_84729:15")`. Pattern B.
- [ ] Task 3.3. `MessageUsesDefaultWhenAttributeMissing`: `Received(1)` + `MessagePlainTextEquals(msg, "DefaultMessage_UniqueValue_72914")`. Pattern B.
- [ ] Task 3.4. `MessageSilentSwitch`: Before/after count comparison → `DidNotReceive()` for the exact confirmation string after the `/silent` command. Content assertion → `Received(1)` + `MessagePlainTextEquals(msg, "MessageSilent_Value_61829")`. Pattern B.
- [ ] Task 3.5. `MessageNoisySwitch`: Two manual inspections → two `Received(1)` calls with `MessagePlainTextEquals` for each. Pattern B.
- [ ] Task 3.6. `MessageNospoofSwitch`: `Received(1)` + `MessagePlainTextEquals(msg, "MessageNospoof_Value_48203")`. Pattern B.

---

### Phase 4 — Client/Components/WebSocketTestTests.cs

- [ ] Task 4.1. `WebSocketTest_ConnectButton_CallsConnectAsync` (line 233): Change `Arg.Any<string>()` → exact URI `"ws://localhost:4202/ws"` (same value asserted at line 214). Full call: `await mockWebSocketClient.Received(1).ConnectAsync("ws://localhost:4202/ws")`.

---

### Phase 5 — ConnectionServer/WebSocketConnectionServiceTests.cs

- [ ] Task 5.1. `WebSocketConnectionUsesCorrectConnectionType` (lines 154-159): The `Arg.Is<ConnectionEstablishedMessage>` lambda already asserts `Handle == handle`, `ConnectionType == "websocket"`, `IpAddress == "192.168.1.100"` — full equality on all identifying fields. Confirm count is `Received(1)`. `Arg.Any<CancellationToken>()` is acceptable.

---

### Phase 6 — Services/ScheduledTaskManagementServiceTests.cs

- [ ] Task 6.1. `UpdateWarningTimeJob_UpdatesTimeWhenExpired` (lines 110-112): Capture `var expectedNextWarning = now.AddSeconds(3600)` before `job.Execute`. Replace wide range with tight structural equality — assert all known-constant `UptimeData` fields exactly, and for the clock-generated field use `Math.Abs((d.NextWarningTime - expectedNextWarning).TotalSeconds) < 1.0`.
- [ ] Task 6.2. `UpdatePurgeTimeJob_UpdatesTimeWhenExpired` (lines 199-201): Same approach for `NextPurgeTime`.
- [ ] Task 6.3. `DidNotReceive` calls (lines 144, 167, 233): `Arg.Any<UptimeData>()` is correct for "never called" — leave as-is.

---

## Verification Criteria

- No `.Received()` without an explicit `(1)` count argument remains anywhere.
- No `Received(Quantity.AtLeastOne())` remains.
- No `MessageContains`, `MessagePlainTextContains`, `MessagePlainTextStartsWith`, or inline `.Contains(…)` appears inside any `Received(…).Notify(…)` argument matcher.
- Every `Received(1).Notify(…)` specifies all four arguments: receiver (`MatchingObject`), message (`MessagePlainTextEquals` with a globally unique exact string), sender (`MatchingObject` or documented `null`), `NotificationType`.
- Pattern C is only applied where both Pattern A and Pattern B are provably insufficient (documented with a comment in the test).
- `WebSocketTestTests.WebSocketTest_ConnectButton_CallsConnectAsync` asserts the exact URI `"ws://localhost:4202/ws"`.
- `ScheduledTaskManagementServiceTests` asserts all constant `UptimeData` fields with equality; the clock field uses sub-1-second tolerance.
- All existing tests continue to pass without modification to production code.
- No unused `using NSubstitute.ReceivedExtensions;` remains.

---

## Potential Risks and Mitigations

1. **Unknown exact server message strings**
   Many server-generated notification strings are not visible from the test layer.
   Mitigation: For each affected assertion, read the corresponding command handler to extract the verbatim emitted string before writing the equality check. If the string is from a resource key, use `ReceivedNotifyLocalizedWithKey` instead.

2. **Fixed-text message + shared receiver = Pattern C required**
   Some messages (e.g., `@warnings` output) are fully static with no dynamic field, directed to the shared `#1` executor. Parallel tests triggering the same notification to the same receiver would cause `Received(1)` to over-count.
   Mitigation: Pattern C exactly addresses this — a freshly created player's DBRef is unique across the entire session, anchoring the assertion safely.

3. **Pattern C overhead**
   `CreateTestPlayerWithHandleAsync` involves database writes and connection registration. Over-use inflates test runtime.
   Mitigation: Only apply Pattern C after confirming neither Pattern A nor B achieves uniqueness. Document the reason with a comment on each Pattern C usage.

4. **`@pemit`-sourced tests using a fixed string (not token)**
   Tests like `ZMRUserDefinedCommandTest` (currently `"ZMR command executed"`) would collide if another test ever uses that literal.
   Mitigation: Pattern A — the unique token must be embedded in the `@pemit` command body at setup time, not only in the assertion predicate.

5. **Multi-line command output and exact-count fragility**
   Commands like `@examine` emit many `Notify` calls. `Received(1)` on a specific line requires that line to appear exactly once, which depends on test object state.
   Mitigation: Control the examined object's attributes, contents, exits, and flags to produce a deterministic, minimal output. If a line genuinely appears more than once (e.g., repeated flag entries), use the exact count `Received(n)` rather than `Received(1)`.

---

## Alternative Approaches

1. **`ReceivedNotifyLocalizedWithKey` for all server messages**: Migrate all server-side notifications to `NotifyLocalized` and verify via the key-equality helper. Eliminates the need to know exact rendered strings but requires production-code changes.

2. **`ITimeProvider` injection for ScheduledTaskManagementServiceTests**: Supply a deterministic clock to the jobs, enabling full `UptimeData` structural equality with no tolerance window.
