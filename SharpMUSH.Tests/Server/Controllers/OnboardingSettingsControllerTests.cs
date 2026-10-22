using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

public class OnboardingSettingsControllerTests
{
	private static OnboardingSettings Request => new("Test game", ["Fantasy"], ["High Fantasy"], "Welcome!",
		"main/welcome", "", "", "chibi", "");

	[Test]
	public async Task SettingsRequireConfigAdmin()
	{
		var policy = typeof(OnboardingSettingsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
			.Cast<AuthorizeAttribute>().Single().Policy;
		await Assert.That(policy).IsEqualTo(PortalPermission.ConfigAdmin);
	}

	[Test]
	public async Task IdentitySavePreservesUnrelatedMsspVariables()
	{
		var original = SharpMUSHOptions.Default();
		original = original with { Mssp = new(new Dictionary<string, string[]> { ["CONTACT"] = ["staff@example.org"], ["genre"] = ["Old"] }) };
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		await Assert.That(await controller.Put(Request, default)).IsTypeOf<OkObjectResult>();
		await Assert.That(saved!.Mssp.Variables["CONTACT"]).IsEquivalentTo(new[] { "staff@example.org" });
		await Assert.That(saved.Mssp.Variables.Keys.Count(key => string.Equals(key, "GENRE", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
		await Assert.That(saved.Net.MudName).IsEqualTo("Test game");
		await Assert.That(original.Mssp.Variables["genre"]).IsEquivalentTo(new[] { "Old" });
	}

	[Test]
	public async Task InvalidLinkOrArtCannotChangeSettings()
	{
		var writer = Substitute.For<IConfigOptionWriter>();
		var controller = new OnboardingSettingsController(Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), writer, Substitute.For<IAuditLog>());
		await Assert.That(await controller.Put(Request with { WelcomePage = "https://example.org" }, default)).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await controller.Put(Request with { ArtOverrideId = "../../anything.png" }, default)).IsTypeOf<BadRequestObjectResult>();
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}
}
