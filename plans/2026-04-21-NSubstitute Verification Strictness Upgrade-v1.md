# NSubstitute Verification Strictness Upgrade

## Objective

Audit and upgrade all NSubstitute `.Received()` / `.DidNotReceive()` call-verification assertions across `SharpMUSH.Tests` to satisfy three strict requirements per verified call:

1. **Receiver** — the object the mocked method was called on (already asserted by `TestHelpers.MatchingObject(…)` in most cases, but must be explicit and equality-based)
2. **Sender** — the sender argument (`AnySharpObject? sender` — third parameter of `INotifyService.Notify`) must be explicitly matched with a DBRef equality check, never left as `null` unless `null` is the correct expected value and is asserted as such
3. **Unique string** — the message / argument that uniquely identifies the specific call must be verified with a full equality assertion (using `TestHelpers.MessagePlainTextEquals`) rather than substring containment (`MessageContains`, `MessagePlainTextContains`) or a prefix (`MessagePlainTextStartsWith`) wherever the full expected text is deterministic

Additionally, all call counts must be explicit: `Received(1)` for exactly one call, never the vague `Received()` (≥ 1 implicit) or `Received(Quantity.AtLeastOne())`.

---

## Implementation Plan

### Phase 1 — Count Strictness (All Files)

- [ ] Task 1.1. Replace every bare `Received()` (no argument) in all command-test files with `Received(1)`. This affects every file in `SharpMUSH.Tests/Commands/` that currently imports `NSubstitute.ReceivedExtensions`.
- [ ] Task 1.2. Replace every `Received(Quantity.AtLeastOne())` with `Received(1)` in `ZoneCommandTests.cs`, `WarningCommandTests.cs`, `SystemCommandTests.cs`, `AtListCommandTests.cs`, `UserDefinedCommandsTests.cs`, `DebugVerboseTests.cs`, and any other file using the `Quantity` helper.
- [ ] Task 1.3. Remove the `using NSubstitute.ReceivedExtensions;` import where it only existed to provide `Quantity.AtLeastOne()`; after the above replacements it will be unused.

---

### Phase 2 — Message-String Equality (INotifyService.Notify Calls)

The central pattern to enforce everywhere is:

```csharp
await NotifyService
    .Received(1)
    .Notify(
        TestHelpers.MatchingObject(receiver),                                            // receiver
        Arg.Is<OneOf<MString, string>>(msg => TestHelpers.MessagePlainTextEquals(msg, "exact text here")), // unique-string (equals)
        TestHelpers.MatchingObject(sender),                                              // sender
        INotifyService.NotificationType.Announce);
```

#### 2a — Commands/ZoneCommandTests.cs

- [ ] Task 2a.1. `ChzoneClearZone` (line 104-107): Change `MessageContains(msg, "Zone cleared")` to `MessagePlainTextEquals(msg, "<exact localized zone-cleared text>")`. Look up the exact string from the `@chzone … =none` branch of `ChzoneCommand` in the implementation. Change count from `Quantity.AtLeastOne()` to `1`.
- [ ] Task 2a.2. `ChzoneInvalidObject` (line 154-157): The three OR'd Contains checks (`"don't see"` / `"can't see"` / `"NO SUCH OBJECT"`) must collapse to one deterministic equality check. Determine which exact error string the `LocateService` produces for a non-existent #99999, then replace the OR with `MessagePlainTextEquals(msg, "<that exact string>")`. Change count to `1`.
- [ ] Task 2a.3. `ChzoneInvalidZone` (line 175-178): Same fix — two OR'd Contains (`"don't see"` / `"can't see"`) to single `MessagePlainTextEquals`. Count to `1`.
- [ ] Task 2a.4. `ZMRUserDefinedCommandTest` (line 279-282): Change `MessageContains(msg, "ZMR command executed")` to `MessagePlainTextEquals(msg, "ZMR command executed")` — the exact string is embedded in the `@pemit` call at line 270, so equality is directly possible. Count to `1`.
- [ ] Task 2a.5. `PersonalZoneUserDefinedCommandTest` (line 341-344): Change `MessageContains(msg, "Personal zone command executed")` to `MessagePlainTextEquals(msg, "Personal zone command executed")` — again directly from the `@pemit` at line 322. Count to `1`.
- [ ] Task 2a.6. `ZMRDoesNotMatchCommandsOnZMRItself` (line 393-396, `DidNotReceive`): The negative assertion is already valid semantically, but the Contains predicate should still become `MessagePlainTextEquals(msg, "This should not execute")` to be exact — the text is set directly in the test at line 383.

#### 2b — Commands/FlagWildcardMatchingTests.cs

- [ ] Task 2b.1. `UnsetFlag_PartialMatch_NoCommand` (lines 58-64): Replace the nested `.Match(s => s.ToPlainText()!.Contains("Unset", …), …)` inline predicate with `TestHelpers.MessagePlainTextEquals(msg, "<exact unset notification text>")`. Determine the exact string from the `@set` command's unset branch. Count from `Received()` to `Received(1)`.
- [ ] Task 2b.2. `UnsetFlag_PartialMatch_Visual` (lines 98-104): Same fix as 2b.1. Count from `Received()` to `Received(1)`.

#### 2c — Commands/WarningCommandTests.cs

- [ ] Task 2c.1. `WarningsCommand_SetToNormal` (line 33-35): `MessageContains(s, "Warnings set to")` → `MessagePlainTextEquals(s, "<exact 'Warnings set to normal' text from `@warnings` implementation>")`. Count to `1`.
- [ ] Task 2c.2. `WarningsCommand_SetToAll` (line 47-49): Same fix, exact text for "all" variant. Count to `1`.
- [ ] Task 2c.3. `WarningsCommand_SetToNone` (line 61-63): Two OR'd Contains (`"cleared"` / `"none"`) → single equality. Determine which exact string the implementation emits. Count to `1`.
- [ ] Task 2c.4. `WarningsCommand_WithNegation` (line 75-77): `MessageContains(s, "Warnings set to")` → exact equality. Count to `1`.
- [ ] Task 2c.5. `WarningsCommand_WithUnknownWarning` (line 89-91): `MessageContains(s, "Unknown warning")` → exact equality for the unknown-warning error string. Count to `1`.
- [ ] Task 2c.6. `WarningsCommand_NoArguments_ShowsUsage` (line 103-105): `MessageContains(s, "Usage")` → exact equality for the usage string emitted by `@warnings`. Count to `1`.
- [ ] Task 2c.7. `WCheckCommand_SpecificObject` (line 117-119): Two OR'd Contains (`"@wcheck complete"` / `"Warning"`) → single `MessagePlainTextEquals`. Count to `1`.
- [ ] Task 2c.8. `WCheckCommand_NoArguments_ShowsUsage` (line 131-133): `MessageContains(s, "Usage")` → exact equality for `@wcheck` usage string. Count to `1`.

#### 2d — Commands/UserDefinedCommandsTests.cs

- [ ] Task 2d.1. Every `Received()` call that uses `MessageContains(s, "${token} …")`: since the token is generated at runtime via `GenerateUniqueName`, the full expected string IS fully deterministic within the test. Replace `MessageContains` with `MessagePlainTextEquals` for each of the 11 calls. The exact expected string for each call can be constructed by concatenating the token with the known literal suffix (e.g., `$"{token} Boo! a - b"`, `$"{token} Hello, World!"`, etc.). Count to `Received(1)`.

#### 2e — Commands/AtListCommandTests.cs

- [ ] Task 2e.1. `ListFlags` and related tests (lines 46, 60, 74, 88, 102, 116, 130, 144): These currently check for header strings like `"OBJECT FLAGS:"`, `"Object Flags:"`, `"LOCK TYPES:"`, `"COMMANDS:"`, etc. These headers are fixed strings, so equality is possible. Replace `MessageContains` with `MessagePlainTextEquals` targeting only the header line (which will be sent in its own `Notify` call). Count to `Received(1)`.
- [ ] Task 2e.2. Line 32 already uses `MessagePlainTextEquals` — confirm count is `Received(1)`.

#### 2f — Commands/SystemCommandTests.cs

- [ ] Task 2f.1. `MessagePlainTextContains(msg, "Object Flags:")` (line 36) and `MessagePlainTextContains(msg, "Object Powers:")` (line 51): Change to `MessagePlainTextEquals`. Count to `Received(1)`.

#### 2g — Commands/MiscCommandTests.cs

- [ ] Task 2g.1. `MessagePlainTextStartsWith(msg, "Room Zero")` and `MessagePlainTextStartsWith(msg, "Player")` (lines 125, 139, 153): Change `StartsWith` to `Equals` using the full exact first-line text of each command's output. Count to `Received(1)`.
- [ ] Task 2g.2. Line 167 already uses `MessagePlainTextEquals(msg, "GOODBYE.")` and should already be `Received(1)` — verify and confirm.

#### 2h — Commands/UtilityCommandTests.cs (28 Received calls)

- [ ] Task 2h.1. All `MessagePlainTextContains` and `MessagePlainTextStartsWith` calls in `@examine` sub-verifications (lines 72, 87, 102, 117, 132, 147, 160, 164, 177, 190, 203, 224, 237, 261, 267, 288, 294, 307): For each individual notification line sent by `@examine` (e.g., `"God(#1P…"`, `"Owner: God(#1)"`, `"Zone: *NOTHING*"`, `"Powers: "`, etc.), determine the complete exact string for that line and replace the partial check with `MessagePlainTextEquals`. Count to `Received(1)` per line.

#### 2i — Commands/DebugVerboseTests.cs (28 Received calls)

- [ ] Task 2i.1. All `msg.Match(mstr => mstr.ToString().Contains("…"), str => str.Contains("…"))` inline predicates: replace each `Contains` with `==` (exact equality), making them effectively `MessagePlainTextEquals`. The debug output format is fixed (e.g., `"! add(123,456) :"` and `"! add(123,456) => 579"`), so exact equality is achievable. Count each to `Received(1)`.
- [ ] Task 2i.2. Sender argument in `DebugVerboseTests`: debug notifications are currently sent with sender `null` (line 50). Determine from the implementation whether debug output has a sender. If yes, replace `null` with `TestHelpers.MatchingObject(expectedSender)`. If the implementation genuinely passes `null`, assert it explicitly with a comment clarifying the intention.

#### 2j — Commands/GeneralCommandTests.cs (54 Received calls)

- [ ] Task 2j.1. Apply the same pattern: change all `MessageContains` / `MessagePlainTextContains` / `MessagePlainTextStartsWith` to `MessagePlainTextEquals` with the full exact string. Change all counts to `Received(1)`.

#### 2k — All remaining command test files with Received calls

Apply identical fixes to each of the following files (change all Contains/StartsWith → Equals, all vague counts → `Received(1)`, verify sender is explicitly specified):

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
- [ ] Task 2k.23. `Commands/SystemCommandTests.cs` (2 calls)
- [ ] Task 2k.24. `Commands/VerbCommandTests.cs` — uses `ReceivedCalls()` manual inspection with `MessageContains`; migrate to proper `Received(1)` + `MessagePlainTextEquals`
- [ ] Task 2k.25. `Commands/GuestLoginTests.cs` (1 call): Multiple OR'd Contains for guest messages → single equality. Sender is `null` — assert it is genuinely null (the server sends no sender for guest messages) or provide the correct sender.
- [ ] Task 2k.26. `Commands/LogCommandTests.cs` (1 call)
- [ ] Task 2k.27. `Commands/QuotaCommandTests.cs` (1 call)
- [ ] Task 2k.28. `Commands/PlayerDestructionTests.cs` (1 call)

---

### Phase 3 — MessageCommandTests.cs (Manual ReceivedCalls API)

- [ ] Task 3.1. `MessageBasic`: Replace `ReceivedCalls()` manual inspection + `Contains("MessageBasic_UniqueValue_93751")` with a proper `Received(1).Notify(receiver, Arg.Is<…>(msg => TestHelpers.MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751")), sender, type)`. The receiver is the executor (#1); the sender should be determined from the `@message` implementation (likely the object `objDbRef`). Assert sender explicitly.
- [ ] Task 3.2. `MessageWithAttribute`: Replace manual inspection + `Contains("MessageWithAttribute_Result_84729:15")` with `Received(1).Notify(…, MessagePlainTextEquals(…, "MessageWithAttribute_Result_84729:15"), …)`.
- [ ] Task 3.3. `MessageUsesDefaultWhenAttributeMissing`: Replace manual inspection + `Contains("DefaultMessage_UniqueValue_72914")` with `Received(1)` + equality.
- [ ] Task 3.4. `MessageSilentSwitch`: Contains two manual inspection loops. The confirmation-count check (lines 96-128) asserts that a "Message sent to 1 recipient(s)." notification count is unchanged after `/silent` — this should become a `DidNotReceive()` check for that exact string after the silent command. The message-content check (line 114) should migrate to `Received(1)` + `MessagePlainTextEquals(msg, "MessageSilent_Value_61829")`.
- [ ] Task 3.5. `MessageNoisySwitch`: Replace both manual inspection loops with (a) `Received(1).Notify(…, MessagePlainTextEquals("MessageNoisy_Value_55193"), …)` for the content check and (b) `Received(1).Notify(…, MessagePlainTextEquals("Message sent to 1 recipient(s)."), …)` for the confirmation-line check.
- [ ] Task 3.6. `MessageNospoofSwitch`: Replace manual inspection + `Contains("MessageNospoof_Value_48203")` with `Received(1)` + equality.

---

### Phase 4 — WebSocketTestTests.cs

- [ ] Task 4.1. `WebSocketTest_ConnectButton_CallsConnectAsync` (line 233): Replace `Arg.Any<string>()` with the exact expected URI `"ws://localhost:4202/ws"`, which is the default value asserted on line 214. The full corrected call becomes `await mockWebSocketClient.Received(1).ConnectAsync("ws://localhost:4202/ws")`. There is no "sender" concept for `IWebSocketClientService.ConnectAsync`, so only the receiver (`mockWebSocketClient`) and the unique string (the URI) apply.

---

### Phase 5 — WebSocketConnectionServiceTests.cs

- [ ] Task 5.1. `WebSocketConnectionUsesCorrectConnectionType` (lines 154-159): The current `Arg.Is<ConnectionEstablishedMessage>(m => m.Handle == handle && m.ConnectionType == "websocket" && m.IpAddress == "192.168.1.100")` already applies full equality on all three identifying fields. Change `Arg.Any<CancellationToken>()` to `Arg.Is<CancellationToken>(ct => ct == CancellationToken.None)` if the service passes `CancellationToken.None`, or leave as-is with an explanatory comment if CancellationToken equality is not meaningful. No sender concept applies to `IMessageBus.Publish`.

---

### Phase 6 — ScheduledTaskManagementServiceTests.cs

The `IExpandedObjectDataService.SetExpandedServerDataAsync` call takes a single `UptimeData` record. There is no sender concept. "As close to equality as possible" means capturing the expected state precisely before the job runs.

- [ ] Task 6.1. `UpdateWarningTimeJob_UpdatesTimeWhenExpired`: Capture `var expectedNextWarning = now.AddSeconds(3600);` before invoking `job.Execute`. Then replace the range predicate `d.NextWarningTime > now && d.NextWarningTime <= now.AddHours(1.1)` with a tight tolerance check: `Math.Abs((d.NextWarningTime - expectedNextWarning).TotalSeconds) < 1.0`. This is as close to equality as a time-generated field allows without injecting a clock mock. For all other fields of `UptimeData` that are known exactly (`StartTime`, `LastRebootTime`, `Reboots`, `NextPurgeTime`), assert them with equality inside the predicate as well, so the full record is verified structurally.
- [ ] Task 6.2. `UpdatePurgeTimeJob_UpdatesTimeWhenExpired`: Apply the same approach for `NextPurgeTime`, capturing `var expectedNextPurge = now.AddSeconds(3600)` and using tight-tolerance equality. Assert all known-exact fields of `UptimeData` with equality.
- [ ] Task 6.3. For the two `DidNotReceive().SetExpandedServerDataAsync(Arg.Any<UptimeData>())` calls: change `Arg.Any<UptimeData>()` to `Arg.Is<UptimeData>(_ => true)` with an inline comment noting that this is the negative assertion; the goal is "never called regardless of argument". This is already the correct strictness for a `DidNotReceive` case.

---

### Phase 7 — TestHelpers.cs Cross-Cutting Improvements

- [ ] Task 7.1. Add a new `ReceivedNotify` helper method to `TestHelpers.cs` (or `SharpMUSH.Tests.Infrastructure/TestHelpers.cs`) that encapsulates the standard three-argument equality check in one call, reducing per-test boilerplate and enforcing the pattern:
  ```csharp
  public static async ValueTask AssertReceivedNotify(
      INotifyService notifyService,
      DBRef receiver,
      string exactMessage,
      DBRef sender,
      INotifyService.NotificationType type = INotifyService.NotificationType.Announce,
      int count = 1)
  ```
  This internally calls `notifyService.Received(count).Notify(MatchingObject(receiver), Arg.Is<OneOf<MString,string>>(m => MessagePlainTextEquals(m, exactMessage)), MatchingObject(sender), type)`.
- [ ] Task 7.2. Migrate the existing `ReceivedNotifyLocalizedWithKey` helper to also accept an explicit `count` parameter (currently it's a boolean existence check — add an overload that asserts exact count via `.ReceivedCalls().Count(…)`).

---

## Verification Criteria

- No `.Received()` call without an explicit count argument remains in any test file.
- No `Received(Quantity.AtLeastOne())` call remains.
- No `MessageContains`, `MessagePlainTextContains`, or `MessagePlainTextStartsWith` appears inside a `Received().Notify(…)` argument matcher (they are only permitted outside of Received checks, e.g., in `WaitForNotification` polling).
- Every `Received(1).Notify(…)` call explicitly specifies all four parameters: receiver (via `MatchingObject`), message (via `MessagePlainTextEquals`), sender (via `MatchingObject` or `null` with comment), and `NotificationType`.
- `WebSocketTestTests.WebSocketTest_ConnectButton_CallsConnectAsync` asserts `ConnectAsync("ws://localhost:4202/ws")` exactly.
- `ScheduledTaskManagementServiceTests` assertions verify all fields of `UptimeData` with field-level equality; time fields use a ±1 second tolerance expressed as a numeric comparison rather than a range spanning minutes.
- All tests pass without modification to production code.

---

## Potential Risks and Mitigations

1. **Unknown exact server message strings**
   Several commands produce messages that are assembled from resources, localized format strings, or attribute data not visible from the test. These exact strings must be determined by reading the implementation or running the tests and inspecting output.
   Mitigation: For each file in Phase 2, read the corresponding implementation command handler to extract the verbatim message string before writing the equality assertion. If the string is localized (from `ErrorMessages`), use `nameof` or the resource string literal directly.

2. **Multi-line command output requires multiple Received(1) calls**
   Commands like `@examine` (UtilityCommandTests) and `@list` (AtListCommandTests) send many individual `Notify` calls. Changing every Contains to Equals means each line of output needs its own `Received(1)` assertion with the full exact text of that line.
   Mitigation: Determine each individual message line by running the test in debug mode or inspecting the implementation, then write one `Received(1)` assertion per distinct notification. Do not collapse multiple lines into a single assertion.

3. **Dynamic content makes equality impossible for some messages**
   Some messages embed runtime-variable data (object counts, timestamps, dynamic names). These can never be exact-equality checks.
   Mitigation: For purely dynamic segments (e.g., a count that varies with database state), isolate the test setup to produce a deterministic count, then assert the full expected string including that count. For timestamps in displayed messages, redirect to the `ReceivedNotifyLocalizedWithKey` pattern using the localization key rather than the rendered string.

4. **Shared test session causes Received() call accumulation**
   Because many tests use `Shared = SharedType.PerTestSession`, a mock accumulates calls from all tests in that session. `Received(1)` will fail if a prior test in the session already triggered the same notification.
   Mitigation: For tests sharing the session factory, use `NSubstitute.ClearSubstitute()` or call `notifyService.ClearReceivedCalls()` at the start of each test that uses `Received(1)`. Alternatively, migrate sensitive tests to `Shared = SharedType.None` (per-test instance) where the mock is fresh each time.

5. **Sender argument value unknown for some commands**
   Some `Notify` calls pass a sender that is determined inside the command implementation; the test currently passes `null` or skips the sender check.
   Mitigation: Trace the code path for each affected test to determine the exact sender. If the sender is the executor, use `TestHelpers.MatchingObject(executor)`. If it is the target object, use `TestHelpers.MatchingObject(targetDbRef)`. Only use `null` when the implementation is verified to pass `null`.

---

## Alternative Approaches

1. **Introduce an `ITimeClock` abstraction for ScheduledTaskManagementServiceTests**: Instead of ±1 second tolerance, inject a deterministic `ITimeProvider` mock into `UpdateWarningTimeJob` and `UpdatePurgeTimeJob`, capture the exact `DateTimeOffset` returned by the mock, then assert full structural equality on `UptimeData`. This would make the time assertions pure equality at the cost of refactoring production code.

2. **Switch the message verification layer from NSubstitute arg-matchers to a separate assertion step**: After calling the command under test, use `notifyService.ReceivedCalls()` to retrieve all calls, then assert on the collected arguments using TUnit's `Assert.That(…).IsEqualTo(…)` directly — the same approach `ReceivedNotifyLocalizedWithKey` uses. This avoids NSubstitute's arg-matching overhead and produces clearer failure messages (TUnit diff output) over NSubstitute's "expected to receive… but received…" messages. The risk is more boilerplate per test.

3. **Consolidate all Notify verification through `ReceivedNotifyLocalizedWithKey`**: Migrate all non-localized `Notify` calls to use `NotifyLocalized` in the implementation (passing a resource key rather than a raw string) and then verify uniformly with the existing helper that already does equality on the key. This removes the need to assert raw message text entirely at the cost of a production-code change.
