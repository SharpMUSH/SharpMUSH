
namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The Play page's settings that follow the player: the group <c>portal.play</c>, kept on the account
/// (<c>#acct:&lt;key&gt;</c>) as <c>PREF`PORTAL`PLAY`&lt;KEY&gt;</c>, so every computer and every character on it
/// shows the game the same way. What only suits one screen (the width, the input box's height, which panels are
/// open) stays in that browser.
/// </summary>
/// <remarks>A Play feature that keeps a setting on the account adds its key here, with a default that matches Play without it.</remarks>
public static class PlayPreferences
{
	/// <summary>The group's name within the portal's package id, <c>portal</c>.</summary>
	public const string GroupName = "play";

	/// <summary>Whether the terminal shows the commands the player sends.</summary>
	public const string Echo = "echo";

	/// <summary>Whether pages stay in the Pages list, out of the main output.</summary>
	public const string HidePages = "hide_pages";

	/// <summary>Whether channel lines stay in their channel's view, out of the main output.</summary>
	public const string HideChannels = "hide_channels";

	/// <summary>The player's alert rules: what to look for in the game's lines, and the colour, sound or notification a match gets.</summary>
	public const string Highlights = "highlights";

	/// <summary>
	/// The most characters the alert rules may take: 50 rules of up to 200 characters each, with their choices, even
	/// when every character of a pattern is written escaped (<c>\uXXXX</c>, as non-ASCII text and <c>&lt;&gt;&amp;+</c>
	/// are). Under the attribute value limit of 64 KiB.
	/// </summary>
	public const int HighlightsMaxLength = 64000;

	/// <summary>The group's declaration.</summary>
	public static PackagePreferenceGroupSpec Group { get; } = new(
		GroupName,
		PreferenceScope.Account,
		"Play",
		"How the Play page shows the game, on every computer and every character of the account.",
		[
			Boolean(Echo, true, "Echo commands", "Show the commands you send in the output."),
			Boolean(HidePages, false, "Hide pages", "Keep pages in the Pages list, out of the main output."),
			Boolean(HideChannels, false, "Hide channel messages", "Keep channel lines in their channel's view, out of the main output."),
			Json(Highlights, "[]", HighlightsMaxLength, "Alerts",
				"Rules that colour, style, rewrite or hide a line or part of it, play a sound or raise a notification when a line from the game matches. Edited in Play's Alerts.")
		]);

	private static PackagePreferenceSpec Boolean(string key, bool fallback, string label, string description)
		=> new(key, PreferenceType.Boolean, fallback ? "1" : "0", [], null, null, Preferences.DefaultMaxLength, label, description);

	/// <summary>A JSON preference, such as a list of rules, holding at most <paramref name="maxLength"/> characters.</summary>
	private static PackagePreferenceSpec Json(string key, string fallback, int maxLength, string label, string description)
		=> new(key, PreferenceType.Json, fallback, [], null, null, maxLength, label, description);
}
