using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// An in-memory entry describing a global user-defined function registered via <c>@function</c>.
/// </summary>
/// <param name="Name">The function name, lower-cased (the lookup key).</param>
/// <param name="Object">The object whose attribute is evaluated as softcode.</param>
/// <param name="Attribute">The attribute name on <paramref name="Object"/> evaluated with <c>%0..</c> = call args.</param>
/// <param name="MinArgs">Minimum number of arguments the function accepts.</param>
/// <param name="MaxArgs">Maximum number of arguments the function accepts.</param>
/// <param name="Enabled">Whether the function is currently callable (toggled by /ENABLE and /DISABLE).</param>
/// <param name="AliasOf">
/// When non-null, this entry is an alias created via <c>@function/alias</c>; the value is the
/// lower-cased name of the user function this alias resolves the object/attribute through.
/// </param>
/// <param name="Restriction">
/// Optional permission restriction set via <c>@function/restrict</c> (e.g. <c>wizard</c>,
/// <c>admin</c>, <c>god</c>, <c>nobody</c>, or a space-separated list with optional <c>!</c>
/// negation). When non-null and the caller fails the check, the function returns the standard
/// permission error instead of evaluating.
/// </param>
/// <param name="Preserved">
/// When <c>true</c>, the entry was marked via <c>@function/preserve</c> so it survives a bulk
/// reset (<c>@function/restore</c> with no argument) and is reported for re-registration.
/// </param>
public record UserDefinedFunction(
	string Name,
	DBRef Object,
	string Attribute,
	int MinArgs,
	int MaxArgs,
	bool Enabled,
	string? AliasOf,
	string? Restriction = null,
	bool Preserved = false);

/// <summary>
/// In-memory registry of global user-defined functions (<c>@function</c>).
///
/// <para>
/// Intentionally NOT persisted: durability comes from re-running <c>@function</c> on boot via the
/// <c>@STARTUP</c> attribute pass, so there is no database collection or migration. The registry is
/// a process-lifetime singleton; entries are re-established each server start.
/// </para>
///
/// <para>Built-in functions always take precedence — the parser only consults this registry on a
/// FunctionLibrary miss.</para>
/// </summary>
public interface IUserDefinedFunctionService
{
	/// <summary>Registers (or overwrites) a global user-defined function.</summary>
	void Define(UserDefinedFunction function);

	/// <summary>
	/// Resolves a function by name (case-insensitive), following an alias to its target.
	/// Returns <c>null</c> when no enabled, resolvable entry exists for <paramref name="name"/>.
	/// </summary>
	/// <remarks>
	/// The returned entry carries the resolved <see cref="UserDefinedFunction.Object"/> /
	/// <see cref="UserDefinedFunction.Attribute"/> / arg-count bounds to evaluate, while
	/// <see cref="UserDefinedFunction.Name"/> remains the requested name.
	/// </remarks>
	UserDefinedFunction? Resolve(string name);

	/// <summary>Returns the raw entry for a name (case-insensitive) without following aliases.</summary>
	UserDefinedFunction? Get(string name);

	/// <summary>Removes a function (or alias) by name. Returns whether an entry was removed.</summary>
	bool Delete(string name);

	/// <summary>Enables or disables a function by name. Returns whether an entry was found.</summary>
	bool SetEnabled(string name, bool enabled);

	/// <summary>Registers <paramref name="alias"/> as another name for the existing function <paramref name="target"/>.</summary>
	/// <returns><c>true</c> if <paramref name="target"/> exists and the alias was created.</returns>
	bool Alias(string alias, string target);

	/// <summary>
	/// Sets (or clears, when <paramref name="restriction"/> is <c>null</c>/empty) the permission
	/// restriction on an existing user function. Returns whether an entry was found.
	/// </summary>
	bool SetRestriction(string name, string? restriction);

	/// <summary>
	/// Creates <paramref name="newName"/> as an independent copy of the existing user function
	/// <paramref name="existing"/> (object/attribute/bounds copied; restriction and preserve flags
	/// reset). The clone can be restricted, disabled, or deleted without touching the original.
	/// </summary>
	/// <returns><c>true</c> if <paramref name="existing"/> resolves and the clone was created.</returns>
	bool Clone(string newName, string existing);

	/// <summary>Marks or unmarks a user function as preserved (survives <see cref="ResetUnpreserved"/>).</summary>
	/// <returns>Whether an entry was found.</returns>
	bool SetPreserved(string name, bool preserved);

	/// <summary>
	/// Removes every entry that is NOT marked <see cref="UserDefinedFunction.Preserved"/>.
	/// Implements <c>@function/restore</c> (no argument): preserved entries survive a bulk reset.
	/// </summary>
	/// <returns>The number of entries removed.</returns>
	int ResetUnpreserved();

	/// <summary>
	/// Sets (or clears, when <paramref name="restriction"/> is <c>null</c>/empty) a restriction
	/// overlay for a BUILT-IN function name. The overlay is consulted at call time so a built-in
	/// (or a "deleted" built-in) can be restricted without a backing registry entry.
	/// </summary>
	void SetBuiltinRestriction(string name, string? restriction);

	/// <summary>Returns the restriction overlay for a built-in name, or <c>null</c> if none.</summary>
	string? GetBuiltinRestriction(string name);

	/// <summary>All registered entries, including aliases and disabled ones.</summary>
	IReadOnlyCollection<UserDefinedFunction> All();
}
