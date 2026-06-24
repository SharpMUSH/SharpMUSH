namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// A command dispatched from the web portal to the game engine.
/// </summary>
public sealed record EngineCommandRequest(
    /// <summary>DBRef string of the character issuing the command (e.g. "#42").</summary>
    string CharacterDbref,
    /// <summary>Raw command string as typed by the player.</summary>
    string Command,
    /// <summary>UTC timestamp when the command was received by the portal.</summary>
    DateTimeOffset Timestamp);

/// <summary>
/// The engine's synchronous acknowledgement of a dispatched command.
/// Asynchronous output arrives later via SignalR.
/// </summary>
public sealed record EngineCommandAck(
    /// <summary>Whether the command was accepted into the processing queue.</summary>
    bool Accepted,
    /// <summary>Optional immediate rejection reason (e.g. rate-limit, bad state).</summary>
    string? RejectionReason = null);

/// <summary>
/// Queries the engine for the current state visible to a character (room + contents).
/// </summary>
public sealed record EngineStateRequest(
    string CharacterDbref);

/// <summary>
/// A snapshot of the game-world state visible to the requesting character.
/// </summary>
public sealed record EngineStateResponse(
    string CharacterDbref,
    string RoomDbref,
    string RoomName,
    IReadOnlyList<string> VisibleObjectDbrefs);

/// <summary>
/// Abstraction over the transport layer between the web portal and the SharpMUSH game engine.
/// Implementations may use NATS, HTTP, or an in-process call; callers are unaffected.
/// </summary>
public interface IGameEngineBridge
{
    /// <summary>
    /// Dispatches a player command to the game engine.
    /// Returns an <see cref="EngineCommandAck"/> indicating whether the engine accepted the request.
    /// </summary>
    /// <param name="request">The command to dispatch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<EngineCommandAck> SendCommandAsync(
        EngineCommandRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a snapshot of the game-world state visible to the given character.
    /// Returns <see langword="null"/> when the character is not connected or the engine is unreachable.
    /// </summary>
    ValueTask<EngineStateResponse?> GetStateAsync(
        EngineStateRequest request,
        CancellationToken cancellationToken = default);
}
