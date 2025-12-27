using Bunit;
using Bunit.TestDoubles;
using TUnit.Core;
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
		// Arrange
		using var ctx = new Bunit.TestContext();
		ctx.AddTestAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();

		// Act
		var cut = ctx.RenderComponent<LoginDisplay>();

		// Assert
		var loginButton = cut.Find("a[href='authentication/login']");
		await Assert.That(loginButton).IsNotNull();
		await Assert.That(loginButton.TextContent).Contains("Login");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsUsername()
	{
		// Arrange
		using var ctx = new Bunit.TestContext();
		var authContext = ctx.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();

		// Act
		var cut = ctx.RenderComponent<LoginDisplay>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("TestUser");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsLogoutButton()
	{
		// Arrange
		using var ctx = new Bunit.TestContext();
		var authContext = ctx.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();

		// Act
		var cut = ctx.RenderComponent<LoginDisplay>();

		// Assert
		var logoutButton = cut.Find("button");
		await Assert.That(logoutButton).IsNotNull();
	}

	[Test]
	public async Task LoginDisplay_WhenNotAuthenticated_DoesNotShowUsername()
	{
		// Arrange
		using var ctx = new Bunit.TestContext();
		ctx.AddTestAuthorization();
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		ctx.Services.AddMudServices();

		// Act
		var cut = ctx.RenderComponent<LoginDisplay>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).DoesNotContain("TestUser");
	}
}
