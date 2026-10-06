using System.Text.Json;

namespace SharpMUSH.Library.Models.Packages;

/// <summary>
/// A configuration option a package sets (the <c>settings:</c> block, format 1.3), such as the Messages package
/// pointing <c>messages_object</c> at its object. Installing sets it and remembers the value it replaced; an
/// upgrade sets it again only when the new version changes it, so an administrator's own value survives; removing
/// the package, or a version that drops the setting, puts the replaced value back unless the game has changed it
/// since. Two installed packages never set the same option.
/// </summary>
/// <param name="Option">The option's name as <c>@config</c> lists it, lowercase.</param>
/// <param name="Value">
/// The value as <c>@config/set</c> takes it (<c>yes</c>, <c>40</c>, <c>#12</c>), or one object ref such as
/// <c>{{messages}}</c> for a dbref option.
/// </param>
public sealed record PackageSettingSpec(string Option, string Value);

/// <summary>An option a package set and still owns, as it last set it.</summary>
/// <param name="Option">The option's name as <c>@config</c> lists it, lowercase.</param>
/// <param name="Value">The value the package set, as <c>@config</c> shows it.</param>
/// <param name="Previous">The value it replaced, to put back when the package lets go; null when the option had none.</param>
public sealed record PackageSettingRecord(string Option, string Value, string? Previous)
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>The stored form of a package's settings, as a registry row keeps them.</summary>
	public static string ToJson(IReadOnlyList<PackageSettingRecord> settings) => JsonSerializer.Serialize(settings, Json);

	/// <summary>Reads what <see cref="ToJson"/> wrote.</summary>
	public static IReadOnlyList<PackageSettingRecord> FromJson(string json)
		=> JsonSerializer.Deserialize<List<PackageSettingRecord>>(json, Json) ?? [];
}

/// <summary>What happens to one option in a plan.</summary>
public enum PackageSettingAction
{
	/// <summary>The option changes from <see cref="PackageSettingChange.Current"/> to <see cref="PackageSettingChange.Value"/>.</summary>
	Set,

	/// <summary>The option already has the value; the package records that it set it.</summary>
	Unchanged,

	/// <summary>The game changed the option since the package set it, and the package leaves the game's value.</summary>
	Keep,

	/// <summary>The package lets go of the option and puts back the value it replaced.</summary>
	Restore,

	/// <summary>The package lets go of the option and leaves it as the game has changed it.</summary>
	Release,

	/// <summary>The option cannot be set; the plan cannot be applied.</summary>
	Blocked
}

/// <summary>One option in a plan, shown on the review screen.</summary>
/// <param name="Option">The option's name as <c>@config</c> lists it.</param>
/// <param name="Action">What happens to it.</param>
/// <param name="Current">Its value now, or null when it has none.</param>
/// <param name="Value">Its value after the apply, or null when it will have none.</param>
/// <param name="Detail">Why, for <see cref="PackageSettingAction.Blocked"/> and <see cref="PackageSettingAction.Keep"/>.</param>
public sealed record PackageSettingChange(
	string Option,
	PackageSettingAction Action,
	string? Current,
	string? Value,
	string? Detail = null);
