using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Mssp;
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
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>());
		await Assert.That(await controller.Put(Request with { WelcomePage = "https://example.org" }, default)).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await controller.Put(Request with { ArtOverrideId = "../../anything.png" }, default)).IsTypeOf<BadRequestObjectResult>();
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments("Fantasy", "Space Opera", false)]
	[Arguments("Fantasy", "High Fantasy\nCyberpunk", false)]
	[Arguments("Custom genre", "Space Opera / Detective", false)]
	[Arguments("Fantasy\nHorror", "Space Opera\nCyberpunk", false)]
	[Arguments("", "", false)]
	[Arguments("", "", true)]
	public async Task UnchangedMetadataCanBeRetainedExactly(string genres, string subgenres, bool keepEmptyKeys)
	{
		var expectedGenres = genres == "" ? Array.Empty<string>() : genres.Split('\n');
		var expectedSubgenres = subgenres == "" ? Array.Empty<string>() : subgenres.Split('\n');
		var variables = new Dictionary<string, string[]> { ["CONTACT"] = ["staff@example.org"] };
		if (expectedGenres.Length > 0 || keepEmptyKeys) variables["genre"] = expectedGenres;
		if (expectedSubgenres.Length > 0 || keepEmptyKeys) variables["subgenre"] = expectedSubgenres;
		var original = SharpMUSHOptions.Default() with { Mssp = new(variables) };
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var request = Request with { Genres = expectedGenres, Subgenres = expectedSubgenres, Introduction = "An unrelated edit" };
		await Assert.That(await controller.Put(request, default)).IsTypeOf<OkObjectResult>();
		await Assert.That(saved!.Mssp.Variables.Keys).IsEquivalentTo(variables.Keys, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		foreach (var (key, values) in variables)
		{
			await Assert.That(saved.Mssp.Variables[key]).IsEquivalentTo(values, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		}
		await Assert.That(saved.Onboarding.Introduction).IsEqualTo("An unrelated edit");
		await Assert.That(saved.Net.MudName).IsEqualTo(Request.MudName);
	}

	[Test]
	public async Task ANewCatalogChoiceReplacesOnlyItsField()
	{
		var original = SharpMUSHOptions.Default() with
		{
			Mssp = new(new Dictionary<string, string[]> { ["genre"] = ["Custom genre"], ["subgenre"] = ["Space Opera", "Cyberpunk"] })
		};
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var request = Request with { Genres = ["Horror"], Subgenres = ["Space Opera", "Cyberpunk"] };
		await Assert.That(await controller.Put(request, default)).IsTypeOf<OkObjectResult>();
		await Assert.That(saved!.Mssp.Variables["GENRE"]).IsEquivalentTo(new[] { "Horror" });
		await Assert.That(saved.Mssp.Variables.ContainsKey("genre")).IsFalse();
		await Assert.That(saved.Mssp.Variables["subgenre"]).IsEquivalentTo(new[] { "Space Opera", "Cyberpunk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task RetainedMetadataUsesTheWritersCurrentSnapshot()
	{
		var original = SharpMUSHOptions.Default() with { Mssp = new(new Dictionary<string, string[]> { ["SUBGENRE"] = ["Space Opera"] }) };
		var latest = original with { Mssp = new(new Dictionary<string, string[]> { ["SUBGENRE"] = ["Space Opera", "Cyberpunk"] }) };
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(latest); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		await Assert.That(await controller.Put(Request with { Genres = [], Subgenres = ["Space Opera"] }, default)).IsTypeOf<OkObjectResult>();
		await Assert.That(saved!.Mssp.Variables["SUBGENRE"]).IsEquivalentTo(new[] { "Space Opera", "Cyberpunk" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("High Fantasy\nCyberpunk")]
	[Arguments("Space Opera\tInjected")]
	public async Task RetainingMetadataDoesNotAllowBlankMultipleOrControlValues(string subgenres)
	{
		var original = SharpMUSHOptions.Default() with { Mssp = new(new Dictionary<string, string[]> { ["SUBGENRE"] = ["Space Opera"] }) };
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>());
		await Assert.That(await controller.Put(Request with { Subgenres = subgenres.Split('\n') }, default)).IsTypeOf<BadRequestObjectResult>();
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	[Arguments("Space Opera")]
	[Arguments("Space Opera / Detective")]
	[Arguments("宇宙冒険")]
	[Arguments("High Fantasy")]
	public async Task NewCustomOrSuggestedSubgenreReplacesOnlyThatField(string subgenre)
	{
		var original = SharpMUSHOptions.Default() with
		{
			Mssp = new(new Dictionary<string, string[]> { ["genre"] = ["Custom genre"], ["subgenre"] = ["Cyberpunk", "Gothic"], ["CONTACT"] = ["staff@example.org"] })
		};
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var request = Request with { Genres = ["Custom genre"], Subgenres = [subgenre] };
		var result = await controller.Put(request, default);
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		await Assert.That(((OnboardingSettings)((OkObjectResult)result).Value!).Subgenres).IsEquivalentTo(new[] { subgenre });
		await Assert.That(saved!.Mssp.Variables["SUBGENRE"]).IsEquivalentTo(new[] { subgenre });
		await Assert.That(saved.Mssp.Variables.ContainsKey("subgenre")).IsFalse();
		await Assert.That(saved.Mssp.Variables["genre"]).IsEquivalentTo(new[] { "Custom genre" });
		await Assert.That(saved.Mssp.Variables["CONTACT"]).IsEquivalentTo(new[] { "staff@example.org" });
	}

	[Test]
	[Arguments(512, true)]
	[Arguments(513, false)]
	public async Task NewSubgenreKeepsTheMsspLengthLimit(int length, bool accepted)
	{
		var original = SharpMUSHOptions.Default();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original));
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		var result = await controller.Put(Request with { Subgenres = [new string('x', length)] }, default);
		await Assert.That(result is OkObjectResult).IsEqualTo(accepted);
		if (!accepted)
		{
			await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
			await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
		}
	}

	[Test]
	[Arguments("\n")]
	[Arguments("\r")]
	[Arguments("\0")]
	public async Task NewScalarSubgenreCannotContainControlCharacters(string control)
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var writer = Substitute.For<IConfigOptionWriter>();
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>());
		var result = await controller.Put(Request with { Subgenres = ["Space" + control + "Western"] }, default);
		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task NullMetadataCannotChangeSettings()
	{
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(SharpMUSHOptions.Default());
		var writer = Substitute.For<IConfigOptionWriter>();
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>());
		foreach (var request in new[] { Request with { Genres = null! }, Request with { Subgenres = null! }, Request with { Subgenres = [null!] } })
		{
			await Assert.That(await controller.Put(request, default)).IsTypeOf<BadRequestObjectResult>();
		}
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task RequestLimitFitsRetainedArraysWithJsonEscaping()
	{
		var values = Enumerable.Repeat(new string('<', MsspCatalog.MaxValueLength), MsspCatalog.MaxValues).ToArray();
		var request = Request with { Genres = values, Subgenres = values };
		var json = JsonSerializer.SerializeToUtf8Bytes(request, JsonSerializerOptions.Web);
		var limit = typeof(OnboardingSettingsController).GetCustomAttributes(typeof(RequestSizeLimitAttribute), true)
			.Cast<IRequestSizeLimitMetadata>().Single().MaxRequestBodySize ?? 0;
		await Assert.That(json.Length).IsGreaterThan(16 * 1024);
		await Assert.That(json.LongLength).IsLessThanOrEqualTo(limit);
		var original = SharpMUSHOptions.Default() with
		{
			Mssp = new(new Dictionary<string, string[]> { ["GENRE"] = values, ["SUBGENRE"] = values })
		};
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var controller = new OnboardingSettingsController(options, writer, Substitute.For<IAuditLog>())
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		await Assert.That(await controller.Put(request, default)).IsTypeOf<OkObjectResult>();
		await Assert.That(saved!.Mssp.Variables["GENRE"]).IsEquivalentTo(values, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(saved.Mssp.Variables["SUBGENRE"]).IsEquivalentTo(values, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}
}
