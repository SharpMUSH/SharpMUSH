# Failing Tests — 2026-04-22

**Test run summary:** 3200 total | **73 failed** | 2939 passed | 188 skipped | duration ~1m 20s

Raw test output saved to: `/tmp/test-output.txt`
TRX report: `/tmp/test-results/_pop-os_2026-04-22_20_13_41.5486271.trx`

---

## Root Cause Pattern

The dominant failure pattern across nearly all tests is **NSubstitute mock pollution** — tests are sharing a single `INotifyService` mock across tests run in parallel, causing call-count assertions (`Received(1)`) to fail because prior test invocations have accumulated matching calls on the same mock instance. This surfaces as:

> `ReceivedCallsException: Expected to receive exactly 1 call matching … Actually received N matching calls`

A smaller group of tests fails because the expected call is **never received at all** (the feature under test is broken or the command routing misfires).

---

## Failing Tests

### Commands / FlagAndPowerCommandTests.cs

- [ ] **`Flag_List_DisplaysAllFlags`** — `SharpMUSH.Tests/Commands/FlagAndPowerCommandTests.cs:35`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., s => MessagePlainTextStartsWith(s, "Object Flags:"), ..., Announce)
  Actually received 2 matching calls.
  ```

---

### Commands / HelpCommandTests.cs

- [ ] **`HelpWithWildcardWorks`** — `SharpMUSH.Tests/Commands/HelpCommandTests.cs:59`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "help" or "helpfile", ..., Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`HelpWithPrefixMatchWorks`** — `SharpMUSH.Tests/Commands/HelpCommandTests.cs:104`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "MUSH", ..., Announce)
  Actually received 6 matching calls.
  ```

- [ ] **`HelpWithTopicWorks`** — `SharpMUSH.Tests/Commands/HelpCommandTests.cs:44`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "MUSH", ..., Announce)
  Actually received 6 matching calls.
  ```

---

### Commands / HttpCommandTests.cs

- [ ] **`Test_Respond_InvalidStatusCode`** — `SharpMUSH.Tests/Commands/HttpCommandTests.cs:57`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., "Status code must be a 3-digit number.", ..., Announce)
  Actually received 2 matching calls.
  ```

---

### Commands / CommandUnitTests.cs

- [ ] **`TestSingle(think Command1 Arg;think Command2 Arg, Command1 Arg, Command2 Arg)`** — `SharpMUSH.Tests/Commands/CommandUnitTests.cs:64`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., x => x.Value.ToString().Contains(expected1), ..., Announce)
  Actually received 2 matching calls.
  First call received: "Command1 Arg;think Command2 Arg"
  Second call received: "Command1 Arg"
  ```

- [ ] **`TestSingle(think [add(1,2)]6;think add(3,2)7, 36, 57)`** — `SharpMUSH.Tests/Commands/CommandUnitTests.cs:64`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., x => x.Value.ToString().Contains(expected1), ..., Announce)
  Actually received 2 matching calls.
  Received: "(HTTP): Header Set-Cookie: name=Bob; Max-Age=3600; Version=1" and "36"
  ```

- [ ] **`TestSingle(think add(1,2)4;think add(2,3)5, 34, 55)`** — `SharpMUSH.Tests/Commands/CommandUnitTests.cs:64`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., x => x.Value.ToString().Contains(expected1), ..., Announce)
  Actually received 2 matching calls.
  Received: "Power 'TEST_POWER_DISABLE_334F54DB' disabled." and "34"
  ```

---

### Commands / NewsCommandTests.cs

- [ ] **`NewsWithTopicWorks`** — `SharpMUSH.Tests/Commands/NewsCommandTests.cs:45`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "SharpMUSH", ..., Announce)
  Actually received 5 matching calls.
  ```

---

### Commands / NetworkCommandTests.cs

- [ ] **`SitelockCommand`** — `SharpMUSH.Tests/Commands/NetworkCommandTests.cs:64`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., any OneOf<MarkupString, String>, ..., Announce)
  Actually received 76 matching calls (mock pollution from prior tests).
  ```

---

### Commands / ConfigCommandTests.cs

- [ ] **`DoingPollCommand_WithPattern`** — `SharpMUSH.Tests/Commands/ConfigCommandTests.cs:155`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., any OneOf<MarkupString, String>, ..., Announce)
  Actually received 86 matching calls (mock pollution).
  ```

- [ ] **`DoingPollCommand`** — `SharpMUSH.Tests/Commands/ConfigCommandTests.cs:143`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., any OneOf<MarkupString, String>, ..., Announce)
  Actually received 89 matching calls (mock pollution).
  ```

---

### Commands / SystemCommandTests.cs

- [ ] **`FlagCommand`** — `SharpMUSH.Tests/Commands/SystemCommandTests.cs:32`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Object Flags:"), ..., Announce)
  Actually received 3 matching calls.
  ```

- [ ] **`PowerCommand`** — `SharpMUSH.Tests/Commands/SystemCommandTests.cs:47`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Object Powers:"), ..., Announce)
  Actually received 2 matching calls.
  ```

---

### Commands / ChannelCommandTests.cs

- [ ] **`NscemitCommand`** — `SharpMUSH.Tests/Commands/ChannelCommandTests.cs:109`

  ```
  ReceivedCallsException: Expected to receive a call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "<TestCommandChannel> NscemitCommand: Test message"), ..., NSEmit)
  Actually received no matching calls.
  Received 116 non-matching calls (wrong notification type or wrong channel message).
  ```

---

### Commands / MessageCommandTests.cs

- [ ] **`MessageUsesDefaultWhenAttributeMissing`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:65`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "DefaultMessage_UniqueValue_72914"), ..., Announce)
  Actually received no matching calls. Received 215 non-matching calls.
  ```

- [ ] **`MessageBasic`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:29`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "MessageBasic_UniqueValue_93751"), ..., Announce)
  Actually received no matching calls. Received 222 non-matching calls.
  ```

- [ ] **`MessageNospoofSwitch`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:167`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "MessageNospoof_Value_48203"), ..., NSAnnounce)
  Actually received no matching calls. Received 225 non-matching calls.
  ```

- [ ] **`MessageNoisySwitch`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:128`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "MessageNoisy_Value_55193"), ..., Announce)
  Actually received no matching calls. Received 230 non-matching calls.
  ```

- [ ] **`MessageWithAttribute`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:48`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "MessageWithAttribute_Result_84729:15"), ..., Announce)
  Actually received no matching calls. Received 237 non-matching calls.
  ```

- [ ] **`MessageSilentSwitch`** — `SharpMUSH.Tests/Commands/MessageCommandTests.cs:96`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "MessageSilent_Value_61829"), ..., Announce)
  Actually received no matching calls. Received 4859 non-matching calls.
  ```

---

### Commands / PlayerDestructionTests.cs

- [ ] **`Destroy_Player_RequiresNuke_NukeMarksPlayerAsGoing`** — `SharpMUSH.Tests/Commands/PlayerDestructionTests.cs:123`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    NotifyAndReturn(any DBRef, s => s.Contains("#-1"), s => s.Contains("nuke"), any Boolean)
  Actually received 2 matching calls.
  NotifyAndReturn(#1:..., "#-1 PERMISSION DENIED", "You must use @nuke to destroy a player.", True) — called twice.
  ```

---

### Commands / CommandFlowUnitTests.cs

- [ ] **`Retry`** — `SharpMUSH.Tests/Commands/CommandFlowUnitTests.cs:51`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessageEquals(msg, "-1"), ..., Announce)
  Actually received 1000 matching calls — retry loop is not being bounded correctly.
  ```

---

### Commands / WizardCommandTests.cs

- [ ] **`UptimeCommand`** — `SharpMUSH.Tests/Commands/WizardCommandTests.cs:247`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "SharpMUSH Uptime:"), ..., Announce)
  Actually received no matching calls. Received 2525 non-matching calls.
  ```

---

### Commands / ZoneCommandTests.cs

- [ ] **`ZMRUserDefinedCommandTest`** — `SharpMUSH.Tests/Commands/ZoneCommandTests.cs:298`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "{cmdName}: ZMR command executed"), ..., Announce)
  Actually received no matching calls. Received 2802 non-matching calls.
  ```

- [ ] **`PersonalZoneUserDefinedCommandTest`** — source file location not emitted in stack trace

  ```
  AssertionException: Expected to be false
  but found True
  ```

---

### Commands / BuildingCommandTests.cs

- [ ] **`DescribeCommand_InvalidTarget_ShowsError`** — `SharpMUSH.Tests/Commands/BuildingCommandTests.cs:665`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "I don't see that here."), ..., Announce)
  Actually received 12 matching calls (mock pollution).
  ```

---

### Commands / CommunicationCommandTests.cs

- [ ] **`AddComChannelNotFound`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:312`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., "Channel not found.", ..., Announce)
  Actually received 3 matching calls.
  ```

- [ ] **`NszemitBasic`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:275`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, expectedMsg), ..., NSEmit)
  Actually received no matching calls. Received 3381 non-matching calls.
  ```

- [ ] **`CListBasic(@clist/full)`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:358`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "Name: Public"), ..., Announce)
  Actually received no matching calls. Received 3384 non-matching calls.
  ```

- [ ] **`CListBasic(@clist)`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:358`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "Name: Public"), ..., Announce)
  Actually received no matching calls. Received 3385 non-matching calls.
  ```

- [ ] **`RemitBasic(@remit #0=Test remote emit, Test remote emit)`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:119`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "Test remote emit"), ..., Emit)
  Actually received no matching calls. Received 3664 non-matching calls.
  ```

- [ ] **`NsremitBasic(@nsremit #0=Test nospoof remote)`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:216`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessageEquals(msg, "Test nospoof remote"), ..., NSEmit)
  Actually received no matching calls. Received 3911 non-matching calls.
  ```

- [ ] **`NsoemitBasic(@nsoemit #1=Test nospoof omit)`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:232`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(any AnySharpObject, msg => MessageEquals(msg, "Test nospoof omit"), ..., Emit)
  Actually received 32 matching calls — message is sent to SharpThing instead of SharpPlayer.
  ```

- [ ] **`ZemitBasic`** — `SharpMUSH.Tests/Commands/CommunicationCommandTests.cs:166`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, expectedMsg), ..., Emit)
  Actually received no matching calls. Received 4289 non-matching calls.
  ```

---

### Commands / DatabaseCommandTests.cs

- [ ] **`Test_Sql_InvalidQuery`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:332`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "#-1 SQL ERROR", ..., Announce)
  Actually received 2 matching calls (mock pollution).
  ```

- [ ] **`Test_Sql_Count`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:202`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "3", ..., Announce)
  Actually received 97 matching calls — @list FUNCTIONS output contains "3" and pollutes the check.
  ```

- [ ] **`Test_MapSql_InvalidObjectAttribute`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:319`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "#-1 INVALID OBJECT/ATTRIBUTE", ..., Announce)
  Actually received 2 matching calls (mock pollution).
  ```

- [ ] **`Test_Sql_PrepareSwitch_NoResults`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:391`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg == "", ..., Announce)
  Actually received 2 matching calls (mock pollution).
  ```

- [ ] **`Test_Sql_SelectWithWhere`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:189`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "200", ..., Announce)
  Actually received 2 matching calls.
  Received: "(HTTP): Status 200 OK" and "200" — HTTP status output leaks into SQL test.
  ```

- [ ] **`Test_Sql_SelectSingleRow`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:163`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "test_sql_row1", ..., Announce)
  Actually received 3 matching calls:
    "test_sql_row1", "test_sql_row1\ntest_sql_row2\ntest_sql_row3", "test_sql_row1 100"
  ```

- [ ] **`Test_Sql_PrepareSwitch_SelectWithMultipleParameters`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:363`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "test_sql_row1" AND "test_sql_row2", ..., Announce)
  Actually received 2 matching calls:
    "test_sql_row1\ntest_sql_row2\ntest_sql_row3" and "test_sql_row1\ntest_sql_row2"
  ```

- [ ] **`Test_Sql_PrepareSwitch_WhereClauseWithStringParameter`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:377`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg contains "200", ..., Announce)
  Actually received 3 matching calls.
  Received: "(HTTP): Status 200 OK", "200", "200"
  ```

- [ ] **`Test_Sql_NoResults`** — `SharpMUSH.Tests/Commands/DatabaseCommandTests.cs:215`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => msg == "", ..., Announce)
  Actually received 3 matching calls (mock pollution).
  ```

---

### Commands / DebugVerboseTests.cs

- [ ] **`Debug_ShowsIterTokens_InExpressionText`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:615`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +iter\(Hello,strlen\(##\)\) :$", null, Announce)
  Actually received 2 matching calls (mock pollution — same message fired twice).
  ```

- [ ] **`Debug_ShowsPercentZeroArg_InExpressionText`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:583`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +strlen\(%0\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`Debug_ShowsPercentQRegister_InExpressionText`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:551`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +strlen\(%qa\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`DebugFlag_ShowsNesting_WithIndentation`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:81`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +mul\(add\(11,22\),3\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`DebugFlag_OutputsFunctionEvaluation_WithSpecificValues`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:44`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +add\(123,456\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`Debug_ExactPennMUSHFormat_PostEvalArrow`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:396`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +add\(7,8\) => 15$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`Debug_SetqShowsRegisterName_InExpressionText`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:655`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +setq\(a,TestVal123\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

- [ ] **`Debug_ExactPennMUSHFormat_PreEvalColon`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:374`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +add\(7,8\) :$", null, Announce)
  Actually received 4 matching calls (two different dbref#s, each twice).
  ```

- [ ] **`Debug_NestingUsesSpaceIndentation_MatchesPennMUSH`** — `SharpMUSH.Tests/Commands/DebugVerboseTests.cs:418`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => regex "^#\d+! +strlen\(add\(2,3\)\) :$", null, Announce)
  Actually received 2 matching calls.
  ```

---

### Commands / GeneralCommandTests.cs

- [ ] **`DoListBatchesToOtherPlayers`** — `SharpMUSH.Tests/Commands/GeneralCommandTests.cs:626`

  ```
  ReceivedCallsException: Expected to receive exactly 3 calls matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "Message to other player"), ..., Announce)
  Actually received no matching calls. Received 4779 non-matching calls.
  ```

- [ ] **`WhereIs_ValidPlayer_ReportsLocation`** — `SharpMUSH.Tests/Commands/GeneralCommandTests.cs:328`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., s => MessagePlainTextEquals(s, "One is in Room Zero."), ..., Announce)
  Actually received no matching calls. Received 4785 non-matching calls.
  ```

---

### Commands / PostmanEchoHttpTests.cs

- [ ] **`HttpCommand_GetWithBody_RejectsImmediately`** — `SharpMUSH.Tests/Commands/PostmanEchoHttpTests.cs:329`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextEquals(msg, "GET requests cannot have a body"), ..., Announce)
  Actually received no matching calls. Received 4866 non-matching calls.
  ```

---

### Commands / UtilityCommandTests.cs

- [ ] **`LookBasic`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:68`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Room Zero(#0"), ..., Announce)
  Actually received 11 matching calls (mock pollution).
  ```

- [ ] **`LookBasic_RoomNameHasAnsiMarkup`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:82`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Room Zero(#0") AND markup contains "[", ..., Announce)
  Actually received 10 matching calls (mock pollution).
  ```

- [ ] **`LookAtObject`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:98`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "God(#1"), ..., Announce)
  Actually received 7 matching calls (mock pollution).
  ```

- [ ] **`ExamineObject_HeaderContainsNameAndDbref`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:113`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "God(#1"), ..., Announce)
  Actually received 8 matching calls (mock pollution).
  ```

- [ ] **`ExamineObject_NameRowHasAnsiMarkup`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:127`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "God(#1") AND markup contains "[", ..., Announce)
  Actually received 9 matching calls (mock pollution).
  ```

- [ ] **`ExamineObject_HeaderContainsOwnerRow`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:143`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Owner: "), ..., Announce)
  Actually received no matching calls. Received 5240 non-matching calls.
  ```

- [ ] **`ExamineObject_HeaderContainsZoneAndPowers`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:156`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Zone: *NOTHING*"), ..., Announce)
  Actually received no matching calls. Received 5213 non-matching calls.
  ```

- [ ] **`ExamineObject_HeaderContainsWarningsChecked`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:173`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Warnings checked:"), ..., Announce)
  Actually received no matching calls. Received 5059 non-matching calls.
  ```

- [ ] **`ExamineObject_HeaderContainsLastModified`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:186`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Last modified:"), ..., Announce)
  Actually received no matching calls. Received 5032 non-matching calls.
  ```

- [ ] **`ExaminePlayer_HeaderContainsQuota`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:199`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Quota:"), ..., Announce)
  Actually received no matching calls. Received 5002 non-matching calls.
  ```

- [ ] **`ExamineRoom_ShowsExits`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:220`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Exits:"), ..., Announce)
  Actually received 2 matching calls (mock pollution).
  ```

- [ ] **`ExamineObject_BriefSwitch_AlsoShowsLastModified`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:233`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Last modified:"), ..., Announce)
  Actually received no matching calls. Received 5086 non-matching calls.
  ```

- [ ] **`ExamineObject_AttributeWithAnsi_PreservesMarkup`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:257`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextContains(msg, "AnsiColorText"), ..., Announce)
  Actually received 2 matching calls (mock pollution).
  ```

- [ ] **`ExamineObject_BriefSwitch_ShowsHeaderNotDescription`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:284`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Owner: "), ..., Announce)
  Actually received no matching calls. Received 4971 non-matching calls.
  ```

- [ ] **`ExamineObjectOpaqueSwitch`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:303`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "God(#1"), ..., Announce)
  Actually received 5 matching calls (mock pollution).
  ```

- [ ] **`ExamineCurrentLocation`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:331`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., msg => MessagePlainTextStartsWith(msg, "Room Zero(#0"), ..., Announce)
  Actually received 9 matching calls (mock pollution).
  ```

- [ ] **`VersionCommand`** — `SharpMUSH.Tests/Commands/UtilityCommandTests.cs:397`

  ```
  ReceivedCallsException: Expected to receive exactly 1 call matching:
    Notify(..., s => MessagePlainTextStartsWith(s, "SharpMUSH version 0"), ..., Announce)
  Actually received no matching calls. Received 5186 non-matching calls.
  ```

---

## Summary by Category

| Category | Count | Root Cause |
|---|---|---|
| Mock pollution (received N > 1) | ~35 | Shared `INotifyService` mock not reset between parallel tests |
| Missing call (received 0) | ~36 | Feature broken, command not routing, or wrong `NotificationType` |
| Retry loop unbounded | 1 | `Retry` test fires 1000 times instead of once |
| Assertion error (bool) | 1 | `PersonalZoneUserDefinedCommandTest` — logic returns `true` when expecting `false` |

## Suggested Fix Direction

1. **Mock isolation** — ensure the `INotifyService` mock is created fresh per-test (or per-test-class) rather than shared across all tests running in the same process. Use `[Before(Test)]` / `[After(Test)]` hooks in TUnit, or switch to `Received(Quantity.AtLeast(1))` where exact counts are not meaningful.
2. **Zero-call failures** — investigate the @examine, @message, @where, @uptime, @version, @zmr commands individually; the command dispatch or permission checks are likely returning early.
3. **`Retry` loop** — the command should short-circuit after hitting the iteration limit; the test is asserting on exactly 1 notification but the loop emits 1000.
4. **`PersonalZoneUserDefinedCommandTest`** — the assertion `Assert.IsFalse(...)` is failing, meaning some boolean condition in zone-command registration is unexpectedly `true`.
