using Microsoft.Extensions.Localization;
using MudBlazor;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// Serves MudBlazor's own text (pager labels, picker buttons, screen-reader labels, converter errors)
/// from <see cref="SharedResource"/>, so the components speak the portal's language.
/// </summary>
/// <remarks>
/// MudBlazor asks for its keys by their own names (<c>MudDataGridPager_RowsPerPage</c>), and the resx
/// holds them under those names. A key the resx lacks comes back as itself with
/// <see cref="LocalizedString.ResourceNotFound"/> set, which is what MudBlazor's default does too.
/// </remarks>
public sealed class PortalMudLocalizationInterceptor(IStringLocalizer<SharedResource> localizer) : ILocalizationInterceptor
{
	public LocalizedString Handle(string key, params object[] arguments) => localizer[key, arguments];
}
