using System.Security.Claims;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Models.Portal.Setup;

namespace SharpMUSH.Client.Services.Navigation;

/// <summary>
/// The main navigation for whoever is signed in: the rail, the drawer, the phone tab bar and the command palette all
/// ask here, so none of them can list a destination another leaves out or put it somewhere else.
/// </summary>
public sealed class PortalNavigation(
	ApplicationRegistryClient registry,
	NavLayoutClient layouts,
	ServerInfoService serverInfo,
	IAccountAuthState account,
	ITerminalService terminal,
	IStringLocalizer<SharedResource> loc)
{
	/// <summary>Raised when staff in this tab saved or reset the layout: whatever draws the navigation asks again.</summary>
	public event Action? Changed
	{
		add => layouts.Changed += value;
		remove => layouts.Changed -= value;
	}

	/// <summary>The features a core destination's gate reads.</summary>
	private static readonly string[] CoreFeatures = [GameFeatures.Scenes, GameFeatures.WorldMap];

	public async Task<ResolvedNav> ResolveAsync(ClaimsPrincipal? user)
	{
		var apps = registry.ListAsync();
		var stored = layouts.GetAsync();
		var guests = serverInfo.GuestLoginsEnabledAsync();
		var features = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var feature in CoreFeatures)
		{
			if (await serverInfo.HasFeatureAsync(feature)) features.Add(feature);
		}

		var viewer = new NavViewer(
			PortalRoleHelper.CurrentRole(user),
			scope => PortalApplication.HoldsPermission(user, scope),
			account.IsLoggedIn,
			await guests,
			terminal.IsConnected,
			features.Contains);
		return NavResolver.Resolve(await stored is NavLayout layout ? layout : null, NavCatalog.All(await apps, Localize), viewer, Localize);
	}

	/// <summary>Every destination there is, whoever may open it: what the navigation editor arranges.</summary>
	public async Task<IReadOnlyList<NavDestination>> CatalogAsync() => NavCatalog.All(await registry.ListAsync(), Localize);

	/// <summary>The navigation <paramref name="viewer"/> would see under <paramref name="layout"/>: the editor's preview.</summary>
	public ResolvedNav Preview(NavLayout layout, IReadOnlyList<NavDestination> catalog, NavViewer viewer) =>
		NavResolver.Resolve(layout, catalog, viewer, Localize);

	/// <summary>A group's name as players read it.</summary>
	public string GroupLabel(string id, string? label) => NavResolver.Label(id, label, Localize);

	private string Localize(string key) => loc[key];
}
