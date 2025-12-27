using Bunit;
using Bunit.TestDoubles;
using TUnit.Core;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the LoginDisplay component to verify authorization state behavior.
/// </summary>
public class LoginDisplayTests : Bunit.TestContext
{
	[Test]
	public async Task LoginDisplay_WhenNotAuthenticated_ShowsLoginButton()
	{
		// Arrange
		this.AddTestAuthorization();

		// Act
		var cut = RenderComponent<LoginDisplay>();

		// Assert
		var loginButton = cut.Find("a[href='authentication/login']");
		await Assert.That(loginButton).IsNotNull();
		await Assert.That(loginButton.TextContent).Contains("Login");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsUsername()
	{
		// Arrange
		var authContext = this.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");

		// Act
		var cut = RenderComponent<LoginDisplay>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("TestUser");
	}

	[Test]
	public async Task LoginDisplay_WhenAuthenticated_ShowsLogoutButton()
	{
		// Arrange
		var authContext = this.AddTestAuthorization();
		authContext.SetAuthorized("TestUser");

		// Act
		var cut = RenderComponent<LoginDisplay>();

		// Assert
		var logoutButton = cut.Find("button");
		await Assert.That(logoutButton).IsNotNull();
	}

	[Test]
	public async Task LoginDisplay_WhenNotAuthenticated_DoesNotShowUsername()
	{
		// Arrange
		this.AddTestAuthorization();

		// Act
		var cut = RenderComponent<LoginDisplay>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).DoesNotContain("TestUser");
	}
}
