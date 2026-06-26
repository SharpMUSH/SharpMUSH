using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the LoginDisplay component to verify authorization state behavior.
/// </summary>
public class LoginDisplayTests
{
	[Test]
	public async Task LoginDisplay_WhenNotAuthenticated_ShowsLoginButton()
	{
		await using var ctx = new BunitContext();
		ctx.AddAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		var cut = ctx.Render<LoginDisplay>();

		var loginButton = cut.Find("a[href='authentication/login']");
		await Assert.That(loginButton).IsNotNull();
		await Assert.That(loginButton.TextContent).Contains("Login");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsUsername()
	{
		await using var ctx = new BunitContext();
		var authContext = ctx.AddAuthorization();
		authContext.SetAuthorized("TestUser");
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		var cut = ctx.Render<LoginDisplay>();

		var markup = cut.Markup;
		await Assert.That(markup).Contains("TestUser");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsLogoutButton()
	{
		await using var ctx = new BunitContext();
		var authContext = ctx.AddAuthorization();
		authContext.SetAuthorized("TestUser");
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		var cut = ctx.Render<LoginDisplay>();

		var logoutButton = cut.Find("button");
		await Assert.That(logoutButton).IsNotNull();
	}

	[Test]
	public async Task LoginDisplay_WhenNotAuthenticated_DoesNotShowUsername()
	{
		await using var ctx = new BunitContext();
		ctx.AddAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();
		ctx.Services.AddLocalization();

		var cut = ctx.Render<LoginDisplay>();

		var markup = cut.Markup;
		await Assert.That(markup).DoesNotContain("TestUser");
	}
}
