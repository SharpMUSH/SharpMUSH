namespace SharpMUSH.Library.Models.InputSessions;

/// <summary>An atomic view of capture and the next generation that may claim queued input.</summary>
public readonly record struct InputCaptureSnapshot(InputSession? Session, InputCaptureTicket? Ticket);

/// <summary>Remembers one successor only; queued input never retains a chain of ended captures.</summary>
public sealed class InputCaptureTicket
{
	private readonly Lock _gate = new();
	private Guid? _successor;
	internal Guid InitialGeneration { get; }
	internal InputCaptureTicket(Guid generation) => InitialGeneration = generation;
	internal Guid ExpectedGeneration { get { lock (_gate) return _successor ?? InitialGeneration; } }
	internal void ObserveStart(Guid generation) { lock (_gate) _successor ??= generation; }
}
