namespace SharpMUSH.ConnectionServer.Models;

/// <summary>
/// Represents player preferences for output formatting
/// </summary>
/// <param name="AnsiEnabled">Whether ANSI flag is enabled (basic ANSI support)</param>
/// <param name="ColorEnabled">Whether COLOR flag is enabled</param>
/// <param name="Xterm256Enabled">Whether XTERM256 flag is enabled (256-color support)</param>
public record PlayerOutputPreferences(
	bool AnsiEnabled = true,
	bool ColorEnabled = true,
	bool Xterm256Enabled = false
);
