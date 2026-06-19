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
public record UserDefinedFunction(
	string Name,
	DBRef Object,
	string Attribute,
	int MinArgs,
	int MaxArgs,
	bool Enabled,
	string? AliasOf);

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

	/// <summary>All registered entries, including aliases and disabled ones.</summary>
	IReadOnlyCollection<UserDefinedFunction> All();
}
