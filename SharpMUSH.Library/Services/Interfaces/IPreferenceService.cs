using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The preference groups installed packages declare (<c>preferences:</c>) and the portal's own
/// (<see cref="BuiltInPreferences"/>, under the package id <c>portal</c>), and each player's values in them. A value is
/// an attribute, <c>PREF`&lt;PACKAGE&gt;`&lt;GROUP&gt;`&lt;KEY&gt;</c>, on the account's holder (<c>#acct:&lt;key&gt;</c>)
/// or on the character, as the group's scope says; softcode reads it with <c>get()</c>. What a write
/// is checked against is the declaration; who may write is the caller's business, since every caller writes only the
/// account or character it acts as.
/// </summary>
public interface IPreferenceService
{
	/// <summary>Every group the installed packages and the portal declare, ordered by id.</summary>
	ValueTask<IReadOnlyList<PreferenceGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

	/// <summary>The group <paramref name="groupId"/> (<c>&lt;package&gt;.&lt;group&gt;</c>, read without case), when an installed package or the portal declares it.</summary>
	ValueTask<Found<PreferenceGroup>> FindGroupAsync(string groupId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Where <paramref name="group"/>'s values are kept for someone: the account's holder for an account group, the
	/// character for a character group. An account group needs <paramref name="accountId"/> or a character on an
	/// account; a character group needs <paramref name="character"/>.
	/// </summary>
	ValueTask<Result<DBRef>> HolderAsync(PreferenceGroup group, string? accountId, SharpPlayer? character,
		CancellationToken cancellationToken = default);

	/// <summary>Each preference in <paramref name="group"/> as <paramref name="holder"/> has it, in declaration order.</summary>
	ValueTask<IReadOnlyList<PreferenceReading>> ReadAsync(PreferenceGroup group, DBRef holder,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Writes <paramref name="values"/> (key to value, keys read without case) to <paramref name="holder"/>. Every
	/// value is checked first: an unknown key, a refused value or a protected one writes nothing and says why. Answers the group as it then stands.
	/// </summary>
	ValueTask<Result<IReadOnlyList<PreferenceReading>>> WriteAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default);

	/// <summary>
	/// <see cref="WriteAsync(PreferenceGroup, DBRef, IReadOnlyDictionary{string, string}, CancellationToken)"/>, with
	/// the version of the shape each JSON value in <paramref name="values"/> was written to, by key: a value read at an
	/// older version and changed without being moved to the current shape keeps the version it has, so its reader still
	/// moves it. A key left out is written at its declaration's version; a version above the declaration's, or below 1,
	/// is refused.
	/// </summary>
	ValueTask<Result<IReadOnlyList<PreferenceReading>>> WriteAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, int> versions,
		CancellationToken cancellationToken = default)
		=> WriteAsync(group, holder, values, cancellationToken);

	/// <summary>
	/// Returns <paramref name="keys"/> (every key when empty) to their defaults on <paramref name="holder"/>, by removing
	/// the stored values. An unknown or protected key resets nothing and says so. Answers the group as it then stands.
	/// </summary>
	ValueTask<Result<IReadOnlyList<PreferenceReading>>> ResetAsync(PreferenceGroup group, DBRef holder,
		IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}

/// <summary>A preference group an installed package, or the portal (<see cref="BuiltInPreferences"/>), declares.</summary>
/// <param name="PackageId">The package that declares it.</param>
/// <param name="Spec">The declaration.</param>
public sealed record PreferenceGroup(string PackageId, PackagePreferenceGroupSpec Spec)
{
	/// <summary>The group's id, <c>&lt;package&gt;.&lt;group&gt;</c>.</summary>
	public string Id => Preferences.GroupId(PackageId, Spec.Name);

	/// <summary>The attribute a preference of the group is kept in.</summary>
	public string[] AttributePath(PackagePreferenceSpec key) => Preferences.AttributePath(PackageId, Spec.Name, key.Key);
}

/// <summary>One preference as a player has it.</summary>
/// <param name="Spec">The declaration.</param>
/// <param name="Value">The value in effect: the stored one, or the default when none is stored or the stored one is no longer allowed.</param>
/// <param name="IsSet">Whether a value the declaration allows is stored, rather than the default being in effect.</param>
public sealed record PreferenceReading(PackagePreferenceSpec Spec, string Value, bool IsSet)
{
	/// <summary>
	/// The version of the shape <see cref="Value"/> was written to (<see cref="PackagePreferenceSpec.Version"/>): the
	/// stored value's, or the declaration's own for the default. Lower than the declaration's when the value was
	/// written before the package changed its shape, so the reader moves it to the current one.
	/// </summary>
	public int Version { get; init; } = 1;
}
