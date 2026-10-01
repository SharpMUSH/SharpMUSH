using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Models.Applications;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// The minimum-role gate every renderer of placed widgets applies (<c>ZoneRenderer</c>, and the Play
/// page's aside and Room sheet). A layout is one arrangement for everyone, so an application placed in it
/// is gated where it renders: below its minimum role it is left out (the server still gates its data
/// routes). One per renderer, since it keeps what it fetched.
/// </summary>
/// <remarks>
/// <para>A registered <see cref="ApplicationPortalWidget"/> carries its role. The registry's by-slug
/// fallback, <see cref="UnresolvedApplicationWidget"/>, does not: until <see cref="ResolveAsync"/> has found
/// its application it is not admitted, and one with no application (or whose fetch failed) never is.</para>
/// <para>The application fetched for the check is kept and handed to the widget (<see cref="Fetched"/>), so
/// the widget renders the answer the gate used rather than fetching the same slug again.</para>
/// <para>The catalog and registry client are asked for only when there is something to resolve. A host that
/// gets that far renders <c>SchemaWidget</c>, which injects both; injecting them here would make every host
/// of a zone register them whether or not it places an application.</para>
/// </remarks>
public sealed class ApplicationWidgetGate(IWidgetRegistry? registry, IServiceProvider services)
{
	private readonly Dictionary<string, PortalApplication> _fetched = new(StringComparer.Ordinal);

	/// <summary>Whether the placement named <paramref name="name"/> renders for <paramref name="role"/>, on what is known now.</summary>
	public bool Admits(string name, PortalRole role) => registry?.GetWidget(name) switch
	{
		ApplicationPortalWidget app => role >= app.MinimumRole,
		UnresolvedApplicationWidget => _fetched.TryGetValue(name, out var app) && role >= app.MinimumRoleEnum,
		_ => true
	};

	/// <summary>The names among <paramref name="names"/> whose application is not known yet.</summary>
	public IReadOnlyList<string> Unresolved(IEnumerable<string> names) =>
		names
			.Where(name => registry?.GetWidget(name) is UnresolvedApplicationWidget && !_fetched.ContainsKey(name))
			.Distinct(StringComparer.Ordinal)
			.ToList();

	/// <summary>
	/// Finds the applications behind <paramref name="names"/> the way <c>SchemaWidget</c> does: the catalog once
	/// it is in, else a fetch by slug, all together. False when there was nothing to resolve.
	/// </summary>
	public async Task<bool> ResolveAsync(IEnumerable<string> names)
	{
		var slugs = Unresolved(names);
		if (slugs.Count == 0)
		{
			return false;
		}

		var catalog = services.GetRequiredService<ApplicationCatalog>();
		var registryClient = services.GetRequiredService<ApplicationRegistryClient>();
		await catalog.Loaded;
		var found = await Task.WhenAll(slugs.Select(async slug =>
			(Slug: slug, App: catalog.Get(slug) ?? await registryClient.GetAsync(slug))));
		foreach (var (slug, app) in found.Where(f => f.App is not null))
		{
			_fetched[slug] = app!;
		}

		return true;
	}

	/// <summary>The application fetched for <paramref name="name"/>'s check, if one was.</summary>
	public PortalApplication? Fetched(string name) => _fetched.GetValueOrDefault(name);

	/// <summary>
	/// The parameters a renderer passes a placed widget. <c>SchemaWidget</c> (which application widgets render
	/// through) also gets its slug, and the application the gate fetched for it when there is one.
	/// </summary>
	public Dictionary<string, object> Parameters(string name, IPortalWidget widget, object? config, string zone)
	{
		var parameters = new Dictionary<string, object> { ["Config"] = config!, ["Zone"] = zone };
		if (widget.ComponentType == typeof(Components.Widgets.SchemaWidget))
		{
			parameters["WidgetName"] = name;
			if (Fetched(name) is { } application)
			{
				parameters["Application"] = application;
			}
		}

		return parameters;
	}
}
