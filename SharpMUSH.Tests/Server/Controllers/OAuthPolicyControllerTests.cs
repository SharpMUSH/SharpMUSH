using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>The staff routes for apps that describe themselves: the policy, the block list, and reviewing one app.</summary>
public class OAuthPolicyControllerTests
{
	private const string ClientId = "https://valyria.example.com/oauth/client.json";

	private static (OAuthClientsController Controller, IConfigOptionWriter Writer, IOAuthAppReview Review, Func<SharpMUSHOptions?> Saved) Make()
	{
		var original = SharpMUSHOptions.Default();
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(original);
		var writer = Substitute.For<IConfigOptionWriter>();
		SharpMUSHOptions? saved = null;
		writer.UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>())
			.Returns(call => { saved = call.Arg<Func<SharpMUSHOptions, SharpMUSHOptions>>()(original); return saved; });
		var review = Substitute.For<IOAuthAppReview>();
		var controller = new OAuthClientsController(Substitute.For<IOAuthService>(), review, Substitute.For<IAuditLog>(), options, writer)
		{ ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
		return (controller, writer, review, () => saved);
	}

	[Test]
	public async Task TheDefaultIsOpen()
		=> await Assert.That(Make().Controller.Policy().Policy).IsEqualTo(OAuthOptions.Open);

	[Test]
	public async Task SavingThePolicyCleansTheDomains()
	{
		var (controller, _, _, saved) = Make();
		var result = await controller.SetPolicy(new OAuthAppPolicyView(OAuthOptions.Review,
			["Example.COM", ".evil.example", "https://bad.example/path", "example.com", "  "]), default);
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		await Assert.That(saved()!.OAuth.AppPolicy).IsEqualTo(OAuthOptions.Review);
		await Assert.That(saved()!.OAuth.BlockedDomains).IsEquivalentTo(new[] { "example.com", "evil.example", "bad.example" });
	}

	[Test]
	[Arguments("anything", "example.com")]
	[Arguments("review", "not a domain")]
	[Arguments("review", "exa_mple..com")]
	public async Task ABadPolicyOrDomainChangesNothing(string policy, string domain)
	{
		var (controller, writer, _, _) = Make();
		await Assert.That(await controller.SetPolicy(new OAuthAppPolicyView(policy, [domain]), default)).IsTypeOf<BadRequestObjectResult>();
		await writer.DidNotReceive().UpdateAsync(Arg.Any<Func<SharpMUSHOptions, SharpMUSHOptions>>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ReviewTakesOnlyASelfDescribedAppAndAKnownStatus()
	{
		var (controller, _, review, _) = Make();
		await Assert.That(await controller.Review(new OAuthClientReview("grid-builder", OAuthClientStatuses.Allowed), default))
			.IsTypeOf<BadRequestObjectResult>();
		await Assert.That(await controller.Review(new OAuthClientReview(ClientId, "approved"), default))
			.IsTypeOf<BadRequestObjectResult>();
		await review.DidNotReceiveWithAnyArgs().SetClientStatusAsync(default!, default);
	}

	[Test]
	public async Task BlockingAnAppPassesTheDecisionOn()
	{
		var (controller, _, review, _) = Make();
		var client = new OAuthClient(ClientId, "Valyria", null, null, ["http://127.0.0.1/cb"], ["profile"], null, DateTimeOffset.UnixEpoch)
		{ Status = OAuthClientStatus.Blocked };
		review.SetClientStatusAsync(ClientId, OAuthClientStatus.Blocked, Arg.Any<CancellationToken>())
			.Returns(new ValueTask<Found<OAuthClient>>(client));
		var result = await controller.Review(new OAuthClientReview(ClientId, OAuthClientStatuses.Blocked), default);
		var view = (OAuthClientView)((OkObjectResult)result).Value!;
		await Assert.That(view.Status).IsEqualTo(OAuthClientStatuses.Blocked);
		await Assert.That(view.DescribedAt).IsEqualTo("valyria.example.com");
	}
}
