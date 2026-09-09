namespace SharpMUSH.Library.Services.Interfaces;

public enum TelemetryInvocationKind { Function, Command }

/// <summary>Invocation metadata only: never arguments, results, or executable text.</summary>
public readonly record struct TelemetryInvocation(TelemetryInvocationKind Kind, string Name,
	double ElapsedMilliseconds, bool Success);

/// <summary>Optional bounded local observers of the existing telemetry hooks.</summary>
public interface ITelemetryInvocationObserver
{
	void RecordInvocation(TelemetryInvocation invocation);
}
