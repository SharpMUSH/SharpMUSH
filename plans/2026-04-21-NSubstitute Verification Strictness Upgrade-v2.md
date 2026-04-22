# NSubstitute Verification Strictness Upgrade

## Objective

Upgrade every NSubstitute `.Received()` / `.DidNotReceive()` call-verification assertion across `SharpMUSH.Tests` to satisfy three strict requirements per verified call:

1. **Receiver** — verified via `TestHelpers.MatchingObject(dbRef)`, which performs DBRef equality. Already correct in most files; confirm it is present everywhere.
2. **Sender** — the `AnySharpObject? sender` third parameter of `INotifyService.Notify` must be explicitly matched via `TestHelpers.MatchingObject(senderDbRef)`. Never omitted or left as un-asserted `null` unless the sender is genuinely `null` for that code path.
3. **Unique string** — the message argument must be checked with `TestHelpers.MessagePlainTextEquals` using a **globally unique exact string** so that `Received(1)` is sound across a shared parallel test session without any call-clearing. No `Contains`, `StartsWith`, or OR-conditions.

All call counts must use `Received(1)` (exact). The mechanism that makes this safe in a shared session is the uniqueness of the asserted string itself — as demonstrated by the already-passing `Received(1)` calls in the codebase (e.g., `AtListCommandTests`, `MiscCommandTests`, `UserDefinedCommandsTests`).

---

## Unique String Strategy

Two patterns are used depending on the source of the message:

**Pattern A — Test-Controlled (`@pemit` / `@emit` output)**
The test sets the notification text directly. Embed `TestIsolationHelpers.GenerateUniqueName("…")` as part of the message body so the exact string is unique per test run:
```csharp
var token = TestIsolationHelpers.GenerateUniqueName("MyCmd");
// @pemit me={token}: result here
await NotifyService
    .Received(1)
    .Notify(TestHelpers.MatchingObject(executor),
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"{token}: result here")),
        TestHelpers.MatchingObject(sender),
        INotifyService.NotificationType.Announce);
```

**Pattern B — Server-Generated messages**
The message is emitted by server code. Use the full, verbatim string from the implementation (not a substring). If the message includes a dynamic field such as an object name, create the test object with a `GenerateUniqueName`-prefixed name so the full expected string is unique:
```csharp
var zoneName = TestIsolationHelpers.GenerateUniqueName("ZoneMaster");
// After @chzone …=none the server emits exactly: "Zone of <objectName> cleared."
await NotifyService
    .Received(1)
    .Notify(TestHelpers.MatchingObject(executor),
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, $"Zone of {objName} cleared.")),
        TestHelpers.MatchingObject(executor),
        INotifyService.NotificationType.Announce);
```
If the server message is entirely fixed and contains no dynamic field (e.g., `"GOODBYE."`), the fixed string itself is unique because no other test scenario triggers that exact notification.

---

## Implementation Plan

### Phase 1 — Count Strictness (Global)

- [ ] Task 1.1. Replace every bare `Received()` (no argument — means ≥ 1) with `Received(1)` in all files under `SharpMUSH.Tests/Commands/`. Files affected include `FlagWildcardMatchingTests.cs`, `UserDefinedCommandsTests.cs`, `DebugVerboseTests.cs`, and the 26 files listed in Phase 2k of the previous plan.
- [ ] Task 1.2. Replace every `Received(Quantity.AtLeastOne())` with `Received(1)` in `ZoneCommandTests.cs`, `WarningCommandTests.cs`, `SystemCommandTests.cs`, `AtListCommandTests.cs`, and any other file importing `NSubstitute.ReceivedExtensions`.
- [ ] Task 1.3. Remove `using NSubstitute.ReceivedExtensions;` from any file where it was only needed for `Quantity.AtLeastOne()` after those references are replaced.

---

### Phase 2 — Unique String + Equality (INotifyService.Notify Calls, by File)

#### 2a — Commands/ZoneCommandTests.cs

- [ ] Task 2a.1. `ChzoneClearZone` (line 104-107, server-generated "Zone cleared"): Determine the exact verbatim string the `@chzone …=none` path emits (look up in `ChzoneCommand` implementation). Create the test object with `GenerateUniqueName` if the message includes the object name; otherwise use the fixed full string. Replace `MessageContains(msg, "Zone cleared")` with `MessagePlainTextEquals(msg, "<full exact text>")`. Change count to `Received(1)`.
- [ ] Task 2a.2. `ChzoneInvalidObject` (line 154-157): The three OR'd `Contains` checks (`"don't see"` / `"can't see"` / `"NO SUCH OBJECT"`) must collapse to the single exact error string that `LocateService` emits for a non-existent `#99999`. Use `MessagePlainTextEquals` with that string. Change count to `Received(1)`.
- [ ] Task 2a.3. `ChzoneInvalidZone` (line 175-178): Same fix — two OR'd Contains to single `MessagePlainTextEquals`. Change count to `Received(1)`.
- [ ] Task 2a.4. `ZMRUserDefinedCommandTest` (line 279-282): Apply Pattern A. The `@pemit` body is currently the fixed string `"ZMR command executed"`. Change the attribute setup at line 270 to embed the unique token: `@pemit #{testPlayer.Number}={token}: ZMR command executed`, then assert `MessagePlainTextEquals(msg, $"{token}: ZMR command executed")`. Change count to `Received(1)`.
- [ ] Task 2a.5. `PersonalZoneUserDefinedCommandTest` (line 341-344): Same Pattern A fix. Change the `@pemit` at line 322 to embed the token, assert exact equality. Change count to `Received(1)`.
- [ ] Task 2a.6. `ZMRDoesNotMatchCommandsOnZMRItself` (line 383, `DidNotReceive`): Change the `@pemit` body at line 383 to embed a unique token — `@pemit #{testPlayer.Number}={token}: This should not execute` — then assert `DidNotReceive().Notify(…, MessagePlainTextEquals(msg, $"{token}: This should not execute"), …)`. This way even a spurious match elsewhere in the session cannot produce a false pass.

#### 2b — Commands/FlagWildcardMatchingTests.cs

- [ ] Task 2b.1. `UnsetFlag_PartialMatch_NoCommand` (lines 58-64): Replace the nested inline `.Match(…Contains("Unset")…)` predicate with `TestHelpers.MessagePlainTextEquals(msg, "<full verbatim unset notification from @set implementation>")`. Change count to `Received(1)`.
- [ ] Task 2b.2. `UnsetFlag_PartialMatch_Visual` (lines 98-104): Same fix. Change count to `Received(1)`.

#### 2c — Commands/WarningCommandTests.cs

For all warning tests the receiver is the shared `#1` executor and the messages are server-generated fixed strings. The full exact string for each is the unique discriminator.

- [ ] Task 2c.1. `WarningsCommand_SetToNormal`: Replace `MessageContains(s, "Warnings set to")` with `MessagePlainTextEquals(s, "<full exact text for normal variant>")`. `Received(1)`.
- [ ] Task 2c.2. `WarningsCommand_SetToAll`: Same — full exact text for "all" variant. `Received(1)`.
- [ ] Task 2c.3. `WarningsCommand_SetToNone`: Replace OR'd Contains (`"cleared"` / `"none"`) with single `MessagePlainTextEquals` for the exact string emitted. `Received(1)`.
- [ ] Task 2c.4. `WarningsCommand_WithNegation`: Full exact text. `Received(1)`.
- [ ] Task 2c.5. `WarningsCommand_WithUnknownWarning`: Full exact text for the unknown-warning error. `Received(1)`.
- [ ] Task 2c.6. `WarningsCommand_NoArguments_ShowsUsage`: Full exact usage string. `Received(1)`.
- [ ] Task 2c.7. `WCheckCommand_SpecificObject`: Replace OR'd Contains (`"@wcheck complete"` / `"Warning"`) with the single exact completion string. `Received(1)`.
- [ ] Task 2c.8. `WCheckCommand_NoArguments_ShowsUsage`: Full exact usage string. `Received(1)`.

#### 2d — Commands/UserDefinedCommandsTests.cs

All 11 `Received()` calls already embed the unique `token` in the expected substring. Apply Pattern A: keep the token, change `MessageContains` → `MessagePlainTextEquals` with the full token-including string.

- [ ] Task 2d.1. `WildcardEqSplitCommandPassesArgsToEmit`: `MessagePlainTextEquals(s, $"{token} Boo! a - b")`. `Received(1)`.
- [ ] Task 2d.2. `Wildcard_Single_SubstitutesArg`: `MessagePlainTextEquals(s, $"{token} Hello, World!")`. `Received(1)`.
- [ ] Task 2d.3. `Wildcard_TwoCaptures_WithLiteralBetween_SubstitutesBothArgs`: `MessagePlainTextEquals(s, $"{token}: Message from Alice to Bob")`. `Received(1)`.
- [ ] Task 2d.4. `Wildcard_ExactMatch_NoWildcards_FiresCommand`: `MessagePlainTextEquals(s, $"{token} Pong!")`. `Received(1)`.
- [ ] Task 2d.5–2d.11. Apply the same exact-equality fix to the remaining 7 `Received()` calls in that file (each already has a deterministic token-prefixed expected string). `Received(1)` for each.

#### 2e — Commands/AtListCommandTests.cs

- [ ] Task 2e.1. Lines 46, 60, 74, 88, 102, 116, 130, 144 (`MessageContains` for listing headers): Replace each `MessageContains` with `MessagePlainTextEquals` using the full verbatim header line. These are fixed strings (e.g., `"OBJECT FLAGS:"`) — the full string IS the unique discriminator. `Received(1)` for each.
- [ ] Task 2e.2. Line 32: Already uses `MessagePlainTextEquals` — verify count is `Received(1)`.

#### 2f — Commands/SystemCommandTests.cs

- [ ] Task 2f.1. Lines 36 and 51: Replace `MessagePlainTextContains` with `MessagePlainTextEquals` using the full exact line. `Received(1)`.

#### 2g — Commands/MiscCommandTests.cs

- [ ] Task 2g.1. Lines 125, 139, 153 (`MessagePlainTextStartsWith`): Determine the full first line of each command's output and replace `StartsWith` with `MessagePlainTextEquals` of that full line. `Received(1)`.
- [ ] Task 2g.2. Line 167 (`MessagePlainTextEquals(msg, "GOODBYE.")`): Confirm count is already `Received(1)`.

#### 2h — Commands/UtilityCommandTests.cs

- [ ] Task 2h.1. All 28 `Received()` calls using `MessagePlainTextContains` / `MessagePlainTextStartsWith`: For each individual `@examine` output line, determine the full exact text of that notification and replace the partial check with `MessagePlainTextEquals`. Where output lines include dynamic values (e.g., object name, DBRef number), construct the test object with `GenerateUniqueName` and compose the full expected string including that name/number. `Received(1)` for each.

#### 2i — Commands/DebugVerboseTests.cs

- [ ] Task 2i.1. All 28 `Received()` calls using inline `msg.Match(…Contains("…")…)`: Replace each `Contains` with `==` (making each equivalent to `MessagePlainTextEquals`). The debug format strings (e.g., `"! add(123,456) :"`, `"! add(123,456) => 579"`) are fixed. `Received(1)` for each.
- [ ] Task 2i.2. Sender argument: Currently `null` on line 50. Trace the debug-output code path to confirm whether the sender is genuinely `null` or is the enactor object. If non-null, replace with `TestHelpers.MatchingObject(expectedSender)`.

#### 2j — Commands/GeneralCommandTests.cs (54 calls)

- [ ] Task 2j.1. Apply Pattern A or B to all 54 `Received()` calls: change all `MessageContains` / `MessagePlainTextContains` / `MessagePlainTextStartsWith` to `MessagePlainTextEquals` with the full exact string, using `GenerateUniqueName`-based object names where the message embeds the object name. `Received(1)` for each.

#### 2k — All remaining command test files

Apply the identical upgrade (full-exact-string equality, `Received(1)`) to every file:

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
- [ ] Task 2k.23. `Commands/VerbCommandTests.cs` — currently uses `ReceivedCalls()` manual inspection + `MessageContains`; migrate to `Received(1)` + `MessagePlainTextEquals`. Apply Pattern A: embed unique token in the `@verb` attribute body so exact equality is possible.
- [ ] Task 2k.24. `Commands/GuestLoginTests.cs` (1 call): Three OR'd Contains for guest availability messages → single `MessagePlainTextEquals` with the exact guest message. `Received(1)`.
- [ ] Task 2k.25. `Commands/LogCommandTests.cs` (1 call). `Commands/QuotaCommandTests.cs` (1 call). `Commands/PlayerDestructionTests.cs` (1 call). `Commands/MailCommandTests.cs` (2 calls). All three-field fix. `Received(1)`.

---

### Phase 3 — Commands/MessageCommandTests.cs (Manual ReceivedCalls API)

- [ ] Task 3.1. `MessageBasic`: Replace manual `ReceivedCalls()` inspection + `Contains("MessageBasic_UniqueValue_93751")` with `Received(1).Notify(receiver, Arg.Is<…>(msg => MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751")), sender, type)`. The hardcoded suffix `_93751` already makes the string globally unique. Determine and assert the sender explicitly.
- [ ] Task 3.2. `MessageWithAttribute`: Replace manual inspection + `Contains("MessageWithAttribute_Result_84729:15")` with `Received(1)` + `MessagePlainTextEquals(msg, "MessageWithAttribute_Result_84729:15")`.
- [ ] Task 3.3. `MessageUsesDefaultWhenAttributeMissing`: Replace manual inspection + `Contains("DefaultMessage_UniqueValue_72914")` with `Received(1)` + `MessagePlainTextEquals(msg, "DefaultMessage_UniqueValue_72914")`.
- [ ] Task 3.4. `MessageSilentSwitch`: The before/after count comparison on "Message sent to 1 recipient(s)." (lines 96-128) should become a `DidNotReceive()` assertion for that string after the `/silent` command executes. The message-content assertion (`Contains("MessageSilent_Value_61829")`) becomes `Received(1)` + `MessagePlainTextEquals(msg, "MessageSilent_Value_61829")`.
- [ ] Task 3.5. `MessageNoisySwitch`: Replace both manual inspections with `Received(1)` + `MessagePlainTextEquals` for each. The confirmation line check (`Contains("Message sent to")`) must use the full exact text.
- [ ] Task 3.6. `MessageNospoofSwitch`: Replace manual inspection + `Contains("MessageNospoof_Value_48203")` with `Received(1)` + `MessagePlainTextEquals`.

---

### Phase 4 — Client/Components/WebSocketTestTests.cs

- [ ] Task 4.1. `WebSocketTest_ConnectButton_CallsConnectAsync` (line 233): Change `Arg.Any<string>()` to the exact URI `"ws://localhost:4202/ws"` (the same value asserted for the default input at line 214). Full call: `await mockWebSocketClient.Received(1).ConnectAsync("ws://localhost:4202/ws")`.

---

### Phase 5 — ConnectionServer/WebSocketConnectionServiceTests.cs

- [ ] Task 5.1. `WebSocketConnectionUsesCorrectConnectionType` (lines 154-159): The three equality predicates inside the `Arg.Is<ConnectionEstablishedMessage>` lambda (`m.Handle == handle`, `m.ConnectionType == "websocket"`, `m.IpAddress == "192.168.1.100"`) already constitute a full unique-equality check. Verify count is `Received(1)`. Note: `Arg.Any<CancellationToken>()` is acceptable since `CancellationToken` cannot carry meaningful equality assertions.

---

### Phase 6 — Services/ScheduledTaskManagementServiceTests.cs

- [ ] Task 6.1. `UpdateWarningTimeJob_UpdatesTimeWhenExpired` (lines 110-112): Capture `var expectedNextWarning = now.AddSeconds(3600)` before `job.Execute`. Replace the wide range predicate with a tight-tolerance comparison that also asserts every other `UptimeData` field with exact equality:
  ```
  d.StartTime == oldData.StartTime &&
  d.LastRebootTime == oldData.LastRebootTime &&
  d.Reboots == 0 &&
  d.NextPurgeTime == oldData.NextPurgeTime &&
  Math.Abs((d.NextWarningTime - expectedNextWarning).TotalSeconds) < 1.0
  ```
  This is as close to full structural equality as a clock-dependent field allows without injecting a clock abstraction.
- [ ] Task 6.2. `UpdatePurgeTimeJob_UpdatesTimeWhenExpired` (lines 199-201): Apply the same approach for `NextPurgeTime`, asserting all other fields with exact equality.
- [ ] Task 6.3. `DidNotReceive` calls (lines 144, 167, 233): `Arg.Any<UptimeData>()` is correct for a "never called" check — leave as-is.

---

## Verification Criteria

- No `.Received()` call without an explicit numeric count argument (`1`) remains in any test file.
- No `Received(Quantity.AtLeastOne())` remains.
- No `MessageContains`, `MessagePlainTextContains`, `MessagePlainTextStartsWith`, or inline `.Contains(…)` appears inside any `Received(…).Notify(…)` argument matcher.
- Every `Received(1).Notify(…)` explicitly specifies: receiver (`MatchingObject`), message (`MessagePlainTextEquals` with a globally unique full string), sender (`MatchingObject` or documented `null`), and `NotificationType`.
- `WebSocketTestTests.WebSocketTest_ConnectButton_CallsConnectAsync` asserts the exact default URI `"ws://localhost:4202/ws"`.
- `ScheduledTaskManagementServiceTests` assertions verify every known-constant `UptimeData` field with equality; only the clock-generated field uses a sub-1-second tolerance.
- All existing tests continue to pass without any modification to production code.
- No `using NSubstitute.ReceivedExtensions;` import remains in any file where `Quantity` is no longer used.

---

## Potential Risks and Mitigations

1. **Unknown exact server message strings**
   The full verbatim text for many server-generated notifications (e.g., `@warnings`, `@chzone`, `@wcheck`) is not visible from the test layer.
   Mitigation: For each file in Phase 2, read the corresponding command handler to extract the exact emitted string before writing the assertion. If the string is assembled from a resource key, use that key with `ReceivedNotifyLocalizedWithKey` instead of raw text.

2. **Server messages contain no dynamic field — same string across tests**
   A few messages (e.g., "Warnings set to normal.") are entirely fixed and could theoretically be emitted by another test in the session. If so, `Received(1)` would over-count.
   Mitigation: For any fixed-text message where the receiver is also the shared `#1` player, audit whether any other test in the session triggers the same notification to `#1`. If a collision exists, change the test to use a freshly created player via `TestIsolationHelpers.CreateTestPlayerWithHandleAsync` so the receiver DBRef is unique to that test.

3. **@pemit-sourced messages must include token to be unique**
   Tests that construct their expected message from `@pemit` but use a fixed string (currently `"ZMR command executed"`, `"Personal zone command executed"`, `"This should not execute"`) will collide if any other test uses the same literal.
   Mitigation: Follow Pattern A for every such test — embed `GenerateUniqueName("…")` into the `@pemit` body before the command is set on the object (not just in the assertion), so the uniqueness is established at test setup time.

4. **Multi-line command output**
   Commands like `@examine` emit many sequential `Notify` calls. Changing every `Received()` to `Received(1)` requires knowing that each particular line appears exactly once — which depends on test setup (e.g., the object having no exits, no content, etc.).
   Mitigation: Tighten test object creation to control what the examined object contains, ensuring each expected output line appears exactly once. Where that is not feasible, use `Received().WithCount(n)` to assert the exact number of times that specific line appears.

---

## Alternative Approaches

1. **`ReceivedNotifyLocalizedWithKey` for all server messages**: If every server-side notification is migrated to `NotifyLocalized` (passing a resource key instead of a raw string), all assertions can use the existing key-equality helper regardless of the rendered text. This eliminates the need to know exact rendered strings but requires a production-code change to the notification layer.

2. **Structural record equality for UptimeData**: Inject an `ITimeProvider` / `TimeProvider` abstraction into the scheduled jobs so tests can supply a fixed `DateTimeOffset`. Then the full `UptimeData` record can be asserted with `Arg.Is<UptimeData>(d => d == expectedRecord)` (structural equality), making the assertion completely exact.
