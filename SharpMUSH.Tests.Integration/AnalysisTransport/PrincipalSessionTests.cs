using System.Security.Claims;
using Mediator;
using NSubstitute;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.CodeAnalysis.Catalog;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Integration.CodeAnalysis;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class PrincipalSessionTests
{
	private sealed class Options : IOptionsWrapper<SharpMUSHOptions> { public SharpMUSHOptions CurrentValue => SharpMUSHOptions.Default(); }

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Session_RejectsMissingOrNonPlayerCasesBeforeMetadata(bool missing)
	{
		var mediator = Substitute.For<IMediator>();
		AnyOptionalSharpObject resolved = missing ? new None() : new SharpRoom { Object = LiveAnalysisCatalogTests.Actor(2).Object, Location = new(_ => Task.FromResult<AnyOptionalSharpContainer>(new None())) };
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(resolved);
		var accounts = Substitute.For<IAccountService>(); var catalog = Substitute.For<IAnalysisCatalog>();
		var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("character_key", "2"), new Claim("character_creation_time", "100"), new Claim(AnalysisIdentity.CredentialVersionClaim, "synthetic")], AnalysisTransportSecurity.AuthenticationScheme));
		var session = new AnalysisSessionFactory(mediator, accounts, catalog, new Options()).CreateSession(principal);
		await Assert.That(async () => await session.CreateAnalyzerAsync()).Throws<UnauthorizedAccessException>();
		await catalog.DidNotReceive().GetSnapshotAsync(Arg.Any<AnySharpObject>(), Arg.Any<CancellationToken>());
		await accounts.DidNotReceive().GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task Session_RevalidatesPasswordAccountAndIncarnationBeforeMetadata()
	{
		var player = LiveAnalysisCatalogTests.Actor(2);
		player.PasswordHash = "synthetic-version-one";
		var account = new SharpAccount { Id = "fixture-account", Username = "fixture", PasswordHash = "synthetic" };
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(_ => new AnyOptionalSharpObject(new AnySharpObject(player)));
		var accounts = Substitute.For<IAccountService>();
		accounts.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(_ => account);
		var catalog = Substitute.For<IAnalysisCatalog>();
		catalog.GetSnapshotAsync(Arg.Any<AnySharpObject>(), Arg.Any<CancellationToken>()).Returns(new AnalysisCatalogSnapshot("e", "r", "live", []));
		var identity = new ClaimsIdentity([
			new Claim("character_key", "2"), new Claim("character_creation_time", "100"),
			new Claim(ClaimTypes.NameIdentifier, account.Id), new Claim(AnalysisIdentity.CredentialVersionClaim, AnalysisIdentity.CredentialVersion(player.PasswordHash))], AnalysisTransportSecurity.AuthenticationScheme);
		var factory = new AnalysisSessionFactory(mediator, accounts, catalog, new Options());
		var session = factory.CreateSession(new ClaimsPrincipal(identity));
		await Assert.That(await session.CreateAnalyzerAsync()).IsNotNull();
		player.PasswordHash = "synthetic-version-two";
		await Assert.That(async () => await session.CreateAnalyzerAsync()).Throws<UnauthorizedAccessException>();
		player.PasswordHash = "synthetic-version-one";
		account.Status = AccountStatus.Disabled;
		await Assert.That(async () => await session.CreateAnalyzerAsync()).Throws<UnauthorizedAccessException>();
		account.Status = AccountStatus.Active;
		player.Object.CreationTime++;
		await Assert.That(async () => await session.CreateAnalyzerAsync()).Throws<UnauthorizedAccessException>();
	}

	[Test]
	public async Task Session_RejectsOtherAuthenticationSchemes()
	{
		var factory = new AnalysisSessionFactory(Substitute.For<IMediator>(), Substitute.For<IAccountService>(), Substitute.For<IAnalysisCatalog>(), new Options());
		await Assert.That(() => factory.CreateSession(new ClaimsPrincipal(new ClaimsIdentity([new Claim("character_key", "1")], "DebugAuth")))).Throws<UnauthorizedAccessException>();
	}
}
