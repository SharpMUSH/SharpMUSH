using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Echoes resource keys back instead of resolving them, so a component test asserts against a stable
/// key rather than English copy that a translator may reword. Tests that care what a label actually
/// says use <see cref="PortalLocalizer"/> instead.
/// </summary>
/// <remarks>
/// The indexed form renders <c>Key(arg1, arg2)</c> rather than <c>string.Format(key, args)</c>. Format
/// would drop every argument whenever the key itself contains no placeholder — which is always, since
/// the placeholders live in the resx value, not the key — silently voiding any assertion on an
/// interpolated value.
/// </remarks>
internal sealed class EchoLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);

	public LocalizedString this[string name, params object[] arguments]
		=> new(name, arguments.Length == 0 ? name : $"{name}({string.Join(", ", arguments)})");

	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal static class EchoLocalizerServiceCollectionExtensions
{
	public static IServiceCollection AddEchoLocalizer(this IServiceCollection services)
		=> services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
}
