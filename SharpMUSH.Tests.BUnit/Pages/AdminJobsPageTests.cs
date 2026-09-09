using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin.Jobs;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class AdminJobsPageTests : TrackingBunitContext
{
	private sealed class JobsHandler : HttpMessageHandler
	{
		public string? Bearer { get; private set; }
		public Uri? Uri { get; private set; }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Bearer = request.Headers.Authorization?.Parameter;
			Uri = request.RequestUri;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) });
		}
	}

	[Test]
	[Arguments(true, false)]
	[Arguments(true, true)]
	[Arguments(false, true)]
	public async Task JobsPageUsesConfiguredApiClientAndSessionBearer(bool own, bool manage)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		var authorization = this.AddAuthorization();
		authorization.SetAuthorized("staff");
		authorization.SetPolicies((own ? new[] { "jobs.manage.own" } : Array.Empty<string>()).Concat(manage ? new[] { "jobs.manage" } : []).ToArray());
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		var auth = Substitute.For<IAccountAuthState>();
		auth.InitAsync().Returns(Task.CompletedTask);
		auth.AccountSessionToken.Returns("current-session");
		var handler = new JobsHandler();
		Services.AddHttpClient("api", client => client.BaseAddress = new Uri("https://game.example/"))
			.AddHttpMessageHandler(() => new AccountSessionBearerHandler(auth))
			.ConfigurePrimaryHttpMessageHandler(() => handler);
		var page = Render<AdminJobs>();
		page.WaitForAssertion(() =>
		{
			if (handler.Uri is null) throw new InvalidOperationException("Jobs request did not use the API client.");
		});
		await Assert.That(handler.Uri!.AbsoluteUri).IsEqualTo("https://game.example/api/recurring-jobs?all=" + (!own && manage).ToString().ToLowerInvariant());
		await Assert.That(handler.Bearer).IsEqualTo("current-session");
		await Assert.That(page.Markup).Contains("JobsEmpty");
	}
}
