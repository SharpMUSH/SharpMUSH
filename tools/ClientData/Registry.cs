using System.Reflection;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Tools.ClientData;

/// <summary>
/// The registered functions and commands, read off the <c>[SharpFunction]</c> and
/// <c>[SharpCommand]</c> attributes in the implementation assembly.
/// </summary>
public static class Registry
{
	private static readonly Lazy<IReadOnlyList<SharpFunctionAttribute>> LazyFunctions = new(Attributed<SharpFunctionAttribute>);
	private static readonly Lazy<IReadOnlyList<SharpCommandAttribute>> LazyCommands = new(Attributed<SharpCommandAttribute>);

	public static IReadOnlyList<SharpFunctionAttribute> Functions => LazyFunctions.Value;

	public static IReadOnlyList<SharpCommandAttribute> Commands => LazyCommands.Value;

	private static IReadOnlyList<TAttribute> Attributed<TAttribute>() where TAttribute : Attribute
	{
		const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

		return typeof(SharpMUSH.Implementation.Functions.Functions).Assembly.GetTypes()
			.SelectMany(type => type.GetMethods(All))
			.SelectMany(method => method.GetCustomAttributes<TAttribute>())
			.ToList();
	}
}
