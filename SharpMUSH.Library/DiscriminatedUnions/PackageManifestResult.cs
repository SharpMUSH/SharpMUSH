using System.Diagnostics.CodeAnalysis;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A parsed package document, or the issues that stopped it parsing.
/// </summary>
public partial union PackageManifestResult<T>(T, PackageManifestFailure)
{
	/// <summary>
	/// True with the value, or false with the issues: a caller can pass the failure straight back and go on
	/// with the value, with no cast in between.
	/// </summary>
	public bool TryGetValue([MaybeNullWhen(false)] out T value, [MaybeNullWhen(true)] out PackageManifestFailure failure)
	{
		switch (Value)
		{
			case T success:
				value = success;
				failure = default;
				return true;
			case PackageManifestFailure held:
				value = default;
				failure = held;
				return false;
			default:
				throw new InvalidOperationException($"A default {nameof(PackageManifestResult<T>)} holds neither case.");
		}
	}
}
