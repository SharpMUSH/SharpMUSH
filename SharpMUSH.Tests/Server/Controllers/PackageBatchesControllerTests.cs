using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.Server.Controllers;

public class PackageBatchesControllerTests
{
	[ClassDataSource<PackageBatchAccountSessionFactory>(Shared = SharedType.PerTestSession)]
	public required PackageBatchAccountSessionFactory Factory { get; init; }

	[Test]
	public async Task AnonymousCannotPlanApplyOrReadBatchProgress()
	{
		using var client = Factory.CreateHttpClient();
		using var plan = await client.PostAsJsonAsync("api/packages/batches/plan", new PackageBatchPlanRequest([]));
		using var apply = await client.PostAsJsonAsync("api/packages/batches/apply", new PackageBatchApplyRequest("unknown"));
		using var progress = await client.GetAsync("api/packages/batches/unknown");
		await Assert.That(plan.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(apply.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(progress.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
	}
}

public sealed class PackageBatchAccountSessionFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	protected override Action<IServiceCollection>? ConfigureTestServices => services =>
		services.PostConfigure<AuthenticationOptions>(options =>
		{
			options.DefaultScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultAuthenticateScheme = AccountSessionAuthenticationHandler.SchemeName;
			options.DefaultChallengeScheme = AccountSessionAuthenticationHandler.SchemeName;
		});
}
