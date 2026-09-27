using Microsoft.Extensions.Logging;

namespace SharpMUSH.Tests.Shared;

/// <summary>An <see cref="ILogger"/> that keeps every formatted message with its level.</summary>
public class RecordingLogger : ILogger
{
	public List<(LogLevel Level, string Message)> Entries { get; } = [];

	public IReadOnlyList<string> Messages => [.. Entries.Select(e => e.Message)];

	public IReadOnlyList<string> Warnings => [.. Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

/// <inheritdoc cref="RecordingLogger"/>
public sealed class RecordingLogger<T> : RecordingLogger, ILogger<T>;
