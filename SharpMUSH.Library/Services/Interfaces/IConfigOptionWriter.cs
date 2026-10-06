using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Reads and writes one configuration option by the name <c>@config</c> lists it under, the way
/// <c>@config/set</c> does: parsed as PennMUSH's handlers read a value, checked by the option
/// validators, stored in the world and announced to every reader of the options. <c>@config/set</c>,
/// the Messages page and a package's <c>settings:</c> all write through here.
/// </summary>
public interface IConfigOptionWriter
{
	/// <summary>The option's property name, for an option named the way <c>@config</c> lists it, or null.</summary>
	string? PropertyFor(string optionName);

	/// <summary>The name <c>@config</c> lists <paramref name="property"/> under.</summary>
	string NameOf(string property);

	/// <summary>
	/// Whether a command may set <paramref name="property"/>: a scalar option outside the <c>files</c> group
	/// and not one of God's (the SQL credentials).
	/// </summary>
	bool IsSettable(string property);

	/// <summary>
	/// Parses <paramref name="text"/> as <paramref name="property"/>'s value: yes/no for a flag, a number with an
	/// optional <c>#</c> for a number or dbref, <c>-1</c> for no dbref, the text itself for a string.
	/// </summary>
	bool TryParse(string property, string text, out object? value);

	/// <summary>
	/// <paramref name="value"/> written as <see cref="TryParse"/> reads it back: <c>#12</c> for a dbref, <c>yes</c>
	/// for a flag, null for no value.
	/// </summary>
	string? Format(string property, object? value);

	/// <summary>The options as stored in the world, or the running ones when nothing is stored yet.</summary>
	ValueTask<SharpMUSHOptions> CurrentAsync();

	/// <summary><paramref name="property"/>'s stored value, as <see cref="Format"/> writes it.</summary>
	ValueTask<string?> CurrentTextAsync(string property);

	/// <summary>
	/// The options <see cref="SetAsync"/> would put in force, or why the value is not one the option takes: outside
	/// its range (which <see cref="SetAsync"/> would clamp) or refused by the validators. Stores nothing.
	/// </summary>
	ValueTask<Result<SharpMUSHOptions>> PreviewAsync(string property, object? value);

	/// <summary>
	/// Sets <paramref name="property"/> to <paramref name="value"/>, already parsed. Returns the options now in
	/// force, or what the validators refused.
	/// </summary>
	ValueTask<Result<SharpMUSHOptions>> SetAsync(string property, object? value);
}
