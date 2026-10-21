using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Resources;

/// <summary>
/// The portal's <see cref="SharedResource"/> strings, with <c>AppTitle</c> answered by the game's own name
/// (<c>mud_name</c>, through <see cref="ServerInfoService.KnownGameName"/>). Every page title, the home and wiki
/// headers and the sign-in and setup pages name the game through that one key, so a game renamed in
/// Configuration or with <c>@config/set mud_name</c> is renamed everywhere. The resource's own value is
/// what shows until the server has answered.
/// </summary>
public sealed class GameNamedLocalizer(IStringLocalizerFactory factory, ServerInfoService serverInfo)
	: IStringLocalizer<SharedResource>
{
	public const string AppTitleKey = "AppTitle";

	private readonly IStringLocalizer _resources = factory.Create(typeof(SharedResource));

	public LocalizedString this[string name] =>
		name == AppTitleKey && serverInfo.KnownGameName is { } game ? new LocalizedString(name, game) : _resources[name];

	public LocalizedString this[string name, params object[] arguments] =>
		name == AppTitleKey ? this[name] : _resources[name, arguments];

	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
		=> _resources.GetAllStrings(includeParentCultures);
}
