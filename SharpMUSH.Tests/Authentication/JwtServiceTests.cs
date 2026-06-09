using System.IdentityModel.Tokens.Jwt;
using System.Collections.Immutable;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Tests.Authentication;

/// <summary>
/// Unit tests for <see cref="JwtService"/> using NSubstitute mocks.
/// </summary>
public class JwtServiceTests
{
	// ── Helpers ──────────────────────────────────────────────────────────────

	private static JwtOptions DefaultOptions() => new()
	{
		SigningKey = "super-secret-test-key-32chars!!!",
		Issuer = "test-issuer",
		Audience = "test-audience",
		AccessTokenLifetimeMinutes = 15,
		RefreshTokenLifetimeDays = 7,
	};

	private static SharpAccount MakeAccount(string id = "accounts/1", string username = "Alice",
		bool isDisabled = false)
		=> new()
		{
			Id = id,
			Username = username,
			Email = null,
			PasswordHash = "hash",
			CreatedAt = 1_000_000,
			IsDisabled = isDisabled,
		};

	private static SharpPlayer MakePlayer(int key, string name)
	{
		var obj = new SharpObject
		{
			Key = key,
			CreationTime = 0L,
			Name = name,
			Type = "Player",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Zone = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>()),
		};

		return new SharpPlayer
		{
			Object = obj,
			Aliases = [],
			Location = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Home = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			PasswordHash = string.Empty,
			PasswordSalt = null,
			Quota = 20,
		};
	}

	private static (
		JwtService Service,
		IRefreshTokenStore Store,
		IRoleDerivationService RoleSvc,
		IAccountService AccountSvc,
		IMediator Mediator)
		Build(JwtOptions? opts = null)
	{
		var options = Options.Create(opts ?? DefaultOptions());
		var store = Substitute.For<IRefreshTokenStore>();
		var roleSvc = Substitute.For<IRoleDerivationService>();
		var accountSvc = Substitute.For<IAccountService>();
		var mediator = Substitute.For<IMediator>();
		var logger = NullLogger<JwtService>.Instance;

		store.CreateTokenAsync(Arg.Any<string>(), Arg.Any<DBRef>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult("aabbccdd11223344aabbccdd11223344"));

		return (new JwtService(options, store, roleSvc, accountSvc, mediator, logger), store, roleSvc, accountSvc, mediator);
	}

	// ── IssueTokensAsync ─────────────────────────────────────────────────────

	[Test]
	public async ValueTask IssueTokens_ReturnsAccessAndRefreshTokens()
	{
		var (svc, _, _, _, _) = Build();
		var account = MakeAccount();
		var player = MakePlayer(5, "Alice");

		var result = await svc.IssueTokensAsync(account, player, PortalRole.Player);

		await Assert.That(result.AccessToken).IsNotNull();
		await Assert.That(result.RefreshToken).IsEqualTo("aabbccdd11223344aabbccdd11223344");
		await Assert.That(result.Role).IsEqualTo(PortalRole.Player);
		await Assert.That(result.ExpiresIn).IsEqualTo(15 * 60);
	}

	[Test]
	public async ValueTask IssueTokens_AccessToken_IsValidJwt()
	{
		var (svc, _, _, _, _) = Build();
		var account = MakeAccount();
		var player = MakePlayer(5, "Alice");

		var result = await svc.IssueTokensAsync(account, player, PortalRole.Wizard);

		var handler = new JwtSecurityTokenHandler();
		await Assert.That(handler.CanReadToken(result.AccessToken)).IsTrue();

		var token = handler.ReadJwtToken(result.AccessToken);
		await Assert.That(token.Issuer).IsEqualTo("test-issuer");
	}

	[Test]
	public async ValueTask IssueTokens_ClaimsContainAccountIdAndCharacterKey()
	{
		var (svc, _, _, _, _) = Build();
		var account = MakeAccount("accounts/42");
		var player = MakePlayer(7, "Bob");

		var result = await svc.IssueTokensAsync(account, player, PortalRole.Player);

		var handler = new JwtSecurityTokenHandler();
		var token = handler.ReadJwtToken(result.AccessToken);

		var sub = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub)?.Value;
		var charKey = token.Claims.FirstOrDefault(c => c.Type == "character_key")?.Value;

		await Assert.That(sub).IsEqualTo("accounts/42");
		await Assert.That(charKey).IsEqualTo("7");
	}

	[Test]
	public async ValueTask IssueTokens_WizardRole_EncodedInRoleClaim()
	{
		var (svc, _, _, _, _) = Build();
		var result = await svc.IssueTokensAsync(MakeAccount(), MakePlayer(5, "Alice"), PortalRole.Wizard);

		var token = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
		var roleClaim = token.Claims
			.FirstOrDefault(c => c.Type is "role" or "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
			?.Value;
		await Assert.That(roleClaim).IsEqualTo("Wizard");
	}

	[Test]
	public async ValueTask IssueTokens_CallsRefreshTokenStore_CreateToken()
	{
		var (svc, store, _, _, _) = Build();
		var account = MakeAccount("accounts/1");
		var player = MakePlayer(5, "Alice");

		await svc.IssueTokensAsync(account, player, PortalRole.Player);

		await store.Received(1).CreateTokenAsync(
			"accounts/1",
			Arg.Is<DBRef>(d => d.Number == 5),
			Arg.Any<TimeSpan>(),
			Arg.Any<CancellationToken>());
	}

	// ── RefreshAsync ─────────────────────────────────────────────────────────

	[Test]
	public async ValueTask Refresh_ValidToken_RevokesAndIssuesNew()
	{
		var (svc, store, _, accountSvc, mediator) = Build();
		var charRef = new DBRef(5, 0L);
		var account = MakeAccount("accounts/1");
		var player = MakePlayer(5, "Alice");

		store.ValidateAsync("old-token", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<(string, DBRef)?>(("accounts/1", charRef)));
		accountSvc.GetByIdAsync("accounts/1", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAccount?>(account));
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new AnyOptionalSharpObject(player));

		var result = await svc.RefreshAsync("old-token");

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.AccessToken).IsNotNull();
		await store.Received(1).RevokeAsync("old-token", Arg.Any<CancellationToken>());
	}

	[Test]
	public async ValueTask Refresh_UnknownToken_ReturnsNull()
	{
		var (svc, store, _, _, _) = Build();

		store.ValidateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<(string, DBRef)?>(null));

		var result = await svc.RefreshAsync("ghost-token");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask Refresh_DisabledAccount_ReturnsNull()
	{
		var (svc, store, _, accountSvc, _) = Build();
		var charRef = new DBRef(5, 0L);

		store.ValidateAsync("tok", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<(string, DBRef)?>(("accounts/1", charRef)));
		accountSvc.GetByIdAsync("accounts/1", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAccount?>(MakeAccount(isDisabled: true)));

		var result = await svc.RefreshAsync("tok");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask Refresh_CharacterNotPlayer_ReturnsNull()
	{
		var (svc, store, _, accountSvc, mediator) = Build();
		var charRef = new DBRef(5, 0L);

		store.ValidateAsync("tok", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<(string, DBRef)?>(("accounts/1", charRef)));
		accountSvc.GetByIdAsync("accounts/1", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAccount?>(MakeAccount()));
		// Return a None (object not found)
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(new AnyOptionalSharpObject(new None()));

		var result = await svc.RefreshAsync("tok");

		await Assert.That(result).IsNull();
	}

	[Test]
	public async ValueTask Refresh_AccountNotFound_ReturnsNull()
	{
		var (svc, store, _, accountSvc, _) = Build();
		var charRef = new DBRef(5, 0L);

		store.ValidateAsync("tok", Arg.Any<CancellationToken>())
			.Returns(Task.FromResult<(string, DBRef)?>(("accounts/99", charRef)));
		accountSvc.GetByIdAsync("accounts/99", Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpAccount?>((SharpAccount?)null));

		var result = await svc.RefreshAsync("tok");

		await Assert.That(result).IsNull();
	}
}
