using System.Reflection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests;

/// <summary>
/// The registered function and command surface, read straight off the <c>[SharpFunction]</c> and
/// <c>[SharpCommand]</c> attributes the source generators build the libraries from.
///
/// <para>Anything that wants to ask "what does this game actually expose" — help parity, coverage
/// inventories, signature checks — asks here rather than re-reflecting. The reflection is not free
/// (it walks every type in the implementation assembly), so both inventories are computed once.</para>
/// </summary>
public static class RegistryInventory
{
	/// <summary>A registered name together with the attribute that declared it.</summary>
	/// <param name="Name">The name as the attribute spells it.</param>
	/// <param name="DeclaringType">The type holding the implementing method.</param>
	/// <param name="MethodName">The implementing method, for error messages that point somewhere.</param>
	public readonly record struct FunctionEntry(
		string Name,
		SharpFunctionAttribute Attribute,
		Type DeclaringType,
		string MethodName)
	{
		public string Origin => $"{DeclaringType.Name}.{MethodName}";
	}

	/// <inheritdoc cref="FunctionEntry"/>
	public readonly record struct CommandEntry(
		string Name,
		SharpCommandAttribute Attribute,
		Type DeclaringType,
		string MethodName)
	{
		public string Origin => $"{DeclaringType.Name}.{MethodName}";
	}

	private static readonly Lazy<IReadOnlyList<FunctionEntry>> LazyFunctions =
		new(() => Attributed<SharpFunctionAttribute, FunctionEntry>((a, t, m) => new FunctionEntry(a.Name, a, t, m)));

	private static readonly Lazy<IReadOnlyList<CommandEntry>> LazyCommands =
		new(() => Attributed<SharpCommandAttribute, CommandEntry>((a, t, m) => new CommandEntry(a.Name, a, t, m)));

	/// <summary>Every <c>[SharpFunction]</c> in the implementation assembly.</summary>
	public static IReadOnlyList<FunctionEntry> Functions => LazyFunctions.Value;

	/// <summary>Every <c>[SharpCommand]</c> in the implementation assembly.</summary>
	public static IReadOnlyList<CommandEntry> Commands => LazyCommands.Value;

	/// <summary>
	/// Every name a caller can type to reach a function: the registered names plus the configured
	/// aliases. A name-coverage check that skips the aliases passes while <c>u()</c> is undocumented.
	/// </summary>
	public static IReadOnlySet<string> FunctionNamesWithAliases() =>
		WithAliases(Functions.Select(f => f.Name), Configurable.FunctionAliases);

	/// <inheritdoc cref="FunctionNamesWithAliases"/>
	public static IReadOnlySet<string> CommandNamesWithAliases() =>
		WithAliases(Commands.Select(c => c.Name), Configurable.CommandAliases);

	private static IReadOnlySet<string> WithAliases(IEnumerable<string> names, Dictionary<string, string[]> aliases)
	{
		var all = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
		foreach (var alias in aliases.Values.SelectMany(a => a))
			all.Add(alias);
		return all;
	}

	private static List<TEntry> Attributed<TAttribute, TEntry>(Func<TAttribute, Type, string, TEntry> project)
		where TAttribute : Attribute
	{
		const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

		var entries = new List<TEntry>();
		foreach (var type in typeof(SharpMUSH.Implementation.Functions.Functions).Assembly.GetTypes())
			foreach (var method in type.GetMethods(All))
				foreach (var attribute in method.GetCustomAttributes<TAttribute>())
					entries.Add(project(attribute, type, method.Name));

		return entries;
	}
}
