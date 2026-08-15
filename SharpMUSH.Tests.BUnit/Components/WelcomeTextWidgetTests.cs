using System.Text.Json;
using Bunit;
using Bunit.TestDoubles;
using SharpMUSH.Client.Components.Widgets;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// BUnit component tests for <see cref="WelcomeTextWidget"/>: markdown rendering and the
/// <c>showToGuests</c> gate, which hides the text from signed-out visitors when set to false.
/// </summary>
public abstract class WelcomeTextWidgetTestBase : BunitContext
{
	protected BunitAuthorizationContext Auth { get; }

	protected WelcomeTextWidgetTestBase()
	{
		Auth = AddAuthorization();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	protected static JsonElement BuildConfig(object obj)
		=> JsonSerializer.SerializeToElement(obj,
			new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}

public class WelcomeTextWidgetRenderingTests : WelcomeTextWidgetTestBase
{
	[TUnit.Core.Test]
	public async Task NoConfig_RendersNothing()
	{
		var cut = Render<WelcomeTextWidget>();
		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}

	[TUnit.Core.Test]
	public async Task Markdown_RendersAsHtml()
	{
		var cut = Render<WelcomeTextWidget>(p => p
			.Add(x => x.Config, BuildConfig(new { Markdown = "# Greetings" })));

		await Assert.That(cut.Markup).Contains("<h1");
		await Assert.That(cut.Markup).Contains("Greetings");
	}

	[TUnit.Core.Test]
	public async Task MalformedConfig_RendersNothing()
	{
		// A JSON string where an object is expected — deserialization fails, widget stays empty.
		var cut = Render<WelcomeTextWidget>(p => p
			.Add(x => x.Config, JsonSerializer.SerializeToElement("not an object")));

		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}
}

public class WelcomeTextWidgetGuestVisibilityTests : WelcomeTextWidgetTestBase
{
	[TUnit.Core.Test]
	public async Task OmittedShowToGuests_VisibleToGuests()
	{
		// Default true, so pre-existing configs that never set the key keep showing to everyone.
		var cut = Render<WelcomeTextWidget>(p => p
			.Add(x => x.Config, BuildConfig(new { Markdown = "Public notice" })));

		await Assert.That(cut.Markup).Contains("Public notice");
	}

	[TUnit.Core.Test]
	public async Task ShowToGuestsFalse_HiddenFromGuests()
	{
		var cut = Render<WelcomeTextWidget>(p => p
			.Add(x => x.Config, BuildConfig(new { Markdown = "Members only", ShowToGuests = false })));

		await Assert.That(cut.Markup.Trim()).IsEqualTo(string.Empty);
	}

	[TUnit.Core.Test]
	public async Task ShowToGuestsFalse_VisibleWhenSignedIn()
	{
		Auth.SetAuthorized("Gandalf");

		var cut = Render<WelcomeTextWidget>(p => p
			.Add(x => x.Config, BuildConfig(new { Markdown = "Members only", ShowToGuests = false })));

		await Assert.That(cut.Markup).Contains("Members only");
	}
}
