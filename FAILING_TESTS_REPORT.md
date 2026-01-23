# SharpMUSH Test Failure Report

**Generated:** 2026-01-22 23:30:21

---

## Executive Summary

| Status | Count | Percentage |
|--------|-------|------------|
| ✅ Passed | 722 | 54.8% |
| ❌ Failed | 375 | 28.5% |
| ⏭️ Skipped | 221 | 16.8% |
| **Total** | **1318** | **100%** |

---

## Failed Tests by Category

### Category Summary

- **1. DBRef Parse Error (Discriminated Union)**: 45 failures
- **2. NotifyService Not Set**: 128 failures
- **3. Mock Assertion Failure**: 129 failures
- **9. Other/Unknown**: 71 failures

---

### 1. DBRef Parse Error (Discriminated Union)

**Total:** 45 failed tests

#### 1. `CreateObjectWithCost`

**Duration:** 101ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.CreateObjectWithCost() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:37
```

#### 2. `DigRoomWithExits`

**Duration:** 5s 102ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.DigRoomWithExits() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:160
```

#### 3. `DoDigForCommandListCheck2`

**Duration:** 2s 232ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.DoDigForCommandListCheck2() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:91
```

#### 4. `ParentSetAndGet`

**Duration:** 134ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ParentSetAndGet() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:218
```

#### 5. `ChzoneObject`

**Duration:** 173ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ChzoneObject() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:449
```

#### 6. `ParentUnset`

**Duration:** 188ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ParentUnset() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:248
```

#### 7. `ParentCycleDetection_DirectCycle`

**Duration:** 1s 086ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ParentCycleDetection_DirectCycle() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:276
```

#### 8. `CreateObject`

**Duration:** 105ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.CreateObject() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:26
```

#### 9. `ParentCycleDetection_SelfParent`

**Duration:** 60ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ParentCycleDetection_SelfParent() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:353
```

#### 10. `ParentCycleDetection_LongChain`

**Duration:** 1s 055ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: InvalidOperationException: Cannot return as T0 as result is T1
at OneOf.OneOfBase`2.get_AsT0()
at SharpMUSH.Library.DiscriminatedUnions.Option`1.AsValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/DiscriminatedUnions/Option.cs:11
at SharpMUSH.Library.Models.DBRef.Parse(String value) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Library/Models/DBref.cs:42
at SharpMUSH.Tests.Commands.BuildingCommandTests.ParentCycleDetection_LongChain() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:378
```

<details>
<summary>Additional 35 failed tests in this category (click to expand)</summary>

- `ParentCycleDetection_IndirectCycle`
- `UUNLOCK_CommandExecutes`
- `ULOCK_CommandExecutes`
- `EUNLOCK_CommandExecutes`
- `ELOCK_CommandExecutes`
- `GetCommand`
- `ChzoneSetZone`
- `ChzonePermissionSuccess`
- `ChzoneClearZone`
- `ChzoneInvalidZone`
- `GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf`
- `GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence`
- `GetAttributeWithInheritance_ParentTakesPrecedenceOverZone`
- `GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent`
- `GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone`
- `GetAttributeWithInheritance_NonExistentAttribute_ReturnsNull`
- `GetAttributeWithInheritance_NestedAttributes_WorksCorrectly`
- `GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject`
- `FilterByCombinedCriteria_ReturnsMatchingObjects`
- `FilterByZone_ReturnsZonedObjects`
- `FilterByDbRefRange_ReturnsObjectsInRange`
- `ObjectCanBeZone`
- `UpdateObjectZone`
- `SetObjectZone`
- `SetObjectZoneToNull`
- `MultipleObjectsSameZone`
- `UnsetObjectZone`
- `DoDigForCommandListCheck`
- `DigRoom`
- `SortKey(sortkey(test/key,abc ab a), a ab abc)`
- `SortBy(sortby(test/comp,c a b), a b c)`
- `Filter(filter(test/is_odd,1 2 3 4 5 6), 1 3 5)`
- `Fold(fold(test/add_func,1 2 3), 6)`
- `Step(step(test/first,a b c d e,2), a c e)`
- `Munge(munge(test/sort,b a c,2 1 3), 1 2 3)`

</details>

---

### 2. NotifyService Not Set

**Total:** 128 failed tests

#### 1. `AhelpNonExistentTopic`

**Duration:** 157ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso msg.AsT0.ToString().Contains("No admin help available")) OrElse (msg.IsT1 AndAlso msg.AsT1.Contains("No admin help available"))), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.Ahe
```

#### 2. `AhelpWithTopicWorks`

**Duration:** 7s 821ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso msg.AsT0.ToString().Contains("Security")) OrElse (msg.IsT1 AndAlso msg.AsT1.Contains("Security"))), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Routing.Route.Handle(ICall call)
at NSubstitute.Core.CallRouter.Route(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpOb
```

#### 3. `AnewsAliasWorks`

**Duration:** 230ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString().Contains("ahelp") OrElse msg.AsT0.ToString().Contains("admin"))) OrElse (msg.IsT1 AndAlso (msg.AsT1.Contains("ahelp") OrElse msg.AsT1.Contains("admin")))), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, Notific
```

#### 4. `AhelpCommandWorks`

**Duration:** 149ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString().Contains("ahelp") OrElse msg.AsT0.ToString().Contains("admin"))) OrElse (msg.IsT1 AndAlso (msg.AsT1.Contains("ahelp") OrElse msg.AsT1.Contains("admin")))), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, Notific
```

#### 5. `List_Motd_DisplaysMotdSettings`

**Duration:** 94ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, s => MessageContains(s, "Current Message of the Day settings:"), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.AtListCommandTests.List_Motd_DisplaysMotdSettings() in /home/runner/work/SharpMUSH/SharpMUSH/S
```

#### 6. `List_Functions_DisplaysFunctionList`

**Duration:** 1s 154ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive exactly 1 call matching:
Notify(any AnySharpObject, s => MessageContains(s, "FUNCTIONS:"), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.AtListCommandTests.List_Functions_DisplaysFunctionList() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Test
```

#### 7. `Enable_NonBooleanOption_ReturnsInvalidType`

**Duration:** 155ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, s => s.Value.ToString().Contains("not a boolean option"), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.ConfigCommandTests.Enable_NonBooleanOption_ReturnsInvalidType() in /home/runner/work/SharpMUSH/SharpM
```

#### 8. `Disable_BooleanOption_ShowsImplementationMessage`

**Duration:** 117ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, s => ((s.Value.ToString().Contains("@disable") AndAlso s.Value.ToString().Contains("@config/set")) AndAlso s.Value.ToString().Contains("noisy_whisper")), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.Confi
```

#### 9. `ConfigCommand_OptionArg_ShowsOptionValue`

**Duration:** 136ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, s => MessageContains(s, "mud_name"), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.ConfigCommandTests.ConfigCommand_OptionArg_ShowsOptionValue() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Com
```

#### 10. `ListmotdCommand`

**Duration:** 65ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, s => MessageContains(s, "Message of the Day settings"), any AnySharpObject, any INotifyService+NotificationType)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.ConfigCommandTests.ListmotdCommand() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/
```

<details>
<summary>Additional 118 failed tests in this category (click to expand)</summary>

- `ConfigCommand_InvalidOption_ReturnsNotFound`
- `Disable_InvalidOption_ReturnsNotFound`
- `Disable_NonBooleanOption_ReturnsInvalidType`
- `Enable_InvalidOption_ReturnsNotFound`
- `Enable_NoArguments_ShowsUsage`
- `Disable_NoArguments_ShowsUsage`
- `Enable_BooleanOption_ShowsImplementationMessage`
- `List_Attribs_DisplaysStandardAttributes`
- `DebugFlag_OutputsStackRegisters_WhenSet`
- `List_Commands_DisplaysCommandList`
- `DebugFlag_ShowsNesting_WithIndentation`
- `DebugFlag_OutputsFunctionEvaluation_WithSpecificValues`
- `VerboseFlag_OutputsCommandExecution`
- `DebugFlag_OutputsQRegisters_WhenSet`
- `DebugFlag_OutputsBothQAndStackRegisters_Separately`
- `Power_Add_RequiresBothArguments`
- `Flag_Add_PreventsDuplicateFlags`
- `List_Powers_DisplaysPowerList`
- `List_NoSwitch_DisplaysHelpMessage`
- `Power_List_DisplaysAllPowers`
- `List_Flags_DisplaysFlagList`
- `Flag_Add_RequiresBothArguments`
- `Flag_Delete_PreventsSystemFlagDeletion`
- `Flag_List_DisplaysAllFlags`
- `Find_SearchesForObjects`
- `WhereIs_NonPlayer_ReturnsError`
- `SimpleCommandParse(@pemit #1=1 This is a test, 1 This is a test)`
- `WhereIs_ValidPlayer_ReportsLocation`
- `SimpleCommandParse(@pemit #1=2 This is a test;, 2 This is a test;)`
- `List_Locks_DisplaysLockTypes`
- `Map_ExecutesAttributeOverList`
- `Include_InsertsAttributeInPlace`
- `Select_MatchesFirstExpression`
- `Search_PerformsDatabaseSearch`
- `Entrances_ShowsLinkedObjects`
- `Stats_ShowsDatabaseStatistics`
- `HelpWithTopicWorks`
- `HelpWithWildcardWorks`
- `HelpCommandWorks`
- `HelpNonExistentTopic`
- `HelpSearchWorks`
- `NewsCommandWorks`
- `NewsWithWildcardWorks`
- `NewsWithTopicWorks`
- `NewsNonExistentTopic`
- `WCheckCommand_SpecificObject`
- `WCheckCommand_NoArguments_ShowsUsage`
- `DecompileCommand`
- `WarningsCommand_SetToAll`
- `WarningsCommand_SetToNormal`
- `WarningsCommand_WithNegation`
- `WarningsCommand_SetToNone`
- `WarningsCommand_NoArguments_ShowsUsage`
- `WarningsCommand_WithUnknownWarning`
- `ChzoneInvalidObject`
- `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_UPPER,TEST_VALUE)][grep(%!,Test_Grep_CaseSensitive_*,VALUE)], TEST_GREP_CASESENSITIVE_UPPER)`
- `Test_Grepi_CaseInsensitive([attrib_set(%!/Test_Grepi_CaseInsensitive2_1,has_TEST)][attrib_set(%!/Test_Grepi_CaseInsensitive2_2,also_TEST)][attrib_set(%!/Test_Grepi_CaseInsensitive2_UPPER,more_TEST)][grepi(%!,Test_Grepi_CaseInsensitive2_*,TEST)], TEST_GREPI_CASEINSENSITIVE2_1 TEST_GREPI_CASEINSENSITIVE2_2 TEST_GREPI_CASEINSENSITIVE2_UPPER)`
- `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_1,test_string_grep_case1)][attrib_set(%!/Test_Grep_CaseSensitive_2,another_test_value)][attrib_set(%!/NO_MATCH,different)][grep(%!,Test_Grep_CaseSensitive_*,test)], TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2)`
- `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_1,has_test_in_value)][attrib_set(%!/Test_Grep_CaseSensitive_2,also_test_here)][attrib_set(%!/Test_Grep_CaseSensitive_2_EMPTY_TEST,)][grep(%!,*Test_Grep_CaseSensitive_*,test)], TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2)`
- `Test_Grepi_CaseInsensitive([attrib_set(%!/Test_Grepi_CaseInsensitive1_1,has_VALUE)][attrib_set(%!/Test_Grepi_CaseInsensitive1_2,also_VALUE)][attrib_set(%!/Test_Grepi_CaseInsensitive1_UPPER,more_VALUE)][grepi(%!,Test_Grepi_CaseInsensitive1_*,VALUE)], TEST_GREPI_CASEINSENSITIVE1_1 TEST_GREPI_CASEINSENSITIVE1_2 TEST_GREPI_CASEINSENSITIVE1_UPPER)`
- `Test_Wildgrep_Pattern([attrib_set(%!/WILDGREP_1,test_wildcard_*_match)][attrib_set(%!/WILDGREP_2,different)][wildgrep(%!,WILDGREP_*,*wildcard*)], WILDGREP_1)`
- `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_001,value1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_002,value2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_100,value3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX1_00\[0-9\]$)], TESTREGLATTR_UNIQUE_RGX1_001 TESTREGLATTR_UNIQUE_RGX1_002)`
- `Test_Wildgrep_Pattern([attrib_set(%!/WILDGREP_1,test_wildcard_value_match)][wildgrep(%!,WILDGREP_*,test_*_match)], WILDGREP_1)`
- `Test_Wildgrepi_CaseInsensitive([attrib_set(%!/WILDGREP_1,has_WILDCARD)][attrib_set(%!/WILDGREP_UPPER,TEST_WILDCARD)][wildgrepi(%!,WILDGREP_*,*WILDCARD*)], WILDGREP_1 WILDGREP_UPPER)`
- `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex1_001,value1)][attrib_set(%!/Test_Regxattr_RangeWithRegex1_002,value2)][attrib_set(%!/Test_Regxattr_RangeWithRegex1_100,value3)][regxattr(%!/Test_Regxattr_RangeWithRegex1_\[0-9\]+,1,2)], TEST_REGXATTR_RANGEWITHREGEX1_001 TEST_REGXATTR_RANGEWITHREGEX1_002)`
- `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_001,value1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_002,value2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_100,value3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX2_\[0-9\]+$)], TESTREGLATTR_UNIQUE_RGX2_001 TESTREGLATTR_UNIQUE_RGX2_002 TESTREGLATTR_UNIQUE_RGX2_100)`
- `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_X,val1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Y,val2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Z,val3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT3_\[XYZ\]$)], 3)`
- `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_001,value1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_002,value2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_100,value3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT1_\[0-9\]+$)], 3)`
- `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_A,val1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_B,val2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_UPPER,val3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT2_\[A-Z\]+$)], 3)`
- `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_A,val1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_B,val2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_UPPER,val3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX3_\[A-Z\]+$)], TESTREGLATTR_UNIQUE_RGX3_A TESTREGLATTR_UNIQUE_RGX3_B TESTREGLATTR_UNIQUE_RGX3_UPPER)`
- `SetAndGet([attrib_set(%!/attribute,ansi(hr,ZIP!))][get(%!/attribute)][attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)], ␛[1;31mZIP!␛[0m␛[1;31mZAP!␛[0m)`
- `SetAndGet([attrib_set(%!/attribute,ZAP!)][get(%!/attribute)], ZAP!)`
- `SetAndGet([attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)], ␛[1;31mZAP!␛[0m)`
- `Test_Basic_AttribSet_And_Get([attrib_set(%!/Test_Basic_AttribSet_And_Get21,val1)][attrib_set(%!/Test_Basic_AttribSet_And_Get22,val2)][get(%!/Test_Basic_AttribSet_And_Get21)][get(%!/Test_Basic_AttribSet_And_Get22)], val1val2)`
- `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex2_001,value1)][attrib_set(%!/Test_Regxattr_RangeWithRegex2_002,value2)][attrib_set(%!/Test_Regxattr_RangeWithRegex2_100,value3)][regxattr(%!/Test_Regxattr_RangeWithRegex2_\[0-9\]+,2,2)], TEST_REGXATTR_RANGEWITHREGEX2_002 TEST_REGXATTR_RANGEWITHREGEX2_100)`
- `Test_Lattr_Simple([attrib_set(%!/Test_Lattr_Simple1,v1)][attrib_set(%!/Test_Lattr_Simple2,v2)][lattr(%!/Test_Lattr_Simple*)], TEST_LATTR_SIMPLE1 TEST_LATTR_SIMPLE2)`
- `Test_Basic_AttribSet_And_Get([attrib_set(%!/Test_Basic_AttribSet_And_Get,testvalue)][get(%!/Test_Basic_AttribSet_And_Get)], testvalue)`
- `Test_Regxattr_AttributeTrees([attrib_set(%!/Test_Regxattr_AttributeTrees,v1)][attrib_set(%!/Test_Regxattr_AttributeTrees`A,v2)][attrib_set(%!/Test_Regxattr_AttributeTrees`B,v3)][attrib_set(%!/Test_Regxattr_AttributeTrees`C,v4)][regxattr(%!/^Test_Regxattr_AttributeTrees,2,2)], TEST_REGXATTR_ATTRIBUTETREES`A TEST_REGXATTR_ATTRIBUTETREES`B)`
- `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex3_1,val1)][attrib_set(%!/Test_Regxattr_RangeWithRegex3_2,val2)][regxattr(%!/^Test_Regxattr_RangeWithRegex3_,1,2)], TEST_REGXATTR_RANGEWITHREGEX3_1 TEST_REGXATTR_RANGEWITHREGEX3_2)`
- `Test_Regnattr_AttributeTrees([attrib_set(%!/Test_Regnattr_AttributeTrees1,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L1,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L2,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L1`L2,v)][regnattr(%!/^Test_Regnattr_AttributeTrees1)], 4)`
- `Test_Wildgrep_AttributeTrees([attrib_set(%!/Test_Wildgrep_AttributeTrees,val)][attrib_set(%!/Test_Wildgrep_AttributeTrees`CHILD,has_pattern)][attrib_set(%!/Test_Wildgrep_AttributeTrees`OTHER,no_match)][wildgrep(%!,Test_Wildgrep_AttributeTrees**,*pattern*)], TEST_WILDGREP_ATTRIBUTETREES`CHILD)`
- `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees3,value)][attrib_set(%!/Test_Lattr_AttributeTrees3`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees3*)], TEST_LATTR_ATTRIBUTETREES3)`
- `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees,root)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1,leaf1)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH2,leaf2)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1`SUBLEAF,deep)][lattr(%!/Test_Lattr_AttributeTrees`**)], TEST_LATTR_ATTRIBUTETREES`BRANCH1 TEST_LATTR_ATTRIBUTETREES`BRANCH1`SUBLEAF TEST_LATTR_ATTRIBUTETREES`BRANCH2)`
- `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees2,value)][attrib_set(%!/Test_Lattr_AttributeTrees2`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees2**)], TEST_LATTR_ATTRIBUTETREES2 TEST_LATTR_ATTRIBUTETREES2`CHILD)`
- `Test_Grep_AttributeTrees([attrib_set(%!/Test_Grep_AttributeTrees_2,test)][attrib_set(%!/Test_Grep_AttributeTrees_2`SUB1,contains_test)][attrib_set(%!/Test_Grep_AttributeTrees_2`SUB2,no_match)][grep(%!,Test_Grep_AttributeTrees_2**,test)], TEST_GREP_ATTRIBUTETREES_2 TEST_GREP_ATTRIBUTETREES_2`SUB1)`
- `Test_Reglattr_AttributeTrees([attrib_set(%!/Test_Reglattr_AttributeTrees2_001,v1)][attrib_set(%!/Test_Reglattr_AttributeTrees2_001`SUB,v2)][reglattr(%!/Test_Reglattr_AttributeTrees2_\[0-9\]+)], TEST_REGLATTR_ATTRIBUTETREES2_001 TEST_REGLATTR_ATTRIBUTETREES2_001`SUB)`
- `Test_Reglattr_AttributeTrees([attrib_set(%!/Test_Reglattr_AttributeTrees1,val)][attrib_set(%!/Test_Reglattr_AttributeTrees1`A,val1)][attrib_set(%!/Test_Reglattr_AttributeTrees1`B,val2)][attrib_set(%!/Test_Reglattr_AttributeTrees1`A`DEEP,val3)][reglattr(%!/^Test_Reglattr_AttributeTrees1)], TEST_REGLATTR_ATTRIBUTETREES1 TEST_REGLATTR_ATTRIBUTETREES1`A TEST_REGLATTR_ATTRIBUTETREES1`A`DEEP TEST_REGLATTR_ATTRIBUTETREES1`B)`
- `Test_Grep_AttributeTrees([attrib_set(%!/Test_Grep_AttributeTrees,root)][attrib_set(%!/Test_Grep_AttributeTrees`BRANCH1,has_search_term)][attrib_set(%!/Test_Grep_AttributeTrees`BRANCH2,different)][grep(%!,Test_Grep_AttributeTrees**,search)], TEST_GREP_ATTRIBUTETREES`BRANCH1)`
- `Test_Wildgrep_InAttributeTree`
- `Test_Reglattr_WithRegex`
- `Test_Wildcard_EntireSubtree`
- `Test_SpecialCharacters_Escaped`
- `Test_Regnattr_AttributeTrees([attrib_set(%!/Test_Regnattr_AttributeTrees2,v)][attrib_set(%!/Test_Regnattr_AttributeTrees2`A,v)][attrib_set(%!/Test_Regnattr_AttributeTrees2`B,v)][regnattr(%!/^Test_Regnattr_AttributeTrees2)], 3)`
- `Test_Wildcard_Grandchildren`
- `Test_Nattr_Counting`
- `Test_Xattr_RangeInTree`
- `Test_Wildcard_QuestionMark`
- `Test_Wildcard_ImmediateChildren`
- `Test_Grep_InAttributeTree`
- `Test_Wildcard_DoubleStar_MatchAll`
- `Test_Wildcard_Star_NoBacktick`
- `Nscemit_WithNonExistentChannel_ReturnsError`
- `Cemit_WithNonExistentChannel_ReturnsError`
- `PemitPort`
- `PrivateEmit`
- `Nspemit`
- `Nsremit(nsremit(#0,test), )`
- `Remit(remit(#0,test message), )`
- `Nsoemit(nsoemit(#1,test), )`
- `Oemit(oemit(#1,test message), )`
- `Nsprompt(nsprompt(#1,test), )`
- `Conn`
- `Lwhoid(lwhoid(), )`
- `ListWho`
- `Test_Doing_WithInvalidPlayerName`
- `Table(table(a b c,10,2), )`
- `Money(money(%#), #-1 NOT SUPPORTED)`
- `Atrlock(atrlock(%#,testattr), )`

</details>

---

### 3. Mock Assertion Failure

**Total:** 129 failed tests

#### 1. `RecycleObject`

**Duration:** 139ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive exactly 1 call matching:
Notify(any AnySharpObject, msg => MessageContains(msg, "Marked for destruction"), <null>, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.BuildingCommandTests.RecycleObject() in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/BuildingCommandTests.cs:473
```

#### 2. `NscemitCommand`

**Duration:** 1s 148ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso msg.AsT0.ToString().Contains("NscemitCommand: Test message")) OrElse (msg.IsT1 AndAlso msg.AsT1.Contains("NscemitCommand: Test message"))), any AnySharpObject, NSEmit)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.ChannelCommandTests.Ns
```

#### 3. `CemitCommand`

**Duration:** 3s 149ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == "<TestCommandChannel> CemitCommand: Test message")) OrElse (msg.IsT1 AndAlso (msg.AsT1 == "<TestCommandChannel> CemitCommand: Test message"))), any AnySharpObject, Emit)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Comma
```

#### 4. `IfElse(@ifelse 1=@pemit #1=1 True,@pemit #1=1 False, 1 True)`

**Duration:** 1s 370ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 5. `IfElse(@ifelse 0=@pemit #1=2 True,@pemit #1=2 False, 2 False)`

**Duration:** 246ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 6. `IfElse(@ifelse 1=@pemit #1=3 True, 3 True)`

**Duration:** 252ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 7. `IfElse(@ifelse 0={@pemit #1=5 True},{@pemit #1=5 False}, 5 False)`

**Duration:** 191ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 8. `IfElse(@ifelse 1={@pemit #1=4 True},{@pemit #1=4 False}, 4 True)`

**Duration:** 242ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 9. `IfElse(@ifelse 1={@pemit #1=6 True}, 6 True)`

**Duration:** 1s 133ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive a call matching:
Notify(any AnySharpObject, msg => ((msg.IsT0 AndAlso (msg.AsT0.ToString() == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected)) OrElse (msg.IsT1 AndAlso (msg.AsT1 == value(SharpMUSH.Tests.Commands.CommandFlowUnitTests+<>c__DisplayClass4_0).expected))), any AnySharpObject, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, 
```

#### 10. `Test(think [add(1,2)]2, 32)`

**Duration:** 258ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: ReceivedCallsException: Expected to receive exactly 1 call matching:
Notify(any AnySharpObject, System.String: 32, <null>, Announce)
Actually received no matching calls.
at NSubstitute.Core.ReceivedCallsExceptionThrower.Throw(ICallSpecification callSpecification, IEnumerable`1 matchingCalls, IEnumerable`1 nonMatchingCalls, Quantity requiredQuantity)
at NSubstitute.Routing.Handlers.CheckReceivedCallsHandler.Handle(ICall call)
at NSubstitute.Proxies.CastleDynamicProxy.CastleForwardingInterceptor.Intercept(IInvocation invocation)
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.DynamicProxy.AbstractInvocation.Proceed()
at Castle.Proxies.ObjectProxy_2.Notify(AnySharpObject who, OneOf`2 what, AnySharpObject sender, NotificationType type)
at SharpMUSH.Tests.Commands.CommandUnitTests.Test(String str, String expected) in /home/runner/work/SharpMUSH/SharpMUSH/SharpMUSH.Tests/Commands/CommandUnitTests.cs:33
info: SharpMUSH.Messaging.Kafka
```

<details>
<summary>Additional 119 failed tests in this category (click to expand)</summary>

- `Test(think Command1 Arg;think Command2 Arg, Command1 Arg;think Command2 Arg)`
- `Test(think add(1,2)1, 31)`
- `Test(]think [add(1,2)]3, [add(1,2)]3)`
- `ComListBasic(comlist)`
- `CListBasic(@clist/full)`
- `PemitBasic(@pemit #1=Test message, Test message)`
- `PemitBasic(@pemit #1=Another test, Another test)`
- `AddComBasic(addcom test_alias_ADDCOM1=Public)`
- `AddComBasic(addcom test_alias_ADDCOM2=Public)`
- `DelComNotFound(delcom nonexistent_alias_DELCOM)`
- `DelComBasic(delcom test_alias_DELCOM1)`
- `CListBasic(@clist)`
- `DoingPollCommand`
- `DoingPollCommand_WithPattern`
- `ComTitleNotFound(comtitle nonexistent_alias_COMTITLE=title)`
- `SkipCommand`
- `IfElseCommand`
- `Test_Sql_SelectMultipleRows`
- `Test_MapSql_WithMultipleRows`
- `Test_Sql_SelectWithWhere`
- `Test_MapSql_WithColnamesSwitch`
- `Test_MapSql_Basic`
- `Test_MapSql_InvalidObjectAttribute`
- `Test_Sql_Count`
- `Test_Sql_InvalidQuery`
- `DoBreakSimpleTruthyCommandList`
- `DoBreakSimpleCommandList`
- `DoListComplex5`
- `DoListComplex4`
- `DoBreakSimpleFalsyCommandList`
- `DoListComplex2`
- `DoListComplex6`
- `DoListSimple2`
- `DoListComplex`
- `DoListWithDelimiter`
- `DoListSimple`
- `NestedDoListWithBreakFlushesMessages`
- `DoBreakCommandList`
- `DoListWithoutBreak_AllMessagesReceived`
- `DoListWithDBRefNotificationBatching`
- `DoListBatchesToOtherPlayers`
- `DoListWithBreakAfterFirst_OnlyFirstMessageReceived`
- `DoBreakCommandList2`
- `DoListWithBreakFlushesMessages`
- `ConnectGuest_CaseInsensitive_Succeeds`
- `ConnectGuest_BasicLogin_Succeeds`
- `ConnectGuest_MultipleGuests_SelectsAppropriateOne`
- `NestedDoListBatching`
- `DoListComplex3`
- `Test_Edit_NoMatch`
- `Test_Respond_StatusCode_404`
- `Test_Respond_StatusCode`
- `Test_Respond_StatusCode_OutOfRange`
- `Test_Respond_StatusLine_TooLong`
- `Test_Respond_StatusCode_WithoutText`
- `Test_Respond_Type_Empty`
- `Test_CopyAttribute_InvalidSource`
- `Test_Respond_Type_ApplicationJson`
- `Test_Respond_Type_TextHtml`
- `Test_Respond_Header_SetCookie`
- `Test_Respond_Header_CustomHeader`
- `Test_Respond_InvalidStatusCode`
- `Test_Respond_Header_WithoutEquals`
- `LogCommand_WithCmdSwitch_LogsToCommandCategory`
- `LogCommand_RecallSwitch_RetrievesLogs`
- `Test_Respond_Header_ContentLength_Forbidden`
- `LogCommand_WithErrSwitch_LogsToErrorCategory`
- `Test_Respond_Header_EmptyName`
- `LogCommand_DefaultSwitch_LogsToCommandCategory`
- `LogCommand_NoMessage_ReturnsError`
- `LogCommand_WithWizSwitch_LogsToWizardCategory`
- `MetricsCommand_Query_ExecutesCustomPromQL`
- `MetricsCommand_Slowest_ReturnsSlowOperations(5m)`
- `MetricsCommand_SlowestFunctions_ReturnsOnlyFunctions`
- `MetricsCommand_NoSwitch_ShowsUsage`
- `MetricsCommand_Connections_ReturnsConnectionMetrics`
- `MetricsCommand_Popular_ReturnsMostCalledOperations(5m)`
- `MetricsCommand_PopularFunctions_ReturnsOnlyFunctions`
- `MetricsCommand_Slowest_ReturnsSlowOperations(24h)`
- `MetricsCommand_Slowest_ReturnsSlowOperations(1h)`
- `MetricsCommand_WithLimit_RespectsLimit`
- `MetricsCommand_Health_ReturnsHealthStatus`
- `MetricsCommand_SlowestCommands_ReturnsOnlyCommands`
- `MetricsCommand_Popular_ReturnsMostCalledOperations(1h)`
- `GrepCommand_WithRegexpSwitch`
- `GrepCommand`
- `Test_MoveAttribute_InvalidSource`
- `GrepCommand_WithPrintSwitch`
- `GrepCommand_WithWildSwitch`
- `GrepCommand_WithAttributePattern`
- `GrepCommand_WithNocaseSwitch`
- `SitelockCommand`
- `NotifySetQ_ShouldSetQRegisterForWaitingTask`
- `DolistInline_ShouldExecuteImmediately`
- `ExamineObjectBriefSwitch`
- `NotifyCommand_ShouldWakeWaitingTask`
- `ExamineCurrentLocation`
- `ExamineWithAttributePattern`
- `ExamineObjectOpaqueSwitch`
- `ExamineObject`
- `ThinkBasic`
- `ThinkWithFunction`
- `ChownallCommand`
- `PollClearCommand`
- `ShutdownRebootCommand`
- `SuggestAddCommand`
- `SuggestListCommand`
- `ReadCacheCommand`
- `PollSetCommand`
- `PurgeCommand`
- `ShutdownCommand`
- `Hide_AlreadyHidden_ShowsAppropriateMessage`
- `Hide_OnSwitch_SetsHidden`
- `AllhaltCommand`
- `PollCommand`
- `Hide_NoSwitch_UnsetsHidden`
- `Hide_YesSwitch_SetsHidden`
- `Test_AtrChown_InvalidArguments`
- `Test_AtrLock_QueryStatus`

</details>

---

### 9. Other/Unknown

**Total:** 71 failed tests

#### 1. `SetFlag`

**Duration:** 1s 056ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to be true
but found False
at Assert.That(flags.Any(x => x.Name == "MONITOR" || x.Name == "DEBUG")).IsTrue()
```

#### 2. `Test_Sql_SelectSingleRow`

**Duration:** 1s 956ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to contain "test_sql_row1"
but found "#-1 SQL ERROR: Unable to connect to any of the specified MySQL hosts."
at Assert.That(plainText).Contains("test_sql_row1")
```

#### 3. `Test_Sql_NoResults`

**Duration:** 1s 703ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to be empty or whitespace
but found "#-1 SQL ERROR: Unable to connect to any of the specified MySQL hosts."
at Assert.That(plainText).IsEmpty()
```

#### 4. `Power_Add_CreatesNewPower`

**Duration:** 165ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to not be null
but value is null
at Assert.That(createdPower).IsNotNull()
```

#### 5. `Power_Delete_RemovesNonSystemPower`

**Duration:** 183ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to be null
but found SharpMUSH.Library.Models.SharpPower
at Assert.That(deletedPower).IsNull()
```

#### 6. `Flag_Delete_RemovesNonSystemFlag`

**Duration:** 172ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to be null
but found SharpMUSH.Library.Models.SharpObjectFlag
at Assert.That(deletedFlag).IsNull()
```

#### 7. `Power_Add_PreventsSystemPowerCreation`

**Duration:** 204ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to not be null
but value is null
at Assert.That(createdPower).IsNotNull()
```

#### 8. `Power_Disable_DisablesNonSystemPower`

**Duration:** 175ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to not be null
but value is null
at Assert.That(power).IsNotNull()
```

#### 9. `Flag_Disable_DisablesNonSystemFlag`

**Duration:** 143ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to not be null
but value is null
at Assert.That(flag).IsNotNull()
```

#### 10. `Flag_Add_CreatesNewFlag`

**Duration:** 146ms

**Error Details:**
```
TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to not be null
but value is null
at Assert.That(createdFlag).IsNotNull()
```

<details>
<summary>Additional 61 failed tests in this category (click to expand)</summary>

- `Flag_Enable_EnablesDisabledFlag`
- `Flag_Add_PreventsSystemFlagCreation`
- `Test_Edit_FirstOnly`
- `DoFlagSet`
- `Attribute_AccessCreatesAttributeEntry`
- `Attribute_EntryFlagsAreAppliedWhenAttributeCreated`
- `Test_Edit_Prepend`
- `ConnectGuest_NoGuestCharacters_FailsWithError`
- `MessageSilentSwitch`
- `MessageBasic`
- `MessageNoisySwitch`
- `MessageNospoofSwitch`
- `MessageWithAttribute`
- `MessageUsesDefaultWhenAttributeMissing`
- `Test_Edit_Regex`
- `Test_Edit_SimpleReplace`
- `Test_Edit_Append`
- `Test_Edit_ReplaceAll`
- `FilterByType_ReturnsOnlyMatchingTypes`
- `FilterByName_ReturnsMatchingObjects`
- `GenderTest3(aposs(%#), his)`
- `GenderTest3(poss(%#), his)`
- `GenderTest3(obj(%#), him)`
- `GenderTest3(%o, him)`
- `GenderTest3(%s, he)`
- `GenderTest3(%p, his)`
- `GenderTest2(%s, she)`
- `GenderTest2(%a, hers)`
- `GenderTest2(%p, her)`
- `GenderTest2(%o, her)`
- `GenderTest2(subj(%#), she)`
- `GenderTest2(aposs(%#), hers)`
- `DigAndMoveTest`
- `GenderTest2(obj(%#), her)`
- `GenderTest3(%a, his)`
- `GenderTest2(poss(%#), her)`
- `GenderTest3(subj(%#), he)`
- `Test_Sqlescape_PreventInjection`
- `Test_Sql_WithRegister`
- `Test_Sql_SelectWithCustomSeparators`
- `Test_Sql_WhereClause`
- `Test_Sqlescape_RealWorldUse`
- `Test_Sql_SelectWithCustomRowSeparator`
- `Test_Sql_SelectWithCustomFieldSeparator`
- `Test_Sql_SelectMultipleRows_DefaultSeparators`
- `Test_Mapsql_BasicExecution`
- `Test_Mapsql_TableDoesNotExist`
- `Test_Mapsql_BasicExecution2`
- `Test_JsonMap_MapsOverJsonElements(&Test_JsonMap_MapsOverJsonElements_2 me=%0:%1:%2, json_map(me/Test_JsonMap_MapsOverJsonElements_2,\[1\,2\,3\]), number:1:0 number:2:1 number:3:2)`
- `Test_JsonMap_MapsOverJsonElements(&Test_JsonMap_MapsOverJsonElements_1 me=%0:%1, json_map(me/Test_JsonMap_MapsOverJsonElements_1,lit("test_json_map_string")), string:"test_json_map_string")`
- `Mail_NoArgs_ReturnsCount`
- `Mail_WithMessageNumber_ReturnsContent`
- `Maillist_WithFilter_ReturnsFilteredList`
- `Mailstats_ValidPlayer_ReturnsStats`
- `Maillist_NoArgs_ReturnsMailList`
- `Mailfstats_ValidPlayer_ReturnsFullStats`
- `Maildstats_ValidPlayer_ReturnsDetailedStats`
- `Mailsubject_ValidMessage_ReturnsSubject`
- `Mailstatus_ValidMessage_ReturnsStatusFormat`
- `Mailstatus_ChecksForUrgentFlag`
- `Mailtime_ValidMessage_ReturnsTimestamp`

</details>

---

## Skipped Tests by Category

### Skip Category Summary

- **1. Not Yet Implemented**: 101 skipped
- **2. Test Infrastructure - State Pollution**: 9 skipped
- **3. Marked as Failing - Needs Investigation**: 8 skipped
- **4. Switch Parsing Issue**: 1 skipped
- **5. NotifyService Issue**: 6 skipped
- **6. Other**: 96 skipped

---

### 1. Not Yet Implemented

**Total:** 101 skipped tests

- `RestartCommand`
  - Example reason: _Not Yet Implemented_
- `PurgeCommand`
- `ReadcacheCommand`
- `ShutdownCommand`
- `UnlinkExit`
- `SetParent`
- `AddcomCommand`
- `ChatCommand`
- `ChannelCommand`
- `ComtitleCommand`
- `ClistCommand`
- `ComlistCommand`
- `DelcomCommand`
- `DoingCommand`
- `RejectmotdCommand`

<details>
<summary>Additional 86 skipped tests (click to expand)</summary>

- `WizmotdCommand`
- `MotdCommand`
- `MonikerCommand`
- `IncludeCommand`
- `SelectCommand`
- `SwitchCommand`
- `BreakCommand`
- `AssertCommand`
- `RetryCommand`
- `DisableCommand`
- `EnableCommand`
- `UnrecycleCommand`
- `ListCommand`
- `DismissCommand`
- `DesertCommand`
- `UnfollowCommand`
- `FollowCommand`
- `WithCommand`
- `TeachCommand`
- `ScoreCommand`
- `BuyCommand`
- `DolistCommand`
- `LogwipeCommand`
- `MailCommand`
- `MaliasCommand`
- `SweepCommand`
- `EditCommand`
- `ConnectCommand`
- `BriefCommand`
- `WhoCommand`
- `SessionCommand`
- `QuitCommand`
- `PromptCommand`
- `NspromptCommand`
- `VerbCommand`
- `GotoCommand`
- `EnterCommand`
- `SlaveCommand`
- `SocksetCommand`
- `HttpCommand`
- `MapsqlCommand`
- `SqlCommand`
- `SuggestCommand`
- `WcheckCommand`
- `MessageCommand`
- `RespondCommand`
- `RwallCommand`
- `WarningsCommand`
- `UndestroyCommand`
- `NukeCommand`
- `DestroyCommand`
- `UseCommand`
- `SquotaCommand`
- `WhisperCommand`
- `FirstexitCommand`
- `AtrlockCommand`
- `AttributeCommand`
- `KickCommand`
- `HideCommand`
- `CommandCommand`
- `FunctionCommand`
- `HookCommand`
- `AtrchownCommand`
- `FlagCommand`
- `PowerCommand`
- `FindCommand`
- `SearchCommand`
- `EntrancesCommand`
- `WhereisCommand`
- `StatsCommand`
- `HaltCommand`
- `BootCommand`
- `AllquotaCommand`
- `QuotaCommand`
- `DumpCommand`
- `DbckCommand`
- `TriggerCommand`
- `PsWithTarget`
- `PsCommand`
- `Test_Zfun_NotImplemented(zfun(TEST_ATTR), #-1 ZONES NOT YET IMPLEMENTED)`
- `Regrep(regrep(%#,test,*), )`
- `Regrepi(regrepi(%#,test,*), )`
- `Regedit(regedit(obj/attr,pattern,replacement), )`
- `Endtag(endtag(b), </b>)`
- `Tag(tag(b,text), <b>text</b>)`
- `Name`

</details>


### 2. Test Infrastructure - State Pollution

**Total:** 9 skipped tests

- `PoorCommand`
  - Example reason: _Test infrastructure issue - state pollution from other tests_
- `ChownallCommand`
- `ChzoneallCommand`
- `NewpasswordCommand`
- `PasswordCommand`
- `PcreateCommand`
- `LinkExit`
- `LockObject`
- `UnlockObject`

### 3. Marked as Failing - Needs Investigation

**Total:** 8 skipped tests

- `Test_MoveAttribute_Basic`
  - Example reason: _Failing Test - Needs Investigation_
- `Test_AtrLock_LockAndUnlock`
- `Test_WipeAttributes_AllAttributes`
- `Test_CopyAttribute_Basic`
- `Test_CopyAttribute_Direct`
- `Test_CopyAttribute_MultipleDestinations`
- `NameObject`
- `ComListEmpty(comlist)`

### 4. Switch Parsing Issue

**Total:** 1 skipped tests

- `List_Flags_Lowercase_DisplaysLowercaseFlagList`
  - Example reason: _Switch parsing issue with multiple switches - needs investigation_

### 5. NotifyService Issue

**Total:** 6 skipped tests

- `CloneObject`
  - Example reason: _Test infrastructure issue - NotifyService call count mismatch_
- `Halt_ClearsQueue`
- `SayCommand`
- `PoseCommand`
- `SemiposeCommand`
- `PageCommand`

### 6. Other

**Total:** 96 skipped tests

- `ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully`
  - Example reason: _Skip_
- `ImportFromConfigFileAsync_ValidConfig_ShouldNotThrow`
- `GetOptions_ShouldReturnConfiguration`
- `TestSingle(think Command1 Arg;think Command2 Arg, Command1 Arg, Command2 Arg)`
- `TestSingle(think [ansi(hr,red)];think [ansi(hg,green)], ␛[1;31mred␛[0m, ␛[1;32mgreen␛[0m)`
- `TestSingle(think [add(1,2)]6;think add(3,2)7, 36, 57)`
- `TestSingle(think add(1,2)4;think add(2,3)5, 34, 55)`
- `TestSingle(think Command3 Arg;think Command4 Arg·;, Command3 Arg, Command4 Arg·)`
- `LemitBasic(@lemit Test local emit, Test local emit)`
- `RemitBasic(@remit #0=Test remote emit, Test remote emit)`
- `OemitBasic(@oemit #1=Test omit emit, Test omit emit)`
- `ZemitBasic(@zemit Test zone emit, Test zone emit)`
- `NsemitBasic(@nsemit Test nospoof emit)`
- `NslemitBasic(@nslemit Test nospoof local)`
- `ComTitleBasic(comtitle test_alias_COMTITLE=test_title_COMTITLE)`

<details>
<summary>Additional 81 skipped tests (click to expand)</summary>

- `NsoemitBasic(@nsoemit #1=Test nospoof omit)`
- `NszemitBasic(@nszemit Test nospoof zone)`
- `AddComInvalidArgs(addcom=Public, Alias name cannot be empty·)`
- `AddComInvalidArgs(addcom test_alias_ADDCOM3=NonExistentChannel, Channel not found·)`
- `NspemitBasic(@nspemit #1=Test nospoof pemit)`
- `NsremitBasic(@nsremit #0=Test nospoof remote)`
- `ConfigCommand_CategoryArg_ShowsCategoryOptions`
- `ConfigCommand_NoArgs_ListsCategories`
- `AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug`
- `AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug`
- `Power_Delete_HandlesNonExistentPower`
- `Flag_Disable_PreventsSystemFlagDisable`
- `Flag_Delete_HandlesNonExistentFlag`
- `Power_Disable_PreventsSystemPowerDisable`
- `Power_Enable_EnablesDisabledPower`
- `Function_ShowsFunctionInfo`
- `Restart_ValidObject_Restarts`
- `CommandAliasRuns(l)`
- `Trigger_QueuesAttribute`
- `PS_ShowsQueueStatus`
- `Attribute_DisplaysAttributeInfo`
- `Command_ShowsCommandInfo`
- `Function_ListsGlobalFunctions`
- `ConnectGuest_MaxGuestsReached_FailsWithError`
- `ConnectGuest_GuestsDisabled_FailsWithError`
- `MessageOemitSwitch`
- `MessageRemitSwitch`
- `SetAndResetCacheTest`
- `VerbWithDefaultMessages`
- `VerbWithAttributes`
- `VerbWithStackArguments`
- `VerbPermissionDenied`
- `VerbInsufficientArgs`
- `VerbExecutesAwhat`
- `WCheckCommand_WithAll_RequiresWizard`
- `WCheckCommand_WithMe_ChecksOwnedObjects`
- `Hide_AlreadyVisible_ShowsAppropriateMessage`
- `Hide_OffSwitch_UnsetsHidden`
- `Hide_NoSwitch_TogglesHidden`
- `ZMRUserDefinedCommandTest`
- `PersonalZoneUserDefinedCommandTest`
- `OverwritePartialNullAndGetExpandedData`
- `FilterByOwner_ReturnsOnlyOwnedObjects`
- `ClearMotdData`
- `CanIndex(ATTR TREES)`
- `CanIndex(ATTRIB TREES)`
- `CanIndex(ATTRIBUTE TREES)`
- `CanIndex(`)`
- `CanIndex(NSCEMIT())`
- `CanIndex(@CEMIT)`
- `CanIndex(@NSCEMIT)`
- `CanIndex(CEMIT())`
- `Indexable(sharpattr·md, ATTRIBUTE TREES, ATTR TREES, ATTRIB TREES, `)`
- `Indexable(sharpchat·md, @CEMIT, @NSCEMIT, CEMIT(), NSCEMIT())`
- `Valid_AttributeValue(valid(attrvalue,test_value,NONEXISTENT_ATTR), 1)`
- `Valid_AttributeValue(valid(attrvalue,test_value), 1)`
- `Test_Pgrep_IncludesParents([attrib_set(%!/PGREP_CHILD,child_value)][pgrep(%!,PGREP_*,child)], PGREP_CHILD)`
- `Test_Pgrep_IncludesParents([attrib_set([parent(me,create(PGREP_PARENT))]/PGREP_PARENT,child_value)][pgrep(%!,PGREP_*,child)], PGREP_PARENT)`
- `Test_Reglattrp_IncludesParents([attrib_set(%!/Test_Reglattrp_IncludesParents_001,value1)][attrib_set([parent(me,create(Test_Reglattrp_IncludesParents))]/Test_Reglattrp_IncludesParents_002,value2)][attrib_set(%!/Test_Reglattrp_IncludesParents_100,value3)][reglattrp(%!,Test_Reglattrp_IncludesParents_[0-9]+)], Test_Reglattrp_IncludesParents_001 Test_Reglattrp_IncludesParents_002 Test_Reglattrp_IncludesParents_100)`
- `Test_Regnattrp_CountWithParents([attrib_set(%!/Test_Regnattrp_CountWithParents_001,value1)][attrib_set([parent(me,create(Test_Regnattrp_CountWithParents))]/Test_Regnattrp_CountWithParents_002,value2)][attrib_set(%!/Test_Regnattrp_CountWithParents_100,value3)][regnattrp(%!,Test_Regnattrp_CountWithParents_[0-9]+)], 3)`
- `Test_Regxattrp_RangeWithParents([attrib_set(%!/Test_Regxattrp_RangeWithParents_001,value1)][attrib_set(%!/Test_Regxattrp_RangeWithParents_002,value2)][attrib_set(%!/Test_Regxattrp_RangeWithParents_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents_[0-9]+,1,2)], TEST_REGXATTRP_RANGEWITHPARENTS_001 TEST_REGXATTRP_RANGEWITHPARENTS_002)`
- `Test_Regxattrp_RangeWithParents([attrib_set(%!/Test_Regxattrp_RangeWithParents2_001,value1)][attrib_set([parent(me,create(Test_Regxattrp_RangeWithParents2))]/Test_Regxattrp_RangeWithParents2_002,value2)][attrib_set(%!/Test_Regxattrp_RangeWithParents2_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents2_[0-9]+,1,2)], TEST_REGXATTRP_RANGEWITHPARENTS2_001 TEST_REGXATTRP_RANGEWITHPARENTS2_002)`
- `Cstatus_WithNonMember_ReturnsOff`
- `Zemit(zemit(#0,test message), )`
- `Idle`
- `Hidden(hidden(%#), 0)`
- `JsonMap(json_map(#lambda/toupper\(%%1\,%%2\),json(object,a,1,b,2)), A:1 B:2)`
- `Test_Oob_SendsGmcpMessages(oob(me,Package·Name), 1)`
- `Test_Oob_SendsGmcpMessages(oob(me,Package·Name,{"key":"test_oob_case1"}), 1)`
- `ListInsert(linsert(a b c,2,x), a x b c)`
- `Splice(splice(a b c,d e f, ), a d  b e  c f)`
- `Elist(elist(a b c), a, b, and c)`
- `Sort(sort(foo bar baz), bar baz foo)`
- `Sort(sort(3 1 2), 1 2 3)`
- `FilterBool(filterbool(#lambda/\%0,1 0 1), 1 1)`
- `Itext(itext(0), #-1 REGISTER OUT OF RANGE)`
- `Matchall(matchall(foo bar baz,ba*), 2 3)`
- `Itemize(itemize(a b c), a, b, and c)`
- `Mix(mix(test/concat,a b c,1 2 3), a 1 b 2 c 3)`
- `Mail_InvalidMessage_ReturnsError(mail(999), #-1 NO SUCH MAIL)`
- `Mailsubject_InvalidMessage_ReturnsError(mailsubject(999), #-1 NO SUCH MAIL)`

</details>


---

## Complete Alphabetical Lists

### All Failed Tests

<details>
<summary>Click to expand complete list of failed tests</summary>

1. `AddComBasic(addcom test_alias_ADDCOM1=Public)` (157ms)
2. `AddComBasic(addcom test_alias_ADDCOM2=Public)` (144ms)
3. `AhelpCommandWorks` (149ms)
4. `AhelpNonExistentTopic` (157ms)
5. `AhelpWithTopicWorks` (7s 821ms)
6. `AllhaltCommand` (455ms)
7. `AnewsAliasWorks` (230ms)
8. `Atrlock(atrlock(%#,testattr), )` (4s 223ms)
9. `Attribute_AccessCreatesAttributeEntry` (159ms)
10. `Attribute_EntryFlagsAreAppliedWhenAttributeCreated` (253ms)
11. `CListBasic(@clist)` (121ms)
12. `CListBasic(@clist/full)` (205ms)
13. `CemitCommand` (3s 149ms)
14. `Cemit_WithNonExistentChannel_ReturnsError` (2s 344ms)
15. `ChownallCommand` (226ms)
16. `ChzoneClearZone` (278ms)
17. `ChzoneInvalidObject` (178ms)
18. `ChzoneInvalidZone` (5s 122ms)
19. `ChzoneObject` (173ms)
20. `ChzonePermissionSuccess` (272ms)
21. `ChzoneSetZone` (218ms)
22. `ComListBasic(comlist)` (182ms)
23. `ComTitleNotFound(comtitle nonexistent_alias_COMTITLE=title)` (5s 088ms)
24. `ConfigCommand_InvalidOption_ReturnsNotFound` (202ms)
25. `ConfigCommand_OptionArg_ShowsOptionValue` (136ms)
26. `Conn` (5s 085ms)
27. `ConnectGuest_BasicLogin_Succeeds` (2s 880ms)
28. `ConnectGuest_CaseInsensitive_Succeeds` (3s 651ms)
29. `ConnectGuest_MultipleGuests_SelectsAppropriateOne` (1s 115ms)
30. `ConnectGuest_NoGuestCharacters_FailsWithError` (209ms)
31. `CreateObject` (105ms)
32. `CreateObjectWithCost` (101ms)
33. `DebugFlag_OutputsBothQAndStackRegisters_Separately` (72ms)
34. `DebugFlag_OutputsFunctionEvaluation_WithSpecificValues` (76ms)
35. `DebugFlag_OutputsQRegisters_WhenSet` (136ms)
36. `DebugFlag_OutputsStackRegisters_WhenSet` (109ms)
37. `DebugFlag_ShowsNesting_WithIndentation` (96ms)
38. `DecompileCommand` (183ms)
39. `DelComBasic(delcom test_alias_DELCOM1)` (149ms)
40. `DelComNotFound(delcom nonexistent_alias_DELCOM)` (1s 196ms)
41. `DigAndMoveTest` (917ms)
42. `DigRoom` (402ms)
43. `DigRoomWithExits` (5s 102ms)
44. `Disable_BooleanOption_ShowsImplementationMessage` (117ms)
45. `Disable_InvalidOption_ReturnsNotFound` (77ms)
46. `Disable_NoArguments_ShowsUsage` (220ms)
47. `Disable_NonBooleanOption_ReturnsInvalidType` (142ms)
48. `DoBreakCommandList` (5s 336ms)
49. `DoBreakCommandList2` (459ms)
50. `DoBreakSimpleCommandList` (340ms)
51. `DoBreakSimpleFalsyCommandList` (279ms)
52. `DoBreakSimpleTruthyCommandList` (240ms)
53. `DoDigForCommandListCheck` (1s 060ms)
54. `DoDigForCommandListCheck2` (2s 232ms)
55. `DoFlagSet` (212ms)
56. `DoListBatchesToOtherPlayers` (108ms)
57. `DoListComplex` (202ms)
58. `DoListComplex2` (125ms)
59. `DoListComplex3` (136ms)
60. `DoListComplex4` (105ms)
61. `DoListComplex5` (141ms)
62. `DoListComplex6` (5s 201ms)
63. `DoListSimple` (5s 192ms)
64. `DoListSimple2` (155ms)
65. `DoListWithBreakAfterFirst_OnlyFirstMessageReceived` (186ms)
66. `DoListWithBreakFlushesMessages` (5s 121ms)
67. `DoListWithDBRefNotificationBatching` (197ms)
68. `DoListWithDelimiter` (165ms)
69. `DoListWithoutBreak_AllMessagesReceived` (296ms)
70. `DoingPollCommand` (106ms)
71. `DoingPollCommand_WithPattern` (145ms)
72. `DolistInline_ShouldExecuteImmediately` (223ms)
73. `ELOCK_CommandExecutes` (223ms)
74. `EUNLOCK_CommandExecutes` (5s 306ms)
75. `Enable_BooleanOption_ShowsImplementationMessage` (59ms)
76. `Enable_InvalidOption_ReturnsNotFound` (165ms)
77. `Enable_NoArguments_ShowsUsage` (140ms)
78. `Enable_NonBooleanOption_ReturnsInvalidType` (155ms)
79. `Entrances_ShowsLinkedObjects` (78ms)
80. `ExamineCurrentLocation` (222ms)
81. `ExamineObject` (256ms)
82. `ExamineObjectBriefSwitch` (228ms)
83. `ExamineObjectOpaqueSwitch` (223ms)
84. `ExamineWithAttributePattern` (312ms)
85. `Filter(filter(test/is_odd,1 2 3 4 5 6), 1 3 5)` (1s 037ms)
86. `FilterByCombinedCriteria_ReturnsMatchingObjects` (326ms)
87. `FilterByDbRefRange_ReturnsObjectsInRange` (450ms)
88. `FilterByName_ReturnsMatchingObjects` (5s 508ms)
89. `FilterByType_ReturnsOnlyMatchingTypes` (1s 231ms)
90. `FilterByZone_ReturnsZonedObjects` (301ms)
91. `Find_SearchesForObjects` (208ms)
92. `Flag_Add_CreatesNewFlag` (146ms)
93. `Flag_Add_PreventsDuplicateFlags` (103ms)
94. `Flag_Add_PreventsSystemFlagCreation` (113ms)
95. `Flag_Add_RequiresBothArguments` (54ms)
96. `Flag_Delete_PreventsSystemFlagDeletion` (209ms)
97. `Flag_Delete_RemovesNonSystemFlag` (172ms)
98. `Flag_Disable_DisablesNonSystemFlag` (143ms)
99. `Flag_Enable_EnablesDisabledFlag` (101ms)
100. `Flag_List_DisplaysAllFlags` (5s 135ms)
101. `Fold(fold(test/add_func,1 2 3), 6)` (1s 079ms)
102. `GenderTest2(%a, hers)` (938ms)
103. `GenderTest2(%o, her)` (1s 079ms)
104. `GenderTest2(%p, her)` (1s 002ms)
105. `GenderTest2(%s, she)` (1s 156ms)
106. `GenderTest2(aposs(%#), hers)` (5s 075ms)
107. `GenderTest2(obj(%#), her)` (2s 454ms)
108. `GenderTest2(poss(%#), her)` (4s 187ms)
109. `GenderTest2(subj(%#), she)` (4s 735ms)
110. `GenderTest3(%a, his)` (670ms)
111. `GenderTest3(%o, him)` (942ms)
112. `GenderTest3(%p, his)` (1s 203ms)
113. `GenderTest3(%s, he)` (484ms)
114. `GenderTest3(aposs(%#), his)` (2s 996ms)
115. `GenderTest3(obj(%#), him)` (2s 786ms)
116. `GenderTest3(poss(%#), his)` (2s 798ms)
117. `GenderTest3(subj(%#), he)` (1s 798ms)
118. `GetAttributeWithInheritance_CheckParentFalse_OnlyChecksObject` (380ms)
119. `GetAttributeWithInheritance_ComplexHierarchy_CorrectPrecedence` (1s 050ms)
120. `GetAttributeWithInheritance_DirectAttribute_ReturnsFromSelf` (331ms)
121. `GetAttributeWithInheritance_NestedAttributes_WorksCorrectly` (289ms)
122. `GetAttributeWithInheritance_NonExistentAttribute_ReturnsNull` (221ms)
123. `GetAttributeWithInheritance_ParentAttribute_ReturnsFromParent` (349ms)
124. `GetAttributeWithInheritance_ParentTakesPrecedenceOverZone` (1s 090ms)
125. `GetAttributeWithInheritance_ZoneAttribute_ReturnsFromZone` (194ms)
126. `GetCommand` (5s 958ms)
127. `GrepCommand` (126ms)
128. `GrepCommand_WithAttributePattern` (145ms)
129. `GrepCommand_WithNocaseSwitch` (166ms)
130. `GrepCommand_WithPrintSwitch` (215ms)
131. `GrepCommand_WithRegexpSwitch` (249ms)
132. `GrepCommand_WithWildSwitch` (225ms)
133. `HelpCommandWorks` (5s 080ms)
134. `HelpNonExistentTopic` (5s 202ms)
135. `HelpSearchWorks` (210ms)
136. `HelpWithTopicWorks` (766ms)
137. `HelpWithWildcardWorks` (172ms)
138. `Hide_AlreadyHidden_ShowsAppropriateMessage` (300ms)
139. `Hide_NoSwitch_UnsetsHidden` (5s 393ms)
140. `Hide_OnSwitch_SetsHidden` (247ms)
141. `Hide_YesSwitch_SetsHidden` (181ms)
142. `IfElse(@ifelse 0=@pemit #1=2 True,@pemit #1=2 False, 2 False)` (246ms)
143. `IfElse(@ifelse 0={@pemit #1=5 True},{@pemit #1=5 False}, 5 False)` (191ms)
144. `IfElse(@ifelse 1=@pemit #1=1 True,@pemit #1=1 False, 1 True)` (1s 370ms)
145. `IfElse(@ifelse 1=@pemit #1=3 True, 3 True)` (252ms)
146. `IfElse(@ifelse 1={@pemit #1=4 True},{@pemit #1=4 False}, 4 True)` (242ms)
147. `IfElse(@ifelse 1={@pemit #1=6 True}, 6 True)` (1s 133ms)
148. `IfElseCommand` (94ms)
149. `Include_InsertsAttributeInPlace` (121ms)
150. `ListWho` (3s 316ms)
151. `List_Attribs_DisplaysStandardAttributes` (78ms)
152. `List_Commands_DisplaysCommandList` (5s 130ms)
153. `List_Flags_DisplaysFlagList` (5s 171ms)
154. `List_Functions_DisplaysFunctionList` (1s 154ms)
155. `List_Locks_DisplaysLockTypes` (5s 134ms)
156. `List_Motd_DisplaysMotdSettings` (94ms)
157. `List_NoSwitch_DisplaysHelpMessage` (147ms)
158. `List_Powers_DisplaysPowerList` (5s 140ms)
159. `ListmotdCommand` (65ms)
160. `LogCommand_DefaultSwitch_LogsToCommandCategory` (314ms)
161. `LogCommand_NoMessage_ReturnsError` (156ms)
162. `LogCommand_RecallSwitch_RetrievesLogs` (1s 188ms)
163. `LogCommand_WithCmdSwitch_LogsToCommandCategory` (164ms)
164. `LogCommand_WithErrSwitch_LogsToErrorCategory` (253ms)
165. `LogCommand_WithWizSwitch_LogsToWizardCategory` (158ms)
166. `Lwhoid(lwhoid(), )` (4s 377ms)
167. `Mail_NoArgs_ReturnsCount` (2s 185ms)
168. `Mail_WithMessageNumber_ReturnsContent` (3s 786ms)
169. `Maildstats_ValidPlayer_ReturnsDetailedStats` (6s 198ms)
170. `Mailfstats_ValidPlayer_ReturnsFullStats` (7s 365ms)
171. `Maillist_NoArgs_ReturnsMailList` (4s 556ms)
172. `Maillist_WithFilter_ReturnsFilteredList` (4s 114ms)
173. `Mailstats_ValidPlayer_ReturnsStats` (6s 599ms)
174. `Mailstatus_ChecksForUrgentFlag` (4s 092ms)
175. `Mailstatus_ValidMessage_ReturnsStatusFormat` (3s 986ms)
176. `Mailsubject_ValidMessage_ReturnsSubject` (3s 459ms)
177. `Mailtime_ValidMessage_ReturnsTimestamp` (3s 557ms)
178. `Map_ExecutesAttributeOverList` (251ms)
179. `MessageBasic` (135ms)
180. `MessageNoisySwitch` (212ms)
181. `MessageNospoofSwitch` (250ms)
182. `MessageSilentSwitch` (204ms)
183. `MessageUsesDefaultWhenAttributeMissing` (169ms)
184. `MessageWithAttribute` (201ms)
185. `MetricsCommand_Connections_ReturnsConnectionMetrics` (163ms)
186. `MetricsCommand_Health_ReturnsHealthStatus` (5s 220ms)
187. `MetricsCommand_NoSwitch_ShowsUsage` (222ms)
188. `MetricsCommand_PopularFunctions_ReturnsOnlyFunctions` (215ms)
189. `MetricsCommand_Popular_ReturnsMostCalledOperations(1h)` (201ms)
190. `MetricsCommand_Popular_ReturnsMostCalledOperations(5m)` (129ms)
191. `MetricsCommand_Query_ExecutesCustomPromQL` (212ms)
192. `MetricsCommand_SlowestCommands_ReturnsOnlyCommands` (225ms)
193. `MetricsCommand_SlowestFunctions_ReturnsOnlyFunctions` (194ms)
194. `MetricsCommand_Slowest_ReturnsSlowOperations(1h)` (172ms)
195. `MetricsCommand_Slowest_ReturnsSlowOperations(24h)` (200ms)
196. `MetricsCommand_Slowest_ReturnsSlowOperations(5m)` (117ms)
197. `MetricsCommand_WithLimit_RespectsLimit` (290ms)
198. `Money(money(%#), #-1 NOT SUPPORTED)` (2s 452ms)
199. `MultipleObjectsSameZone` (517ms)
200. `Munge(munge(test/sort,b a c,2 1 3), 1 2 3)` (1s 606ms)
201. `NestedDoListBatching` (167ms)
202. `NestedDoListWithBreakFlushesMessages` (200ms)
203. `NewsCommandWorks` (173ms)
204. `NewsNonExistentTopic` (218ms)
205. `NewsWithTopicWorks` (226ms)
206. `NewsWithWildcardWorks` (1s 212ms)
207. `NotifyCommand_ShouldWakeWaitingTask` (2s 391ms)
208. `NotifySetQ_ShouldSetQRegisterForWaitingTask` (2s 365ms)
209. `NscemitCommand` (1s 148ms)
210. `Nscemit_WithNonExistentChannel_ReturnsError` (1s 938ms)
211. `Nsoemit(nsoemit(#1,test), )` (4s 311ms)
212. `Nspemit` (4s 634ms)
213. `Nsprompt(nsprompt(#1,test), )` (4s 829ms)
214. `Nsremit(nsremit(#0,test), )` (3s 949ms)
215. `ObjectCanBeZone` (311ms)
216. `Oemit(oemit(#1,test message), )` (3s 671ms)
217. `ParentCycleDetection_DirectCycle` (1s 086ms)
218. `ParentCycleDetection_IndirectCycle` (95ms)
219. `ParentCycleDetection_LongChain` (1s 055ms)
220. `ParentCycleDetection_SelfParent` (60ms)
221. `ParentSetAndGet` (134ms)
222. `ParentUnset` (188ms)
223. `PemitBasic(@pemit #1=Another test, Another test)` (132ms)
224. `PemitBasic(@pemit #1=Test message, Test message)` (85ms)
225. `PemitPort` (1s 881ms)
226. `PollClearCommand` (264ms)
227. `PollCommand` (313ms)
228. `PollSetCommand` (312ms)
229. `Power_Add_CreatesNewPower` (165ms)
230. `Power_Add_PreventsSystemPowerCreation` (204ms)
231. `Power_Add_RequiresBothArguments` (182ms)
232. `Power_Delete_RemovesNonSystemPower` (183ms)
233. `Power_Disable_DisablesNonSystemPower` (175ms)
234. `Power_List_DisplaysAllPowers` (153ms)
235. `PrivateEmit` (4s 292ms)
236. `PurgeCommand` (268ms)
237. `ReadCacheCommand` (322ms)
238. `RecycleObject` (139ms)
239. `Remit(remit(#0,test message), )` (3s 370ms)
240. `Search_PerformsDatabaseSearch` (171ms)
241. `Select_MatchesFirstExpression` (170ms)
242. `SetAndGet([attrib_set(%!/attribute,ZAP!)][get(%!/attribute)], ZAP!)` (5s 187ms)
243. `SetAndGet([attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)], ␛[1;31mZAP!␛[0m)` (7s 219ms)
244. `SetAndGet([attrib_set(%!/attribute,ansi(hr,ZIP!))][get(%!/attribute)][attrib_set(%!/attribute,ansi(hr,ZAP!))][get(%!/attribute)], ␛[1;31mZIP!␛[0m␛[1;31mZAP!␛[0m)` (5s 523ms)
245. `SetFlag` (1s 056ms)
246. `SetObjectZone` (348ms)
247. `SetObjectZoneToNull` (395ms)
248. `ShutdownCommand` (465ms)
249. `ShutdownRebootCommand` (772ms)
250. `SimpleCommandParse(@pemit #1=1 This is a test, 1 This is a test)` (212ms)
251. `SimpleCommandParse(@pemit #1=2 This is a test;, 2 This is a test;)` (126ms)
252. `SitelockCommand` (132ms)
253. `SkipCommand` (143ms)
254. `SortBy(sortby(test/comp,c a b), a b c)` (833ms)
255. `SortKey(sortkey(test/key,abc ab a), a ab abc)` (753ms)
256. `Stats_ShowsDatabaseStatistics` (285ms)
257. `Step(step(test/first,a b c d e,2), a c e)` (785ms)
258. `SuggestAddCommand` (1s 020ms)
259. `SuggestListCommand` (1s 124ms)
260. `Table(table(a b c,10,2), )` (1s 386ms)
261. `Test(]think [add(1,2)]3, [add(1,2)]3)` (224ms)
262. `Test(think Command1 Arg;think Command2 Arg, Command1 Arg;think Command2 Arg)` (146ms)
263. `Test(think [add(1,2)]2, 32)` (258ms)
264. `Test(think add(1,2)1, 31)` (161ms)
265. `Test_AtrChown_InvalidArguments` (467ms)
266. `Test_AtrLock_QueryStatus` (249ms)
267. `Test_Basic_AttribSet_And_Get([attrib_set(%!/Test_Basic_AttribSet_And_Get,testvalue)][get(%!/Test_Basic_AttribSet_And_Get)], testvalue)` (3s 200ms)
268. `Test_Basic_AttribSet_And_Get([attrib_set(%!/Test_Basic_AttribSet_And_Get21,val1)][attrib_set(%!/Test_Basic_AttribSet_And_Get22,val2)][get(%!/Test_Basic_AttribSet_And_Get21)][get(%!/Test_Basic_AttribSet_And_Get22)], val1val2)` (4s 840ms)
269. `Test_CopyAttribute_InvalidSource` (118ms)
270. `Test_Doing_WithInvalidPlayerName` (3s 829ms)
271. `Test_Edit_Append` (1s 008ms)
272. `Test_Edit_FirstOnly` (249ms)
273. `Test_Edit_NoMatch` (70ms)
274. `Test_Edit_Prepend` (172ms)
275. `Test_Edit_Regex` (476ms)
276. `Test_Edit_ReplaceAll` (1s 276ms)
277. `Test_Edit_SimpleReplace` (616ms)
278. `Test_Grep_AttributeTrees([attrib_set(%!/Test_Grep_AttributeTrees,root)][attrib_set(%!/Test_Grep_AttributeTrees`BRANCH1,has_search_term)][attrib_set(%!/Test_Grep_AttributeTrees`BRANCH2,different)][grep(%!,Test_Grep_AttributeTrees**,search)], TEST_GREP_ATTRIBUTETREES`BRANCH1)` (2s 693ms)
279. `Test_Grep_AttributeTrees([attrib_set(%!/Test_Grep_AttributeTrees_2,test)][attrib_set(%!/Test_Grep_AttributeTrees_2`SUB1,contains_test)][attrib_set(%!/Test_Grep_AttributeTrees_2`SUB2,no_match)][grep(%!,Test_Grep_AttributeTrees_2**,test)], TEST_GREP_ATTRIBUTETREES_2 TEST_GREP_ATTRIBUTETREES_2`SUB1)` (3s 216ms)
280. `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_1,has_test_in_value)][attrib_set(%!/Test_Grep_CaseSensitive_2,also_test_here)][attrib_set(%!/Test_Grep_CaseSensitive_2_EMPTY_TEST,)][grep(%!,*Test_Grep_CaseSensitive_*,test)], TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2)` (3s 490ms)
281. `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_1,test_string_grep_case1)][attrib_set(%!/Test_Grep_CaseSensitive_2,another_test_value)][attrib_set(%!/NO_MATCH,different)][grep(%!,Test_Grep_CaseSensitive_*,test)], TEST_GREP_CASESENSITIVE_1 TEST_GREP_CASESENSITIVE_2)` (6s 275ms)
282. `Test_Grep_CaseSensitive([attrib_set(%!/Test_Grep_CaseSensitive_UPPER,TEST_VALUE)][grep(%!,Test_Grep_CaseSensitive_*,VALUE)], TEST_GREP_CASESENSITIVE_UPPER)` (4s 490ms)
283. `Test_Grep_InAttributeTree` (4s 958ms)
284. `Test_Grepi_CaseInsensitive([attrib_set(%!/Test_Grepi_CaseInsensitive1_1,has_VALUE)][attrib_set(%!/Test_Grepi_CaseInsensitive1_2,also_VALUE)][attrib_set(%!/Test_Grepi_CaseInsensitive1_UPPER,more_VALUE)][grepi(%!,Test_Grepi_CaseInsensitive1_*,VALUE)], TEST_GREPI_CASEINSENSITIVE1_1 TEST_GREPI_CASEINSENSITIVE1_2 TEST_GREPI_CASEINSENSITIVE1_UPPER)` (2s 671ms)
285. `Test_Grepi_CaseInsensitive([attrib_set(%!/Test_Grepi_CaseInsensitive2_1,has_TEST)][attrib_set(%!/Test_Grepi_CaseInsensitive2_2,also_TEST)][attrib_set(%!/Test_Grepi_CaseInsensitive2_UPPER,more_TEST)][grepi(%!,Test_Grepi_CaseInsensitive2_*,TEST)], TEST_GREPI_CASEINSENSITIVE2_1 TEST_GREPI_CASEINSENSITIVE2_2 TEST_GREPI_CASEINSENSITIVE2_UPPER)` (6s 934ms)
286. `Test_JsonMap_MapsOverJsonElements(&Test_JsonMap_MapsOverJsonElements_1 me=%0:%1, json_map(me/Test_JsonMap_MapsOverJsonElements_1,lit("test_json_map_string")), string:"test_json_map_string")` (4s 270ms)
287. `Test_JsonMap_MapsOverJsonElements(&Test_JsonMap_MapsOverJsonElements_2 me=%0:%1:%2, json_map(me/Test_JsonMap_MapsOverJsonElements_2,\[1\,2\,3\]), number:1:0 number:2:1 number:3:2)` (3s 905ms)
288. `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees,root)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1,leaf1)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH2,leaf2)][attrib_set(%!/Test_Lattr_AttributeTrees`BRANCH1`SUBLEAF,deep)][lattr(%!/Test_Lattr_AttributeTrees`**)], TEST_LATTR_ATTRIBUTETREES`BRANCH1 TEST_LATTR_ATTRIBUTETREES`BRANCH1`SUBLEAF TEST_LATTR_ATTRIBUTETREES`BRANCH2)` (7s 159ms)
289. `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees2,value)][attrib_set(%!/Test_Lattr_AttributeTrees2`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees2**)], TEST_LATTR_ATTRIBUTETREES2 TEST_LATTR_ATTRIBUTETREES2`CHILD)` (5s 487ms)
290. `Test_Lattr_AttributeTrees([attrib_set(%!/Test_Lattr_AttributeTrees3,value)][attrib_set(%!/Test_Lattr_AttributeTrees3`CHILD,childval)][lattr(%!/Test_Lattr_AttributeTrees3*)], TEST_LATTR_ATTRIBUTETREES3)` (7s 370ms)
291. `Test_Lattr_Simple([attrib_set(%!/Test_Lattr_Simple1,v1)][attrib_set(%!/Test_Lattr_Simple2,v2)][lattr(%!/Test_Lattr_Simple*)], TEST_LATTR_SIMPLE1 TEST_LATTR_SIMPLE2)` (4s 615ms)
292. `Test_MapSql_Basic` (168ms)
293. `Test_MapSql_InvalidObjectAttribute` (156ms)
294. `Test_MapSql_WithColnamesSwitch` (250ms)
295. `Test_MapSql_WithMultipleRows` (147ms)
296. `Test_Mapsql_BasicExecution` (6s 058ms)
297. `Test_Mapsql_BasicExecution2` (4s 403ms)
298. `Test_Mapsql_TableDoesNotExist` (4s 468ms)
299. `Test_MoveAttribute_InvalidSource` (190ms)
300. `Test_Nattr_Counting` (4s 820ms)
301. `Test_Reglattr_AttributeTrees([attrib_set(%!/Test_Reglattr_AttributeTrees1,val)][attrib_set(%!/Test_Reglattr_AttributeTrees1`A,val1)][attrib_set(%!/Test_Reglattr_AttributeTrees1`B,val2)][attrib_set(%!/Test_Reglattr_AttributeTrees1`A`DEEP,val3)][reglattr(%!/^Test_Reglattr_AttributeTrees1)], TEST_REGLATTR_ATTRIBUTETREES1 TEST_REGLATTR_ATTRIBUTETREES1`A TEST_REGLATTR_ATTRIBUTETREES1`A`DEEP TEST_REGLATTR_ATTRIBUTETREES1`B)` (3s 227ms)
302. `Test_Reglattr_AttributeTrees([attrib_set(%!/Test_Reglattr_AttributeTrees2_001,v1)][attrib_set(%!/Test_Reglattr_AttributeTrees2_001`SUB,v2)][reglattr(%!/Test_Reglattr_AttributeTrees2_\[0-9\]+)], TEST_REGLATTR_ATTRIBUTETREES2_001 TEST_REGLATTR_ATTRIBUTETREES2_001`SUB)` (3s 237ms)
303. `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_001,value1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_002,value2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX1_100,value3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX1_00\[0-9\]$)], TESTREGLATTR_UNIQUE_RGX1_001 TESTREGLATTR_UNIQUE_RGX1_002)` (6s 084ms)
304. `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_001,value1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_002,value2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX2_100,value3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX2_\[0-9\]+$)], TESTREGLATTR_UNIQUE_RGX2_001 TESTREGLATTR_UNIQUE_RGX2_002 TESTREGLATTR_UNIQUE_RGX2_100)` (4s 944ms)
305. `Test_Reglattr_RegexPattern([attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_A,val1)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_B,val2)][attrib_set(%!/TESTREGLATTR_UNIQUE_RGX3_UPPER,val3)][reglattr(%!/^TESTREGLATTR_UNIQUE_RGX3_\[A-Z\]+$)], TESTREGLATTR_UNIQUE_RGX3_A TESTREGLATTR_UNIQUE_RGX3_B TESTREGLATTR_UNIQUE_RGX3_UPPER)` (3s 425ms)
306. `Test_Reglattr_WithRegex` (2s 993ms)
307. `Test_Regnattr_AttributeTrees([attrib_set(%!/Test_Regnattr_AttributeTrees1,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L1,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L2,v)][attrib_set(%!/Test_Regnattr_AttributeTrees1`L1`L2,v)][regnattr(%!/^Test_Regnattr_AttributeTrees1)], 4)` (6s 768ms)
308. `Test_Regnattr_AttributeTrees([attrib_set(%!/Test_Regnattr_AttributeTrees2,v)][attrib_set(%!/Test_Regnattr_AttributeTrees2`A,v)][attrib_set(%!/Test_Regnattr_AttributeTrees2`B,v)][regnattr(%!/^Test_Regnattr_AttributeTrees2)], 3)` (3s 593ms)
309. `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_001,value1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_002,value2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT1_100,value3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT1_\[0-9\]+$)], 3)` (2s 701ms)
310. `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_A,val1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_B,val2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT2_UPPER,val3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT2_\[A-Z\]+$)], 3)` (2s 850ms)
311. `Test_Regnattr_Count([attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_X,val1)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Y,val2)][attrib_set(%!/TESTREGNATTR_UNIQUE_CNT3_Z,val3)][regnattr(%!/^TESTREGNATTR_UNIQUE_CNT3_\[XYZ\]$)], 3)` (2s 702ms)
312. `Test_Regxattr_AttributeTrees([attrib_set(%!/Test_Regxattr_AttributeTrees,v1)][attrib_set(%!/Test_Regxattr_AttributeTrees`A,v2)][attrib_set(%!/Test_Regxattr_AttributeTrees`B,v3)][attrib_set(%!/Test_Regxattr_AttributeTrees`C,v4)][regxattr(%!/^Test_Regxattr_AttributeTrees,2,2)], TEST_REGXATTR_ATTRIBUTETREES`A TEST_REGXATTR_ATTRIBUTETREES`B)` (5s 509ms)
313. `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex1_001,value1)][attrib_set(%!/Test_Regxattr_RangeWithRegex1_002,value2)][attrib_set(%!/Test_Regxattr_RangeWithRegex1_100,value3)][regxattr(%!/Test_Regxattr_RangeWithRegex1_\[0-9\]+,1,2)], TEST_REGXATTR_RANGEWITHREGEX1_001 TEST_REGXATTR_RANGEWITHREGEX1_002)` (4s 730ms)
314. `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex2_001,value1)][attrib_set(%!/Test_Regxattr_RangeWithRegex2_002,value2)][attrib_set(%!/Test_Regxattr_RangeWithRegex2_100,value3)][regxattr(%!/Test_Regxattr_RangeWithRegex2_\[0-9\]+,2,2)], TEST_REGXATTR_RANGEWITHREGEX2_002 TEST_REGXATTR_RANGEWITHREGEX2_100)` (4s 579ms)
315. `Test_Regxattr_RangeWithRegex([attrib_set(%!/Test_Regxattr_RangeWithRegex3_1,val1)][attrib_set(%!/Test_Regxattr_RangeWithRegex3_2,val2)][regxattr(%!/^Test_Regxattr_RangeWithRegex3_,1,2)], TEST_REGXATTR_RANGEWITHREGEX3_1 TEST_REGXATTR_RANGEWITHREGEX3_2)` (5s 502ms)
316. `Test_Respond_Header_ContentLength_Forbidden` (1s 165ms)
317. `Test_Respond_Header_CustomHeader` (294ms)
318. `Test_Respond_Header_EmptyName` (276ms)
319. `Test_Respond_Header_SetCookie` (120ms)
320. `Test_Respond_Header_WithoutEquals` (150ms)
321. `Test_Respond_InvalidStatusCode` (125ms)
322. `Test_Respond_StatusCode` (140ms)
323. `Test_Respond_StatusCode_404` (253ms)
324. `Test_Respond_StatusCode_OutOfRange` (299ms)
325. `Test_Respond_StatusCode_WithoutText` (185ms)
326. `Test_Respond_StatusLine_TooLong` (226ms)
327. `Test_Respond_Type_ApplicationJson` (162ms)
328. `Test_Respond_Type_Empty` (224ms)
329. `Test_Respond_Type_TextHtml` (291ms)
330. `Test_SpecialCharacters_Escaped` (3s 504ms)
331. `Test_Sql_Count` (128ms)
332. `Test_Sql_InvalidQuery` (5s 119ms)
333. `Test_Sql_NoResults` (28ms)
334. `Test_Sql_NoResults` (1s 703ms)
335. `Test_Sql_SelectMultipleRows` (144ms)
336. `Test_Sql_SelectMultipleRows_DefaultSeparators` (3s 161ms)
337. `Test_Sql_SelectSingleRow` (94ms)
338. `Test_Sql_SelectSingleRow` (1s 956ms)
339. `Test_Sql_SelectWithCustomFieldSeparator` (3s 321ms)
340. `Test_Sql_SelectWithCustomRowSeparator` (3s 111ms)
341. `Test_Sql_SelectWithCustomSeparators` (3s 427ms)
342. `Test_Sql_SelectWithWhere` (141ms)
343. `Test_Sql_WhereClause` (3s 120ms)
344. `Test_Sql_WithRegister` (2s 393ms)
345. `Test_Sqlescape_PreventInjection` (2s 463ms)
346. `Test_Sqlescape_RealWorldUse` (2s 074ms)
347. `Test_Wildcard_DoubleStar_MatchAll` (4s 957ms)
348. `Test_Wildcard_EntireSubtree` (3s 265ms)
349. `Test_Wildcard_Grandchildren` (4s 702ms)
350. `Test_Wildcard_ImmediateChildren` (4s 808ms)
351. `Test_Wildcard_QuestionMark` (4s 906ms)
352. `Test_Wildcard_Star_NoBacktick` (6s 044ms)
353. `Test_Wildgrep_AttributeTrees([attrib_set(%!/Test_Wildgrep_AttributeTrees,val)][attrib_set(%!/Test_Wildgrep_AttributeTrees`CHILD,has_pattern)][attrib_set(%!/Test_Wildgrep_AttributeTrees`OTHER,no_match)][wildgrep(%!,Test_Wildgrep_AttributeTrees**,*pattern*)], TEST_WILDGREP_ATTRIBUTETREES`CHILD)` (7s 743ms)
354. `Test_Wildgrep_InAttributeTree` (2s 693ms)
355. `Test_Wildgrep_Pattern([attrib_set(%!/WILDGREP_1,test_wildcard_*_match)][attrib_set(%!/WILDGREP_2,different)][wildgrep(%!,WILDGREP_*,*wildcard*)], WILDGREP_1)` (6s 130ms)
356. `Test_Wildgrep_Pattern([attrib_set(%!/WILDGREP_1,test_wildcard_value_match)][wildgrep(%!,WILDGREP_*,test_*_match)], WILDGREP_1)` (5s 512ms)
357. `Test_Wildgrepi_CaseInsensitive([attrib_set(%!/WILDGREP_1,has_WILDCARD)][attrib_set(%!/WILDGREP_UPPER,TEST_WILDCARD)][wildgrepi(%!,WILDGREP_*,*WILDCARD*)], WILDGREP_1 WILDGREP_UPPER)` (5s 647ms)
358. `Test_Xattr_RangeInTree` (4s 955ms)
359. `ThinkBasic` (277ms)
360. `ThinkWithFunction` (213ms)
361. `ULOCK_CommandExecutes` (1s 090ms)
362. `UUNLOCK_CommandExecutes` (285ms)
363. `UnsetObjectZone` (294ms)
364. `UpdateObjectZone` (438ms)
365. `VerboseFlag_OutputsCommandExecution` (85ms)
366. `WCheckCommand_NoArguments_ShowsUsage` (759ms)
367. `WCheckCommand_SpecificObject` (839ms)
368. `WarningsCommand_NoArguments_ShowsUsage` (282ms)
369. `WarningsCommand_SetToAll` (213ms)
370. `WarningsCommand_SetToNone` (260ms)
371. `WarningsCommand_SetToNormal` (279ms)
372. `WarningsCommand_WithNegation` (322ms)
373. `WarningsCommand_WithUnknownWarning` (231ms)
374. `WhereIs_NonPlayer_ReturnsError` (199ms)
375. `WhereIs_ValidPlayer_ReportsLocation` (92ms)

</details>

### All Skipped Tests

<details>
<summary>Click to expand complete list of skipped tests</summary>

1. `AddComInvalidArgs(addcom test_alias_ADDCOM3=NonExistentChannel, Channel not found·)` (0ms)
2. `AddComInvalidArgs(addcom=Public, Alias name cannot be empty·)` (0ms)
3. `AddcomCommand` (0ms)
4. `AllquotaCommand` (0ms)
5. `AssertCommand` (0ms)
6. `AtrchownCommand` (0ms)
7. `AtrlockCommand` (0ms)
8. `AttributeCommand` (0ms)
9. `AttributeDebugFlag_ForcesOutput_EvenWithoutObjectDebug` (0ms)
10. `AttributeNoDebugFlag_SuppressesOutput_EvenWithObjectDebug` (0ms)
11. `Attribute_DisplaysAttributeInfo` (0ms)
12. `BootCommand` (0ms)
13. `BreakCommand` (0ms)
14. `BriefCommand` (0ms)
15. `BuyCommand` (0ms)
16. `CanIndex(@CEMIT)` (0ms)
17. `CanIndex(@NSCEMIT)` (0ms)
18. `CanIndex(ATTR TREES)` (0ms)
19. `CanIndex(ATTRIB TREES)` (0ms)
20. `CanIndex(ATTRIBUTE TREES)` (0ms)
21. `CanIndex(CEMIT())` (0ms)
22. `CanIndex(NSCEMIT())` (0ms)
23. `CanIndex(`)` (0ms)
24. `ChannelCommand` (0ms)
25. `ChatCommand` (0ms)
26. `ChownallCommand` (0ms)
27. `ChzoneallCommand` (0ms)
28. `ClearMotdData` (0ms)
29. `ClistCommand` (0ms)
30. `CloneObject` (0ms)
31. `ComListEmpty(comlist)` (0ms)
32. `ComTitleBasic(comtitle test_alias_COMTITLE=test_title_COMTITLE)` (0ms)
33. `ComlistCommand` (0ms)
34. `CommandAliasRuns(l)` (0ms)
35. `CommandCommand` (0ms)
36. `Command_ShowsCommandInfo` (0ms)
37. `ComtitleCommand` (0ms)
38. `ConfigCommand_CategoryArg_ShowsCategoryOptions` (0ms)
39. `ConfigCommand_NoArgs_ListsCategories` (0ms)
40. `ConnectCommand` (0ms)
41. `ConnectGuest_GuestsDisabled_FailsWithError` (0ms)
42. `ConnectGuest_MaxGuestsReached_FailsWithError` (0ms)
43. `Cstatus_WithNonMember_ReturnsOff` (0ms)
44. `DbckCommand` (0ms)
45. `DelcomCommand` (0ms)
46. `DesertCommand` (0ms)
47. `DestroyCommand` (0ms)
48. `DisableCommand` (0ms)
49. `DismissCommand` (0ms)
50. `DoingCommand` (0ms)
51. `DolistCommand` (0ms)
52. `DumpCommand` (0ms)
53. `EditCommand` (0ms)
54. `Elist(elist(a b c), a, b, and c)` (0ms)
55. `EnableCommand` (0ms)
56. `Endtag(endtag(b), </b>)` (0ms)
57. `EnterCommand` (0ms)
58. `EntrancesCommand` (0ms)
59. `FilterBool(filterbool(#lambda/\%0,1 0 1), 1 1)` (0ms)
60. `FilterByOwner_ReturnsOnlyOwnedObjects` (0ms)
61. `FindCommand` (0ms)
62. `FirstexitCommand` (0ms)
63. `FlagCommand` (0ms)
64. `Flag_Delete_HandlesNonExistentFlag` (0ms)
65. `Flag_Disable_PreventsSystemFlagDisable` (0ms)
66. `FollowCommand` (0ms)
67. `FunctionCommand` (0ms)
68. `Function_ListsGlobalFunctions` (0ms)
69. `Function_ShowsFunctionInfo` (0ms)
70. `GetOptions_ShouldReturnConfiguration` (0ms)
71. `GotoCommand` (0ms)
72. `HaltCommand` (0ms)
73. `Halt_ClearsQueue` (0ms)
74. `Hidden(hidden(%#), 0)` (0ms)
75. `HideCommand` (0ms)
76. `Hide_AlreadyVisible_ShowsAppropriateMessage` (0ms)
77. `Hide_NoSwitch_TogglesHidden` (0ms)
78. `Hide_OffSwitch_UnsetsHidden` (0ms)
79. `HookCommand` (0ms)
80. `HttpCommand` (0ms)
81. `Idle` (0ms)
82. `ImportFromConfigFileAsync_HttpError_ShouldHandleGracefully` (1ms)
83. `ImportFromConfigFileAsync_ValidConfig_ShouldNotThrow` (1ms)
84. `IncludeCommand` (0ms)
85. `Indexable(sharpattr·md, ATTRIBUTE TREES, ATTR TREES, ATTRIB TREES, `)` (0ms)
86. `Indexable(sharpchat·md, @CEMIT, @NSCEMIT, CEMIT(), NSCEMIT())` (0ms)
87. `Itemize(itemize(a b c), a, b, and c)` (0ms)
88. `Itext(itext(0), #-1 REGISTER OUT OF RANGE)` (0ms)
89. `JsonMap(json_map(#lambda/toupper\(%%1\,%%2\),json(object,a,1,b,2)), A:1 B:2)` (0ms)
90. `KickCommand` (0ms)
91. `LemitBasic(@lemit Test local emit, Test local emit)` (0ms)
92. `LinkExit` (0ms)
93. `ListCommand` (0ms)
94. `ListInsert(linsert(a b c,2,x), a x b c)` (0ms)
95. `List_Flags_Lowercase_DisplaysLowercaseFlagList` (0ms)
96. `LockObject` (0ms)
97. `LogwipeCommand` (0ms)
98. `MailCommand` (0ms)
99. `Mail_InvalidMessage_ReturnsError(mail(999), #-1 NO SUCH MAIL)` (0ms)
100. `Mailsubject_InvalidMessage_ReturnsError(mailsubject(999), #-1 NO SUCH MAIL)` (0ms)
101. `MaliasCommand` (0ms)
102. `MapsqlCommand` (0ms)
103. `Matchall(matchall(foo bar baz,ba*), 2 3)` (0ms)
104. `MessageCommand` (0ms)
105. `MessageOemitSwitch` (0ms)
106. `MessageRemitSwitch` (0ms)
107. `Mix(mix(test/concat,a b c,1 2 3), a 1 b 2 c 3)` (0ms)
108. `MonikerCommand` (0ms)
109. `MotdCommand` (0ms)
110. `Name` (0ms)
111. `NameObject` (0ms)
112. `NewpasswordCommand` (0ms)
113. `NsemitBasic(@nsemit Test nospoof emit)` (0ms)
114. `NslemitBasic(@nslemit Test nospoof local)` (0ms)
115. `NsoemitBasic(@nsoemit #1=Test nospoof omit)` (0ms)
116. `NspemitBasic(@nspemit #1=Test nospoof pemit)` (0ms)
117. `NspromptCommand` (0ms)
118. `NsremitBasic(@nsremit #0=Test nospoof remote)` (0ms)
119. `NszemitBasic(@nszemit Test nospoof zone)` (0ms)
120. `NukeCommand` (0ms)
121. `OemitBasic(@oemit #1=Test omit emit, Test omit emit)` (0ms)
122. `OverwritePartialNullAndGetExpandedData` (0ms)
123. `PS_ShowsQueueStatus` (0ms)
124. `PageCommand` (0ms)
125. `PasswordCommand` (0ms)
126. `PcreateCommand` (0ms)
127. `PersonalZoneUserDefinedCommandTest` (0ms)
128. `PoorCommand` (0ms)
129. `PoseCommand` (0ms)
130. `PowerCommand` (0ms)
131. `Power_Delete_HandlesNonExistentPower` (0ms)
132. `Power_Disable_PreventsSystemPowerDisable` (0ms)
133. `Power_Enable_EnablesDisabledPower` (0ms)
134. `PromptCommand` (0ms)
135. `PsCommand` (0ms)
136. `PsWithTarget` (0ms)
137. `PurgeCommand` (0ms)
138. `QuitCommand` (0ms)
139. `QuotaCommand` (0ms)
140. `ReadcacheCommand` (0ms)
141. `Regedit(regedit(obj/attr,pattern,replacement), )` (0ms)
142. `Regrep(regrep(%#,test,*), )` (0ms)
143. `Regrepi(regrepi(%#,test,*), )` (0ms)
144. `RejectmotdCommand` (0ms)
145. `RemitBasic(@remit #0=Test remote emit, Test remote emit)` (0ms)
146. `RespondCommand` (0ms)
147. `RestartCommand` (0ms)
148. `Restart_ValidObject_Restarts` (0ms)
149. `RetryCommand` (0ms)
150. `RwallCommand` (0ms)
151. `SayCommand` (0ms)
152. `ScoreCommand` (0ms)
153. `SearchCommand` (0ms)
154. `SelectCommand` (0ms)
155. `SemiposeCommand` (0ms)
156. `SessionCommand` (0ms)
157. `SetAndResetCacheTest` (0ms)
158. `SetParent` (0ms)
159. `ShutdownCommand` (0ms)
160. `SlaveCommand` (0ms)
161. `SocksetCommand` (0ms)
162. `Sort(sort(3 1 2), 1 2 3)` (0ms)
163. `Sort(sort(foo bar baz), bar baz foo)` (0ms)
164. `Splice(splice(a b c,d e f, ), a d  b e  c f)` (0ms)
165. `SqlCommand` (0ms)
166. `SquotaCommand` (0ms)
167. `StatsCommand` (0ms)
168. `SuggestCommand` (0ms)
169. `SweepCommand` (0ms)
170. `SwitchCommand` (0ms)
171. `Tag(tag(b,text), <b>text</b>)` (0ms)
172. `TeachCommand` (0ms)
173. `TestSingle(think Command1 Arg;think Command2 Arg, Command1 Arg, Command2 Arg)` (0ms)
174. `TestSingle(think Command3 Arg;think Command4 Arg·;, Command3 Arg, Command4 Arg·)` (0ms)
175. `TestSingle(think [add(1,2)]6;think add(3,2)7, 36, 57)` (0ms)
176. `TestSingle(think [ansi(hr,red)];think [ansi(hg,green)], ␛[1;31mred␛[0m, ␛[1;32mgreen␛[0m)` (0ms)
177. `TestSingle(think add(1,2)4;think add(2,3)5, 34, 55)` (0ms)
178. `Test_AtrLock_LockAndUnlock` (0ms)
179. `Test_CopyAttribute_Basic` (0ms)
180. `Test_CopyAttribute_Direct` (0ms)
181. `Test_CopyAttribute_MultipleDestinations` (0ms)
182. `Test_MoveAttribute_Basic` (0ms)
183. `Test_Oob_SendsGmcpMessages(oob(me,Package·Name), 1)` (0ms)
184. `Test_Oob_SendsGmcpMessages(oob(me,Package·Name,{"key":"test_oob_case1"}), 1)` (0ms)
185. `Test_Pgrep_IncludesParents([attrib_set(%!/PGREP_CHILD,child_value)][pgrep(%!,PGREP_*,child)], PGREP_CHILD)` (0ms)
186. `Test_Pgrep_IncludesParents([attrib_set([parent(me,create(PGREP_PARENT))]/PGREP_PARENT,child_value)][pgrep(%!,PGREP_*,child)], PGREP_PARENT)` (0ms)
187. `Test_Reglattrp_IncludesParents([attrib_set(%!/Test_Reglattrp_IncludesParents_001,value1)][attrib_set([parent(me,create(Test_Reglattrp_IncludesParents))]/Test_Reglattrp_IncludesParents_002,value2)][attrib_set(%!/Test_Reglattrp_IncludesParents_100,value3)][reglattrp(%!,Test_Reglattrp_IncludesParents_[0-9]+)], Test_Reglattrp_IncludesParents_001 Test_Reglattrp_IncludesParents_002 Test_Reglattrp_IncludesParents_100)` (0ms)
188. `Test_Regnattrp_CountWithParents([attrib_set(%!/Test_Regnattrp_CountWithParents_001,value1)][attrib_set([parent(me,create(Test_Regnattrp_CountWithParents))]/Test_Regnattrp_CountWithParents_002,value2)][attrib_set(%!/Test_Regnattrp_CountWithParents_100,value3)][regnattrp(%!,Test_Regnattrp_CountWithParents_[0-9]+)], 3)` (0ms)
189. `Test_Regxattrp_RangeWithParents([attrib_set(%!/Test_Regxattrp_RangeWithParents2_001,value1)][attrib_set([parent(me,create(Test_Regxattrp_RangeWithParents2))]/Test_Regxattrp_RangeWithParents2_002,value2)][attrib_set(%!/Test_Regxattrp_RangeWithParents2_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents2_[0-9]+,1,2)], TEST_REGXATTRP_RANGEWITHPARENTS2_001 TEST_REGXATTRP_RANGEWITHPARENTS2_002)` (0ms)
190. `Test_Regxattrp_RangeWithParents([attrib_set(%!/Test_Regxattrp_RangeWithParents_001,value1)][attrib_set(%!/Test_Regxattrp_RangeWithParents_002,value2)][attrib_set(%!/Test_Regxattrp_RangeWithParents_100,value3)][regxattrp(%!/Test_Regxattrp_RangeWithParents_[0-9]+,1,2)], TEST_REGXATTRP_RANGEWITHPARENTS_001 TEST_REGXATTRP_RANGEWITHPARENTS_002)` (0ms)
191. `Test_WipeAttributes_AllAttributes` (0ms)
192. `Test_Zfun_NotImplemented(zfun(TEST_ATTR), #-1 ZONES NOT YET IMPLEMENTED)` (0ms)
193. `TriggerCommand` (0ms)
194. `Trigger_QueuesAttribute` (0ms)
195. `UndestroyCommand` (0ms)
196. `UnfollowCommand` (0ms)
197. `UnlinkExit` (0ms)
198. `UnlockObject` (0ms)
199. `UnrecycleCommand` (0ms)
200. `UseCommand` (0ms)
201. `Valid_AttributeValue(valid(attrvalue,test_value), 1)` (0ms)
202. `Valid_AttributeValue(valid(attrvalue,test_value,NONEXISTENT_ATTR), 1)` (0ms)
203. `VerbCommand` (0ms)
204. `VerbExecutesAwhat` (0ms)
205. `VerbInsufficientArgs` (0ms)
206. `VerbPermissionDenied` (0ms)
207. `VerbWithAttributes` (0ms)
208. `VerbWithDefaultMessages` (0ms)
209. `VerbWithStackArguments` (0ms)
210. `WCheckCommand_WithAll_RequiresWizard` (0ms)
211. `WCheckCommand_WithMe_ChecksOwnedObjects` (0ms)
212. `WarningsCommand` (0ms)
213. `WcheckCommand` (0ms)
214. `WhereisCommand` (0ms)
215. `WhisperCommand` (0ms)
216. `WhoCommand` (0ms)
217. `WithCommand` (0ms)
218. `WizmotdCommand` (0ms)
219. `ZMRUserDefinedCommandTest` (0ms)
220. `Zemit(zemit(#0,test message), )` (0ms)
221. `ZemitBasic(@zemit Test zone emit, Test zone emit)` (0ms)

</details>

