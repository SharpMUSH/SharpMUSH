using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;

namespace SharpMUSH.Client.Services;

/// <summary>
/// How the portal names a scope to a player: the built-in ones from the resources, a permission category's by the
/// category. <see cref="DelegatedScopes.Profile"/> is left out of what a player reads, since every grant has it and the
/// page already names the character.
/// </summary>
public static class OAuthScopeText
{
	public static IEnumerable<string> Shown(IEnumerable<string> scopes) => scopes.Where(scope => scope != DelegatedScopes.Profile);

	public static string Name(IStringLocalizer<SharedResource> loc, string scope, IReadOnlyList<OAuthScopeView> known) => scope switch
	{
		DelegatedScopes.Commands => loc["OAuthScopeCommands"],
		DelegatedScopes.Content => loc["OAuthScopeContent"],
		DelegatedScopes.Build => loc["OAuthScopeBuild"],
		DelegatedScopes.Manage => loc["OAuthScopeManage"],
		DelegatedScopes.Game => loc["OAuthScopeGame"],
		_ => known.FirstOrDefault(view => view.Scope == scope)?.Category is { } category ? loc["OAuthScopeCategory", category] : scope,
	};

	public static string Detail(IStringLocalizer<SharedResource> loc, string scope, IReadOnlyList<OAuthScopeView> known) => scope switch
	{
		DelegatedScopes.Commands => loc["OAuthScopeCommandsDetail"],
		DelegatedScopes.Content => loc["OAuthScopeContentDetail"],
		DelegatedScopes.Build => loc["OAuthScopeBuildDetail"],
		DelegatedScopes.Manage => loc["OAuthScopeManageDetail"],
		DelegatedScopes.Game => loc["OAuthScopeGameDetail"],
		_ => known.FirstOrDefault(view => view.Scope == scope)?.Description ?? string.Empty,
	};
}
