using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Server.Controllers;

namespace SharpMUSH.Tests.Server.Controllers;

public class AccountCharacterCreationTests
{
	private sealed class Harness
	{
		public readonly IMediator Mediator = Substitute.For<IMediator>();
		public readonly IAccountService Accounts = Substitute.For<IAccountService>();
		public readonly IPasswordService Passwords = Substitute.For<IPasswordService>();
		public readonly AccountController Controller;
		public CreatePlayerCommand? Created;
		public Harness(bool attach = true)
		{
			var sessions = Substitute.For<IAccountSessionStore>();
			var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
			options.CurrentValue.Returns(SharpMUSHOptions.Default());
			var validation = Substitute.For<IValidateService>();
			validation.ValidPlayerName(Arg.Any<MString>(), Arg.Any<AnyOptionalSharpObject>(), Arg.Any<AnyOptionalSharpObject>()).Returns(true);
			Mediator.CreateStream(Arg.Any<GetPlayerQuery>(), Arg.Any<CancellationToken>()).Returns(AsyncEnumerable.Empty<SharpPlayer>());
			Mediator.Send(Arg.Do<CreatePlayerCommand>(command => Created = command), Arg.Any<CancellationToken>()).Returns(new DBRef(10, 1000));
			Passwords.HashPassword(Arg.Any<string>()).Returns("nonempty-random-hash");
			if (attach) Accounts.GetAccountForCharacterAsync(new DBRef(10, 1000), Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "account", Username = "owner", PasswordHash = null });
			Controller = new(Mediator, Accounts, sessions, options, validation,
				new PasskeyService(Substitute.For<SharpMUSH.Library.IAccountStore>(), new PasskeyRelyingParty(new ConfigurationBuilder().Build(), options),
					new PasskeyCeremonyStore(TimeProvider.System), TimeProvider.System, NullLogger<PasskeyService>.Instance),
				Substitute.For<IPortalThemeService>(), new BearerAccountResolver(sessions, Accounts), NullLogger<AccountController>.Instance)
			{
				ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "account")], AccountSessionAuthenticationHandler.SchemeName)) } }
			};
		}
	}

	[Test]
	public async Task NoChosenPasswordQueuesOnlyANonemptyHashAndAttachesBeforeSuccess()
	{
		var h = new Harness();
		var result = await h.Controller.CreateCharacter(new("New Character", null), new AccountCredentialGate(), h.Passwords);
		await Assert.That(result).IsTypeOf<OkObjectResult>();
		await Assert.That(h.Created!.Password).IsEqualTo("nonempty-random-hash");
		await Assert.That(h.Created.Salt).IsEqualTo("");
		await h.Accounts.Received(1).LinkCharacterAsync("account", new DBRef(10, 1000), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ExistingChosenPasswordCreationRetainsTheNormalHashingPath()
	{
		var h = new Harness();
		await h.Controller.CreateCharacter(new("New Character", "chosen-password"), new AccountCredentialGate(), h.Passwords);
		await Assert.That(h.Created!.Password).IsEqualTo("chosen-password");
		await Assert.That(h.Created.Salt).IsNull();
		h.Passwords.DidNotReceive().HashPassword(Arg.Any<string>());
	}

	[Test]
	public async Task FailedAttachRemovesTheNewUnownedCharacterAndNeverReturnsSuccess()
	{
		var h = new Harness(attach: false);
		await Assert.That(await h.Controller.CreateCharacter(new("New Character", null), new AccountCredentialGate(), h.Passwords)).IsTypeOf<BadRequestObjectResult>();
		await h.Mediator.Received(1).Send(new DeleteObjectCommand(new DBRef(10, 1000)), CancellationToken.None);
	}
}
