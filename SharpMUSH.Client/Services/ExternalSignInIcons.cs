using MudBlazor;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Services;

/// <summary>The icon a sign-in service's button and links carry.</summary>
public static class ExternalSignInIcons
{
	public static string For(string? kind) => kind switch
	{
		ExternalSignInKinds.Discord => Icons.Custom.Brands.Discord,
		ExternalSignInKinds.Google => Icons.Custom.Brands.Google,
		ExternalSignInKinds.GitHub => Icons.Custom.Brands.GitHub,
		ExternalSignInKinds.Microsoft => Icons.Custom.Brands.Microsoft,
		_ => Icons.Material.Outlined.Login
	};
}
