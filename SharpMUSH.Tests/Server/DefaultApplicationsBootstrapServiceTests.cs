using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// Unit tests for <see cref="DefaultApplicationsBootstrapService"/>: it seeds the character-header
/// Widget application on a fresh registry, and leaves an existing one untouched (idempotent).
/// </summary>
public class DefaultApplicationsBootstrapServiceTests
{
	private const string Slug = DefaultApplicationsBootstrapService.CharacterHeaderSlug;

	[Test]
	public async Task Seeds_CharacterHeader_WhenAbsent()
	{
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationAsync(Slug)
			.Returns(Task.FromResult<OneOf<RegisteredApplication, NotFound>>(new NotFound()));

		var svc = new DefaultApplicationsBootstrapService(registry, NullLogger<DefaultApplicationsBootstrapService>.Instance);
		await svc.StartAsync(CancellationToken.None);

		await registry.Received(1).UpsertApplicationAsync(Arg.Is<RegisteredApplication>(a =>
			a.Slug == Slug
			&& a.Kind == ApplicationKind.Widget
			&& a.SchemaUrl == "http/profile/schema"
			&& a.DataUrl == "http/profile?objid={objid}"
			&& a.Zones != null && a.Zones.Contains(WidgetZone.MainContent)));
	}

	[Test]
	public async Task DoesNotReseed_WhenPresent()
	{
		var existing = new RegisteredApplication(
			Slug, "Character Header", null, ApplicationKind.Widget,
			"http/profile/schema", null, null, PortalRole.Guest, null, [WidgetZone.MainContent], 0);

		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationAsync(Slug)
			.Returns(Task.FromResult<OneOf<RegisteredApplication, NotFound>>(existing));

		var svc = new DefaultApplicationsBootstrapService(registry, NullLogger<DefaultApplicationsBootstrapService>.Instance);
		await svc.StartAsync(CancellationToken.None);

		await registry.DidNotReceive().UpsertApplicationAsync(Arg.Any<RegisteredApplication>());
	}
}
