# Monitor, Puppet, and Listen Pattern Architecture Plan

## Executive Summary

This document outlines the architectural plan for implementing PennMUSH-compatible monitor, puppet, and listen pattern functionality in SharpMUSH. These features enable objects to "hear" and react to communications in their environment, providing essential functionality for immersive gameplay, automation, and remote sensing.

## Background

### PennMUSH Functionality

Based on PennMUSH documentation and source code analysis, the system implements three interconnected features:

1. **PUPPET Flag (Things)**: Objects that relay everything they hear to their owner
2. **MONITOR Flag**: Enables ^-listen patterns on objects; for players, provides page attempt notifications
3. **Listen Patterns**: Two types of listening mechanisms:
   - `@listen` attribute: Simple pattern matching
   - `^-listen patterns`: Attribute-based patterns (requires MONITOR flag)

## Current SharpMUSH Architecture

### Notification System

The existing `INotifyService` in `SharpMUSH.Library.Services.NotifyService.cs`:
- Handles all message distribution to players
- Implements automatic batching with 10ms timeout
- Supports different notification types (Emit, Say, Pose, etc.)
- Routes messages through Kafka/MassTransit to connection servers

### Command Matching System

The existing command discovery in `GetCommandAttributesQuery`:
- Caches command attributes with pre-compiled regex patterns
- Supports wildcard and regex matching
- Invalidates cache automatically when attributes change
- Similar pattern can be applied to listen patterns

### Permission System

The existing `IPermissionService`:
- Implements `CanInteract()` for interaction rules
- Handles lock evaluation through `ILockService`
- Can be extended to check @lock/listen and @lock/use for listen patterns

## Proposed Architecture

### 1. Database Schema Extensions

#### New Object Flags

Add to `SharpObjectFlag` table/collection:

```csharp
// PUPPET flag - for things
{
    Name: "PUPPET",
    Symbol: "p",
    SetPermissions: ["control"],
    UnsetPermissions: ["control"],
    System: true,
    TypeRestrictions: ["Thing"],
    Disabled: false
}

// MONITOR flag - for all object types  
{
    Name: "MONITOR",
    Symbol: "M",
    SetPermissions: ["control"],
    UnsetPermissions: ["control"],
    System: true,
    TypeRestrictions: ["Player", "Thing", "Room"],
    Disabled: false
}

// LISTEN_PARENT flag - for all object types
{
    Name: "LISTEN_PARENT",
    Symbol: "^",
    SetPermissions: ["control"],
    UnsetPermissions: ["control"],
    System: true,
    TypeRestrictions: ["Player", "Thing", "Room"],
    Disabled: false
}

// VERBOSE flag - for puppets
{
    Name: "VERBOSE",
    Symbol: "v",
    SetPermissions: ["control"],
    UnsetPermissions: ["control"],
    System: true,
    TypeRestrictions: ["Player", "Thing"],
    Disabled: false
}
```

#### New Attribute Flags

Add to `SharpAttributeFlag` table/collection:

```csharp
// AAHEAR flag - ^-listen triggers for all sources
{
    Name: "AAHEAR",
    Symbol: "A",
    System: true,
    Inheritable: false
}

// AMHEAR flag - ^-listen triggers only for object itself
{
    Name: "AMHEAR", 
    Symbol: "M",
    System: true,
    Inheritable: false
}
```

#### Standard Attributes

Add to standard attribute definitions:

- `LISTEN`: Pattern for @listen functionality
- `AHEAR`: Action triggered when @listen matches (others speaking)
- `AAHEAR`: Action triggered when @listen matches (anyone speaking)
- `AMHEAR`: Action triggered when @listen matches (self speaking)
- `FORWARDLIST`: DBRefs to forward listened messages to (for AUDIBLE objects)
- `PREFIX`: Prefix for forwarded messages
- `FILTER`: Pattern to filter what is heard
- `INFILTER`: Pattern to filter what contents hear

#### New Lock Types

Add to standard lock definitions:

- `LISTEN_LOCK`: Controls who can trigger ^-listen patterns (in addition to USE_LOCK)

### 2. Notification Routing Layer

#### Overview

Extend `INotifyService` to include a notification interception layer that routes messages to listening objects before final delivery. This maintains the single responsibility of the notify service while adding listener awareness.

#### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    NotifyService.Notify()                    │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────────┐
│              Listener Routing Layer                          │
│  1. Determine notification context (room, object, etc.)     │
│  2. Discover listening objects in context                   │
│  3. For each listener:                                       │
│     a. Check if listener should hear (locks, filters)       │
│     b. Match against listen patterns                        │
│     c. Queue triggered actions (@ahear, ^-patterns)         │
│     d. If PUPPET flag, relay to owner                       │
└─────────────────┬───────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────────┐
│              Original Notification Flow                      │
│  Send message to player connections via MassTransit         │
└─────────────────────────────────────────────────────────────┘
```

#### Implementation Strategy

Create a new `IListenerRoutingService` that is called by `NotifyService`:

```csharp
public interface IListenerRoutingService
{
    /// <summary>
    /// Process a notification to discover and trigger listening objects.
    /// Called by NotifyService before sending to player connections.
    /// </summary>
    ValueTask ProcessNotificationAsync(
        NotificationContext context,
        OneOf<MString, string> message,
        AnySharpObject? sender,
        INotifyService.NotificationType type);
}

public record NotificationContext(
    DBRef Target,              // Who is being notified
    DBRef? Location,           // Where notification is happening
    bool IsRoomBroadcast,      // Is this a room-wide message?
    DBRef[] ExcludedObjects    // Objects to exclude from listening
);
```

### 3. Listener Discovery System

#### Query Pattern

Create a query similar to `GetCommandAttributesQuery` for listen patterns:

```csharp
/// <summary>
/// Query to get listen pattern attributes for an object.
/// Results cached automatically via QueryCachingBehavior.
/// </summary>
public record GetListenAttributesQuery(AnySharpObject SharpObject) 
    : IQuery<ListenAttributeCache[]>, ICacheable
{
    public string CacheKey => $"listens:{SharpObject.Object().DBRef}";
    public string[] CacheTags => [];
}

public record ListenAttributeCache(
    SharpAttribute Attribute,
    Regex CompiledRegex,
    bool IsRegexFlag,
    ListenBehavior Behavior    // AHEAR, AAHEAR, or AMHEAR
);

public enum ListenBehavior
{
    AHear,      // Default - triggers for others
    AAHear,     // Triggers for anyone (attribute has AAHEAR flag)
    AMHear      // Triggers for self only (attribute has AMHEAR flag)
}
```

#### Discovery Algorithm

For a given notification in a room:

1. **Query room contents** for objects with:
   - `@listen` attribute set
   - `MONITOR` flag set (for ^-patterns)
   - `PUPPET` flag set (for relaying)

2. **Filter by interaction rules**:
   - Check `CanInteract()` between speaker and listener
   - Respect DARK flag, interaction locks, etc.

3. **Check @filter and @lock/filter**:
   - If listener has @filter, match against it
   - Only proceed if @lock/filter passes

4. **Cache listener list** per room:
   - Cache key: `listeners:{roomDbRef}`
   - Invalidate when: objects enter/leave room, flags change, @listen changes

### 4. Listen Pattern Matching

#### @listen Attribute Matching

For objects with `@listen` attribute:

1. **Retrieve @listen pattern** from attribute
2. **Match message** against pattern (wildcard or regex)
3. **Determine trigger behavior**:
   - If speaker is listener itself → check for @amhear, @aahear
   - If speaker is someone else → check for @ahear, @aahear
4. **Check lock**: Evaluate @lock/listen
5. **Queue action**: Queue @ahear/@amhear/@aahear attribute for execution

#### ^-listen Pattern Matching

For objects with `MONITOR` flag:

1. **Query listen attributes**: Use `GetListenAttributesQuery`
2. **Include parent patterns** if `LISTEN_PARENT` flag set
3. **For each ^-pattern**:
   - Match message against pattern
   - Check behavior flag (AAHEAR, AMHEAR, default)
   - Determine if should trigger based on speaker
4. **Check locks**: Must pass BOTH @lock/use AND @lock/listen
5. **Queue action**: Queue attribute content for execution with %0-%9 set

#### Pattern Matching Implementation

Reuse existing command matching infrastructure:

```csharp
public interface IListenPatternMatcher
{
    /// <summary>
    /// Match a message against listen patterns on an object.
    /// Returns matched patterns with captured groups.
    /// </summary>
    ValueTask<ListenMatch[]> MatchListenPatternsAsync(
        AnySharpObject listener,
        string message,
        AnySharpObject speaker,
        bool checkParents = false);
}

public record ListenMatch(
    SharpAttribute Attribute,
    string[] CapturedGroups,
    ListenBehavior Behavior
);
```

### 5. Lock Evaluation Integration

#### Lock Checking Order

For **@listen** attribute triggers:
1. Check @lock/listen on listener object
2. If passes, trigger @ahear/@amhear/@aahear

For **^-listen** pattern triggers:
1. Check @lock/use on listener object (using speaker as %#)
2. Check @lock/listen on listener object (using speaker as %#)
3. If both pass, trigger attribute content

#### Lock Evaluation Context

When evaluating locks for listen triggers:
- `%#` (enactor) = speaker who made the sound
- `%!` (executor) = listening object
- `%@` (caller) = listening object
- Location context = listener's location

### 6. Puppet Relaying Mechanism

#### Relaying Logic

For objects with `PUPPET` flag:

```csharp
public interface IPuppetRelayService
{
    /// <summary>
    /// Relay a message heard by a puppet to its owner.
    /// Respects VERBOSE flag for same-room filtering.
    /// </summary>
    ValueTask RelayToOwnerAsync(
        AnySharpObject puppet,
        OneOf<MString, string> message,
        AnySharpObject speaker,
        INotifyService.NotificationType type);
}
```

#### Relaying Rules

1. **Ownership check**: Puppet must have valid owner
2. **Location check**: 
   - If puppet and owner in same location AND !VERBOSE → don't relay
   - Otherwise, relay with prefix
3. **Prefix format**: `"[{puppet.Name}] {message}"`
4. **Custom prefix**: Use puppet's @prefix attribute if set
5. **Connected check**: Only relay if owner is connected

#### Message Format

Default format:
```
[PuppetName] Alice says, "Hello world"
```

With custom @prefix:
```
{@prefix value} Alice says, "Hello world"
```

### 7. Action Queueing and Execution

#### Queue Integration

When listen patterns match or @listen triggers:

1. **Create queue entry** similar to $-command execution
2. **Set registers**:
   - `%0-%9`: Captured pattern groups (for ^-listens)
   - `%#`: Speaker DBRef
   - `%!`: Listener DBRef
   - `%L`: Listener's location
3. **Queue attribute** for execution on listener
4. **Execute asynchronously** to avoid blocking notification flow

#### Execution Context

```csharp
public interface IListenActionQueue
{
    /// <summary>
    /// Queue a listen action for execution.
    /// </summary>
    ValueTask QueueListenActionAsync(
        AnySharpObject listener,
        SharpAttribute attribute,
        string[] capturedGroups,
        AnySharpObject speaker);
}
```

### 8. Performance Considerations

#### Caching Strategy

1. **Listener Discovery Cache**:
   - Cache per room: `listeners:{roomDbRef}`
   - Cache per object: `listens:{objDbRef}` (for patterns)
   - TTL: Indefinite (invalidate on changes)
   - Invalidation triggers:
     - Object enters/leaves room
     - MONITOR/PUPPET/LISTEN_PARENT flag changes
     - @listen attribute changes
     - ^-listen attributes added/removed/modified

2. **Pattern Compilation Cache**:
   - Pre-compile regex patterns like $-commands
   - Store in `ListenAttributeCache`
   - Invalidate when attribute changes

3. **Lock Evaluation Cache**:
   - Leverage existing lock evaluation caching
   - Cache @lock/listen and @lock/use results per speaker

#### Optimization Techniques

1. **Early Filtering**:
   - Filter out non-listening objects before pattern matching
   - Check interaction rules before lock evaluation
   - Skip pattern matching if no listeners in room

2. **Batch Processing**:
   - When multiple objects in room have listeners, batch lock checks
   - Process all ^-patterns on an object in single pass

3. **Async Processing**:
   - Listener matching happens asynchronously
   - Don't block original notification
   - Use fire-and-forget pattern for action queueing

4. **Selective Processing**:
   - Only process listeners for NotificationTypes that make sense:
     - Say, Pose, Emit, SemiPose → process listeners
     - Announce (private messages) → skip listeners
   - Configuration option: `player_listen` to enable/disable @listen on players

### 9. Integration Points

#### NotifyService Extension

Modify `NotifyService.Notify()` methods:

```csharp
public async ValueTask Notify(
    DBRef who, 
    OneOf<MString, string> what, 
    AnySharpObject? sender = null, 
    NotificationType type = NotificationType.Announce)
{
    // ... existing null/empty checks ...
    
    // NEW: Route to listeners if appropriate notification type
    if (ShouldProcessListeners(type))
    {
        var location = await DetermineNotificationLocation(who);
        var context = new NotificationContext(
            Target: who,
            Location: location,
            IsRoomBroadcast: IsRoomNotification(who),
            ExcludedObjects: []
        );
        
        // Process listeners asynchronously (fire-and-forget)
        _ = listenerRoutingService.ProcessNotificationAsync(
            context, what, sender, type);
    }
    
    // ... existing notification code ...
}
```

#### CommunicationService Integration

Extend `SendToRoomAsync()` to provide exclusion list:

```csharp
public async ValueTask SendToRoomAsync(
    AnySharpObject executor,
    AnySharpContainer room,
    Func<AnySharpObject, OneOf<MString, string>> messageFunc,
    INotifyService.NotificationType notificationType,
    AnySharpObject? sender = null,
    IEnumerable<AnySharpObject>? excludeObjects = null)
{
    // ... existing code ...
    
    // Pass exclusion list to notification context
    // so listeners don't hear their own triggered actions
}
```

#### Command Discovery Service

Reuse pattern matching infrastructure from `CommandDiscoveryService`:
- Extract common pattern matching logic
- Share regex compilation strategy
- Use same caching mechanism

## PennMUSH Compatibility Requirements

### Required Behavior

1. **@listen Attribute**:
   - Simple wildcard pattern matching
   - Triggers @ahear for others, @amhear for self, @aahear for all
   - Requires @lock/listen to pass
   - Not inherited from @parent
   - Standard attribute, visible with `examine`

2. **^-listen Patterns**:
   - Requires MONITOR flag to activate
   - Supports wildcard and regex matching (via regexp attribute flag)
   - Supports case-sensitive matching (via case attribute flag)
   - Honors AAHEAR and AMHEAR attribute flags
   - Requires BOTH @lock/use AND @lock/listen to pass
   - Inherited from @parent only if LISTEN_PARENT flag set
   - Sets %0-%9 capture registers like $-commands

3. **PUPPET Flag**:
   - Things only
   - Relays everything heard to owner
   - Prefixes with puppet name by default
   - Uses @prefix attribute if set
   - Doesn't relay if in same room as owner (unless VERBOSE)
   - Only relays if owner is connected

4. **MONITOR Flag**:
   - Players: Notified when someone tries to page them (even if failed)
   - Objects: Enables ^-listen pattern matching
   - Can be set on players, things, and rooms

5. **LISTEN_PARENT Flag**:
   - When set, ^-listen patterns checked on @parent chain
   - Does NOT affect @listen attribute (never inherited)
   - Similar to how $-commands work with @parent

### Lock Compatibility

- `@lock/listen`: Controls who can trigger @listen and ^-patterns
- `@lock/use`: Must also pass for ^-patterns (but not @listen)
- `@lock/filter`: Controls what passes to listener and its contents
- `@lock/infilter`: Controls what listener's contents hear

### Configuration Options

PennMUSH configuration options to implement:

- `player_listen`: Boolean, controls if @listen is checked on players
- `room_listen`: Boolean, controls if @listen is checked on rooms (usually true)
- `thing_listen`: Boolean, controls if @listen is checked on things (usually true)

### Failure Attributes

When locks fail for listen triggers:

- `LISTEN_LOCK`FAILURE`: Triggered when @lock/listen fails
- `LISTEN_LOCK`AFLURE`: Action triggered after above
- `USE_LOCK`FAILURE`: Triggered when @lock/use fails (for ^-patterns)
- `USE_LOCK`AFLURE`: Action triggered after above

## Implementation Phases

### Phase 1: Database Schema (Foundation)
- Add PUPPET, MONITOR, LISTEN_PARENT, VERBOSE flags
- Add AAHEAR, AMHEAR attribute flags
- Add LISTEN_LOCK lock type
- Add standard attributes (LISTEN, AHEAR, AAHEAR, AMHEAR, etc.)
- Migration scripts for existing databases

### Phase 2: Core Infrastructure
- Implement `IListenerRoutingService`
- Implement `IListenPatternMatcher`
- Implement `GetListenAttributesQuery` and handler
- Add listener discovery to room contents query
- Implement caching strategy

### Phase 3: Pattern Matching
- Implement @listen attribute matching
- Implement ^-listen pattern matching
- Integrate with lock evaluation system
- Add AAHEAR/AMHEAR behavior support
- Add LISTEN_PARENT inheritance support

### Phase 4: Puppet Relaying
- Implement `IPuppetRelayService`
- Add PUPPET flag behavior
- Add @prefix support
- Add VERBOSE flag support
- Add same-room filtering

### Phase 5: Action Execution
- Implement `IListenActionQueue`
- Integrate with existing queue system
- Set up register population (%0-%9, %#, etc.)
- Add async execution support

### Phase 6: NotifyService Integration
- Modify NotifyService.Notify() methods
- Add notification context determination
- Add listener routing calls
- Configure notification type filtering

### Phase 7: Testing & Compatibility
- Unit tests for each component
- Integration tests for complete flow
- PennMUSH compatibility tests
- Performance benchmarking
- Load testing with many listeners

### Phase 8: Documentation & Configuration
- Update help files
- Add configuration options
- Document performance characteristics
- Add migration guide from PennMUSH

## Testing Strategy

### Unit Tests

1. **Listener Discovery**:
   - Find objects with @listen in room
   - Find objects with MONITOR in room
   - Find objects with PUPPET in room
   - Filter by interaction rules
   - Cache invalidation on flag changes

2. **Pattern Matching**:
   - @listen wildcard matching
   - ^-listen wildcard matching
   - ^-listen regex matching
   - Capture group extraction
   - AAHEAR/AMHEAR behavior

3. **Lock Evaluation**:
   - @lock/listen checking
   - @lock/use checking
   - Combined lock checking
   - Lock failure attribute triggering

4. **Puppet Relaying**:
   - Basic relay to owner
   - Same-room filtering
   - VERBOSE flag override
   - Custom @prefix support
   - Connected owner check

### Integration Tests

1. **Complete Listen Flow**:
   - Say something in room with listeners
   - Verify @ahear triggered
   - Verify ^-patterns triggered
   - Verify puppet relaying
   - Verify action queueing

2. **Lock Interactions**:
   - Locked listeners don't trigger
   - Failed locks trigger failure attributes
   - Multiple lock types work together

3. **Performance Tests**:
   - Room with many listeners
   - Complex ^-pattern sets
   - High message volume
   - Cache effectiveness

### PennMUSH Compatibility Tests

Compare behavior with PennMUSH:
- @listen pattern matching
- ^-listen pattern matching
- PUPPET relaying
- MONITOR behavior
- Lock evaluation
- Attribute inheritance
- Failure attributes

## Security Considerations

### Permission Checks

1. **Listen Trigger Permission**:
   - Only trigger if @lock/listen passes
   - Only trigger ^-patterns if @lock/use AND @lock/listen pass
   - Respect interaction rules

2. **Puppet Control**:
   - Only owner can receive puppet relay
   - Can't use puppet to bypass permissions
   - Puppet actions execute as puppet (not owner)

3. **Information Leakage**:
   - DARK objects should filter appropriately
   - HAVEN should work with listening
   - MONITOR page notifications respect privacy

### Resource Protection

1. **Rate Limiting**:
   - Limit number of listen patterns per object
   - Limit complexity of regex patterns
   - Limit queue depth for triggered actions

2. **Cost Tracking**:
   - ^-pattern matching may cost pennies (configurable)
   - Similar to @search LISTEN class
   - Prevent abuse through excessive listeners

## Monitoring and Metrics

### Metrics to Track

1. **Performance Metrics**:
   - Listener discovery time per notification
   - Pattern matching time per listener
   - Lock evaluation time
   - Action queueing time
   - Cache hit/miss ratio

2. **Usage Metrics**:
   - Number of active listeners per room
   - Number of ^-patterns per object
   - Puppet relay frequency
   - Listen trigger frequency

3. **System Health**:
   - Queue depth for listen actions
   - Cache memory usage
   - Notification latency impact

## Open Questions

1. **Notification Batching**: Should listen triggers be batched with notification batching?
2. **Recursive Triggering**: Should triggered actions be able to trigger more listeners?
3. **Cross-Room Listening**: Should AUDIBLE exits enable cross-room listening?
4. **Listen Priority**: What order should multiple matching patterns execute in?
5. **Puppet Chain Limit**: Should there be a limit on puppet relay chains?

## References

### PennMUSH Documentation
- `help @listen`
- `help ^`
- `help PUPPET`
- `help MONITOR`
- `help LISTEN_PARENT`
- `help AAHEAR`
- `help AMHEAR`

### PennMUSH Source Code
- `src/notify.c`: Notification system
- `src/attrib.c`: Attribute matching (AF_LISTEN, AF_AHEAR, AF_MHEAR flags)
- `src/move.c`: Hearer() macro, object movement notifications
- `hdrs/attrib.h`: Attribute flag definitions

### SharpMUSH Code
- `SharpMUSH.Library.Services.NotifyService`: Current notification implementation
- `SharpMUSH.Library.Services.CommunicationService`: Room broadcasting
- `SharpMUSH.Library.Queries.Database.GetCommandAttributesQuery`: Pattern matching example
- `SharpMUSH.Library.Services.PermissionService`: Lock evaluation

## Conclusion

This architecture provides a comprehensive, PennMUSH-compatible implementation of monitor, puppet, and listen patterns. By extending the existing notification service with a listener routing layer, we maintain clean separation of concerns while adding powerful new functionality. The caching strategy ensures performance at scale, and the phased implementation approach allows for iterative development and testing.
