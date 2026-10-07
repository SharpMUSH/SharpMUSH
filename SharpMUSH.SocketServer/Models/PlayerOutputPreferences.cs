namespace SharpMUSH.SocketServer.Models;

/// <summary>
/// Represents player preferences for output formatting
/// </summary>
/// <param name="AnsiEnabled">Whether ANSI flag is enabled (basic ANSI support)</param>
/// <param name="ColorEnabled">Whether COLOR flag is enabled</param>
/// <param name="Xterm256Enabled">Whether XTERM256 flag is enabled (256-color support)</param>
/// <param name="Locale">BCP-47 locale tag for this connection (e.g. "en", "fr")</param>
/// <param name="TruecolorEnabled">Whether TRUECOLOR flag is enabled (24-bit color support)</param>
/// <param name="Theme">The player's <c>@theme</c>, which layouts are laid out again under, or null for none</param>
public record PlayerOutputPreferences(
	bool AnsiEnabled = true,
	bool ColorEnabled = true,
	bool Xterm256Enabled = false,
	string Locale = "en",
	bool TruecolorEnabled = false,
	string? Theme = null
);
