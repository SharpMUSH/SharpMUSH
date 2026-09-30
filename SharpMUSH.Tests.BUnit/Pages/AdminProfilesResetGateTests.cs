using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Answers the profile-handler status with a configured, present handler, and refuses the reset the
/// way the server does when the installed version is not the one this build ships.
/// </summary>
file sealed class HandlerStatusHandler : HttpMessageHandler
{
	public const string Refusal = "Reset restores profile-handler v1.0.0, which is not the installed version";

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> Task.FromResult(Respond(request));

	/// <summary>The response is the caller's to dispose; <see cref="HttpClient"/> hands it on.</summary>
	private static HttpResponseMessage Respond(HttpRequestMessage request) =>
		(request.Method.Method, request.RequestUri!.AbsolutePath.TrimStart('/')) switch
		{
			("GET", "api/admin/profile-handler") => new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					configured = true,
					handlerDbref = 8,
					handlerName = "HTTP Handler",
					handlerExists = true,
					attributes = new[] { new { attribute = "GET`PROFILE", present = true } }
				})
			},
			("POST", "api/admin/profile-handler/reset") => new HttpResponseMessage(HttpStatusCode.Conflict)
			{
				Content = JsonContent.Create(new { error = Refusal })
			},
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
}

/// <summary>
/// The page is open to <c>players.moderate</c>, but its reset rewrites the handler's wizard softcode
/// and the server takes <c>packages.admin</c> for it. A moderator without that permission must not
/// be offered a button whose only possible answer is 403.
/// </summary>
public class AdminProfilesResetGateTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	public AdminProfilesResetGateTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		var api = Track(new HttpClient(new HandlerStatusHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		Services.AddSingleton(factory);

		Auth = AddAuthorization();
		Auth.SetAuthorized("staff");
	}

	private IRenderedComponent<AdminProfiles> RenderLoaded()
	{
		var cut = Render<AdminProfiles>();
		cut.WaitForAssertion(() => cut.Find(".ph-status"));
		return cut;
	}

	[Test]
	public async Task AModeratorWithoutPackagesAdmin_SeesTheStatusButNoResetButton()
	{
		Auth.SetPolicies("players.moderate");

		var cut = RenderLoaded();

		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("ResetToDefaults"))).IsFalse();
		await Assert.That(cut.Markup).Contains("AdmProfilesResetNeedsPackagesAdmin");
	}

	[Test]
	public async Task APackagesAdmin_IsOfferedTheReset()
	{
		Auth.SetPolicies("players.moderate", "packages.admin");

		var cut = RenderLoaded();

		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Contains("ResetToDefaults"))).IsTrue();
		await Assert.That(cut.Markup).DoesNotContain("AdmProfilesResetNeedsPackagesAdmin");
	}

	/// <summary>A refused reset says why, not only its status code.</summary>
	[Test]
	public async Task ARefusedReset_ShowsTheServersReason()
	{
		var snackbar = Substitute.For<ISnackbar>();
		Services.AddSingleton(snackbar);
		Auth.SetPolicies("players.moderate", "packages.admin");
		var cut = RenderLoaded();

		cut.FindAll("button").First(b => b.TextContent.Contains("ResetToDefaults")).Click();

		cut.WaitForAssertion(() => snackbar.Received().Add(
			Arg.Is<string>(m => m.Contains("AdmProfilesResetFailedError") && m.Contains(HandlerStatusHandler.Refusal)),
			Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>()), TimeSpan.FromSeconds(5));
		await Assert.That(snackbar.ReceivedCalls()).IsNotEmpty();
	}
}
