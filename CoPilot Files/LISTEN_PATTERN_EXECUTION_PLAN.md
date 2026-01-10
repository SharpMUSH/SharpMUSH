# Listen Pattern Execution Integration Plan

## Current State Analysis

The listener routing system currently:
- ✅ Matches ^-listen patterns correctly
- ✅ Matches @listen attribute patterns
- ✅ Identifies which action attributes should trigger (AHEAR/AMHEAR/AAHEAR)
- ⏳ Does NOT execute the matched patterns (lacks parser)

## Problem Statement

The comment requests: "NotifyService should use the Mediator to trigger the 'command' like listen pattern."

This means we need to execute the matched listen patterns as if they were commands, using the Mediator pattern.

## Solution Architecture

### Option 1: Create ExecuteListenPatternCommand (RECOMMENDED)

**Approach**: Create a Mediator command that encapsulates listen pattern execution.

**Advantages**:
- Clean separation of concerns
- Uses existing Mediator infrastructure
- Can be queued/scheduled if needed
- Testable in isolation
- Follows CQRS pattern

**Implementation**:
1. Create `ExecuteListenPatternCommand` record in `SharpMUSH.Library/Commands/`
2. Create handler `ExecuteListenPatternCommandHandler` in `SharpMUSH.Implementation/Handlers/`
3. Modify `ListenerRoutingService` to send the command via Mediator
4. Handler will:
   - Create a parser with appropriate state (executor, enactor, registers)
   - Call `IAttributeService.EvaluateAttributeFunctionAsync()`
   - Set %0-%9 from pattern capture groups
   - Set %# to speaker DBRef
   - Set %! to listener DBRef

**Files to Create**:
- `SharpMUSH.Library/Commands/ListenPattern/ExecuteListenPatternCommand.cs`
- `SharpMUSH.Implementation/Handlers/ListenPattern/ExecuteListenPatternCommandHandler.cs`

**Files to Modify**:
- `SharpMUSH.Library/Services/ListenerRoutingService.cs`

### Option 2: Direct Parser Creation (NOT RECOMMENDED)

**Approach**: Create a parser directly in ListenerRoutingService.

**Disadvantages**:
- Violates separation of concerns
- Makes service harder to test
- Bypasses Mediator pattern
- Inconsistent with rest of codebase

### Option 3: Use Task Scheduler (PARTIAL SOLUTION)

**Approach**: Queue execution via ITaskScheduler.

**Advantages**:
- Deferred execution
- Won't block notifications

**Disadvantages**:
- Still needs parser creation logic somewhere
- Adds complexity
- May not be necessary for listen patterns

## Recommended Implementation Plan

### Phase 1: Create Command Structure

1. **Create ExecuteListenPatternCommand**:
   ```csharp
   public record ExecuteListenPatternCommand(
       AnySharpObject Listener,
       AnySharpObject Speaker,
       string AttributeName,
       Dictionary<string, CallState> Registers
   ) : ICommand;
   ```

2. **Create ExecuteListenPatternCommandHandler**:
   - Inject: IMediator, IAttributeService, IServiceProvider
   - Get parser from service provider
   - Set up parser state with registers
   - Execute attribute via EvaluateAttributeFunctionAsync()

### Phase 2: Integrate into ListenerRoutingService

1. Modify `ProcessListenPatternsAsync()`:
   - When pattern matches, build registers dictionary
   - Send `ExecuteListenPatternCommand` via mediator
   - Fire-and-forget (don't await)

2. Modify `ProcessListenAttributeAsync()`:
   - When @listen matches, build registers dictionary
   - Send `ExecuteListenPatternCommand` via mediator
   - Fire-and-forget (don't await)

### Phase 3: Testing

1. Unit tests for ExecuteListenPatternCommandHandler
2. Integration tests for end-to-end flow
3. Verify registers are set correctly (%0-%9, %#, %!)

## Register Setup

For ^-listen patterns:
- %0 = full matched text
- %1-%9 = capture groups from regex
- %# = speaker DBRef
- %! = listener DBRef
- %L = location DBRef (optional)

For @listen patterns:
- %0 = matched message
- %# = speaker DBRef
- %! = listener DBRef

## Key Considerations

1. **Performance**: Use fire-and-forget to avoid blocking notifications
2. **Error Handling**: Wrap execution in try-catch to prevent listener errors from breaking system
3. **Permissions**: Execute as listener (executor) with speaker as enactor
4. **Parser State**: Need to create parser with minimal state
5. **Caching**: Leverage existing attribute caching

## Implementation Order

1. Create ExecuteListenPatternCommand record
2. Create ExecuteListenPatternCommandHandler
3. Update ListenerRoutingService.ProcessListenPatternsAsync()
4. Update ListenerRoutingService.ProcessListenAttributeAsync()
5. Add unit tests
6. Add integration tests
7. Documentation

## Success Criteria

- ✅ Matched ^-listen patterns execute their attribute code
- ✅ Matched @listen patterns execute AHEAR/AMHEAR/AAHEAR
- ✅ Registers are correctly populated
- ✅ Execution doesn't block notifications
- ✅ Errors in listeners don't crash the system
- ✅ All tests pass
