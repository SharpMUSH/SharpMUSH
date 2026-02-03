#!/usr/bin/env dotnet fsi

// Demonstration: Partition Key Fix for Out-of-Order Responses
// This script demonstrates how the fix ensures message ordering

open System
open System.Collections.Concurrent
open System.Reflection

// Simulate the message types
type TelnetOutputMessage(handle: int64, data: byte[]) =
    member _.Handle = handle
    member _.Data = data

type BroadcastMessage(data: byte[]) =
    member _.Data = data

// Simulate the partition key cache and logic from KafkaMessageBus
let handlePropertyCache = ConcurrentDictionary<Type, PropertyInfo option>()

let getPartitionKey<'T when 'T : not struct> (message: 'T) =
    let messageType = typeof<'T>
    
    let handleProperty = 
        handlePropertyCache.GetOrAdd(messageType, fun t ->
            match t.GetProperty("Handle") with
            | null -> None
            | prop when prop.PropertyType = typeof<int64> -> Some prop
            | _ -> None
        )
    
    match handleProperty with
    | Some prop ->
        match prop.GetValue(message) with
        | :? int64 as handle -> handle.ToString()
        | _ -> Guid.NewGuid().ToString()
    | None -> Guid.NewGuid().ToString()

// Demonstration
printfn "==================================================================="
printfn "DEMONSTRATION: Partition Key Fix for Out-of-Order Responses"
printfn "==================================================================="
printfn ""

// Simulate the problematic scenario: @dolist lnum(1,100)=think %%i0
printfn "Scenario: @dolist lnum(1,100)=think %%i0"
printfn "This creates 100 sequential messages that should arrive in order"
printfn ""

let connectionHandle = 42L
let messages = 
    [1..100]
    |> List.map (fun i -> 
        TelnetOutputMessage(connectionHandle, Text.Encoding.UTF8.GetBytes(string i))
    )

printfn "Connection Handle: %d" connectionHandle
printfn "Total Messages: %d" messages.Length
printfn ""

// Get partition keys for all messages
let partitionKeys = messages |> List.map getPartitionKey

// Count unique partition keys
let uniqueKeys = partitionKeys |> Set.ofList

printfn "==================================================================="
printfn "RESULTS:"
printfn "==================================================================="
printfn "Unique Partition Keys: %d" uniqueKeys.Count
printfn ""

if uniqueKeys.Count = 1 then
    printfn "✓ SUCCESS: All messages use the SAME partition key!"
    printfn "  Partition Key: '%s'" (partitionKeys.[0])
    printfn ""
    printfn "  This means:"
    printfn "  1. All 100 messages route to the SAME Kafka partition"
    printfn "  2. Kafka guarantees FIFO ordering within a partition"
    printfn "  3. Messages will arrive in order: 1, 2, 3, ..., 100"
    printfn ""
    printfn "  ✓ OUT-OF-ORDER ISSUE IS FIXED!"
else
    printfn "✗ FAILURE: Messages use DIFFERENT partition keys!"
    printfn "  This would cause out-of-order delivery"
    printfn ""
    printfn "  Unique keys: %A" uniqueKeys

printfn ""
printfn "==================================================================="
printfn "COMPARISON:"
printfn "==================================================================="
printfn ""
printfn "BEFORE THE FIX (Random GUIDs as keys):"
printfn "  Message 1 → Key: random-guid-abc → Partition A"
printfn "  Message 2 → Key: random-guid-def → Partition B"
printfn "  Message 3 → Key: random-guid-ghi → Partition C"
printfn "  Result: Messages could arrive in ANY order (3, 1, 2, ...)"
printfn ""
printfn "AFTER THE FIX (Handle as key):"
printfn "  Message 1 → Key: '42' → Partition A"
printfn "  Message 2 → Key: '42' → Partition A"
printfn "  Message 3 → Key: '42' → Partition A"
printfn "  Result: Messages arrive in FIFO order (1, 2, 3, ...)"
printfn ""

// Demonstrate different connections get different keys
printfn "==================================================================="
printfn "LOAD BALANCING VERIFICATION:"
printfn "==================================================================="
printfn ""
let connection1 = TelnetOutputMessage(1L, [||])
let connection2 = TelnetOutputMessage(2L, [||])
let connection3 = TelnetOutputMessage(3L, [||])

let key1 = getPartitionKey connection1
let key2 = getPartitionKey connection2
let key3 = getPartitionKey connection3

printfn "Connection 1 (Handle=1) → Partition Key: '%s'" key1
printfn "Connection 2 (Handle=2) → Partition Key: '%s'" key2
printfn "Connection 3 (Handle=3) → Partition Key: '%s'" key3
printfn ""

if key1 <> key2 && key2 <> key3 && key1 <> key3 then
    printfn "✓ Different connections get different keys (good for load balancing)"
else
    printfn "✗ Different connections should get different keys!"

printfn ""
printfn "==================================================================="
printfn "CONCLUSION:"
printfn "==================================================================="
printfn "The fix ensures that all messages from a single connection use"
printfn "the same partition key (the connection Handle), which guarantees"
printfn "FIFO ordering through Kafka's partition-level ordering guarantee."
printfn ""
printfn "This solves the out-of-order response issue for commands like:"
printfn "  @dolist lnum(1,100)=think %%i0"
printfn "==================================================================="
