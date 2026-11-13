using Serilog;
using Serilog.Events;

namespace SharpMUSH.SocketServer.Configuration;

/// <summary>
/// Reads Serilog's <c>MinimumLevel</c> section (a level, or <c>Default</c> and <c>Override</c>) without
/// Serilog.Settings.Configuration, which finds sinks by reflection and cannot run compiled ahead of time.
/// The sinks are fixed in code; the levels stay configurable (<c>Serilog__MinimumLevel__Default=Debug</c>).
/// </summary>
public static class SerilogLevels
{
	public static LoggerConfiguration Apply(LoggerConfiguration logger, IConfiguration section)
	{
		if (Parse(section is IConfigurationSection { Value: { } single } ? single : section["Default"]) is { } level)
			logger.MinimumLevel.Is(level);

		foreach (var entry in section.GetSection("Override").GetChildren())
		{
			if (Parse(entry.Value) is { } overridden) logger.MinimumLevel.Override(entry.Key, overridden);
		}

		return logger;
	}

	private static LogEventLevel? Parse(string? value) =>
		Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level) ? level : null;
}
