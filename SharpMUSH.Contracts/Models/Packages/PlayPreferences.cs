
namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// The Play page's settings that follow the player: the group <c>portal.play</c>, kept on the account
/// (<c>#acct:&lt;key&gt;</c>) as <c>PREF`PORTAL`PLAY`&lt;KEY&gt;</c>, so every computer and every character on it
/// shows the game the same way. What only suits one screen (the width, the input box's height, which panels are
/// open) stays in that browser.
/// </summary>
/// <remarks>
/// A Play feature that keeps a setting on the account adds its key here, with a default that matches Play without it.
/// A JSON key is stored with the version of its shape (<see cref="Preferences.Store"/>); a change to that shape
/// raises the key's version, and the feature's reader moves a value of an older version
/// (<c>PlaySettings.VersionOf</c>) to the new shape.
/// </remarks>
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

	/// <summary>
	/// The account's channel groups: a JSON array of <c>{"name":…,"channels":[…]}</c>, each shown on the Play page
	/// as one view over its channels.
	/// </summary>
	public const string CommGroups = "commgroups";

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
			Json(CommGroups, "[]", 32768, "Channel groups", "Named sets of channels, each shown as one view with every line labelled with its channel.")
		]);

	private static PackagePreferenceSpec Boolean(string key, bool fallback, string label, string description)
		=> new(key, PreferenceType.Boolean, fallback ? "1" : "0", [], null, null, Preferences.DefaultMaxLength, label, description);

	/// <summary>
	/// A JSON preference, such as a list of rules, holding at most <paramref name="maxLength"/> characters, its shape
	/// at <paramref name="version"/>.
	/// </summary>
	private static PackagePreferenceSpec Json(string key, string fallback, int maxLength, string label, string description,
		int version = 1)
		=> new(key, PreferenceType.Json, fallback, [], null, null, maxLength, label, description) { Version = version };
}
