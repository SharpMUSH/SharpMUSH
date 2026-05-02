// BCL stub types required by the C# 14 'union' declaration feature.
// Per the language spec resolution: "users should provide them explicitly,
// either by referencing assemblies or defining them locally."
// These will be superseded by the official BCL types in a later .NET preview.

namespace System.Runtime.CompilerServices
{
	[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
	public sealed class UnionAttribute : Attribute { }

	public interface IUnion
	{
		object? Value { get; }
	}
}
