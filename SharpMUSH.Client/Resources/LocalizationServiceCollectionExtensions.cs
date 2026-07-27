using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// Registers portal localization. Extracted from <c>Program.cs</c> into a real production method so
/// the registration shape itself can be exercised by tests directly, rather than re-typed inside a
/// test fixture where it could silently drift from what actually runs.
/// </summary>
public static class LocalizationServiceCollectionExtensions
{
	/// <summary>
	/// Registers <see cref="Microsoft.Extensions.Localization.IStringLocalizer{T}"/> for
	/// <see cref="SharedResource"/>.
	/// <para>
	/// <c>ResourcesPath</c> must stay unset. It re-roots the lookup as
	/// <c>{RootNamespace}.{ResourcesPath}.{TypeFullName minus RootNamespace}</c>, and
	/// <see cref="SharedResource"/> already lives in the <c>Resources</c> namespace — setting it to
	/// <c>"Resources"</c> asks for <c>SharpMUSH.Client.Resources.Resources.SharedResource</c>, which
	/// does not exist. The lookup then silently falls back to echoing the key, so every multi-word
	/// label in the portal renders as its raw key.
	/// </para>
	/// </summary>
	public static IServiceCollection AddSharedResourceLocalization(this IServiceCollection services)
		=> services.AddLocalization();
}
