using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Phase 9 focused tests for the general <see cref="IEndpointContributor"/> plugin web-contribution seam.
/// A contributor's <see cref="IEndpointContributor.MapEndpoints"/> must be invoked by the host with the
/// real <see cref="IEndpointRouteBuilder"/> and the route it maps must become a live, routable endpoint —
/// the exact mechanism <c>Program.ConfigureApp</c> uses to let the Scene plugin map its <c>SceneHub</c>.
/// </summary>
public class EndpointContributorTests
{
	/// <summary>A minimal general contributor that maps one GET route — the seam under test is fully generic.</summary>
	private sealed class ProbeEndpointContributor : IEndpointContributor
	{
		public const string Route = "/__probe/endpoint-contributor";
		public bool WasInvoked { get; private set; }

		public void MapEndpoints(IEndpointRouteBuilder endpoints)
		{
			WasInvoked = true;
			endpoints.MapGet(Route, () => Results.Ok("contributed"));
		}
	}

	[Test]
	public async Task MapEndpoints_IsInvoked_AndMapsARoutableEndpoint()
	{
		// Build a real minimal host so we have a genuine IEndpointRouteBuilder (WebApplication), exactly the
		// type Program.ConfigureApp passes to each contributor.
		var builder = WebApplication.CreateBuilder();
		await using var app = builder.Build();

		var contributor = new ProbeEndpointContributor();

		// The host's invocation pattern (mirrors the foreach in Program.ConfigureApp).
		contributor.MapEndpoints(app);

		await Assert.That(contributor.WasInvoked).IsTrue();

		// The mapped route must be present as a live endpoint with the contributor's pattern.
		var dataSource = ((IEndpointRouteBuilder)app).DataSources
			.SelectMany(ds => ds.Endpoints)
			.OfType<RouteEndpoint>()
			.ToList();

		await Assert.That(dataSource.Any(e =>
			string.Equals(e.RoutePattern.RawText, ProbeEndpointContributor.Route, StringComparison.Ordinal)))
			.IsTrue()
			.Because("the contributor's MapGet route must become a routable endpoint on the host pipeline");
	}
}
