using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Tests.Shared;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The roster rule <see cref="AccountAuthService"/> delegates to <see cref="ActiveCharacterState"/>,
/// tested without HTTP or storage.
/// </summary>
public class ActiveCharacterStateTests
{
	private static readonly CharacterSummary Alpha = new(1, 100L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 200L, "Beta", "");

	[Test]
	public async Task TheRosterMarkerNamesTheActingCharacter()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);

		state.SetRoster([Alpha, Beta with { IsActing = true }]);

		await Assert.That(state.ActiveCharacter).IsEqualTo(Beta with { IsActing = true });
		await Assert.That(state.Characters.Count).IsEqualTo(2);
	}

	[Test]
	public async Task AnUnmarkedRosterActsAsNobody()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);
		state.SetRoster([Alpha with { IsActing = true }]);

		state.SetRoster([Alpha, Beta]);

		await Assert.That(state.ActiveCharacter).IsNull();
	}

	[Test]
	public async Task SettingTheSameCharacterTwiceRaisesOnce()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);
		var raised = 0;
		state.Changed += () => raised++;

		state.SetActive(Alpha);
		state.SetActive(Alpha with { Name = "Renamed elsewhere" });

		await Assert.That(raised).IsEqualTo(1);
	}

	[Test]
	public async Task ARenameOfTheActingCharacterReachesSubscribers()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);
		state.SetRoster([Alpha with { IsActing = true }]);
		var raised = 0;
		state.Changed += () => raised++;

		state.SetRoster([Alpha with { IsActing = true, Name = "Alpha Prime" }]);

		await Assert.That(raised).IsEqualTo(1);
		await Assert.That(state.ActiveCharacter!.Name).IsEqualTo("Alpha Prime");
	}

	[Test]
	public async Task AddedCharactersJoinWithoutTakingOver()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);
		state.SetRoster([Alpha with { IsActing = true }]);

		state.Add(Beta);

		await Assert.That(state.Characters.Select(c => c.DbrefNumber)).IsEquivalentTo([1, 2]);
		await Assert.That(state.ActiveCharacter!.DbrefNumber).IsEqualTo(1);
		await Assert.That(state.Without(1).Select(c => c.DbrefNumber)).IsEquivalentTo([2]);
	}

	[Test]
	public async Task AThrowingSubscriberDoesNotReachTheCaller()
	{
		var state = new ActiveCharacterState(NullLogger.Instance);
		state.Changed += () => throw new InvalidOperationException("render failed");

		state.SetActive(Alpha);

		await Assert.That(state.ActiveCharacter).IsEqualTo(Alpha);
	}
}

/// <summary>The <c>sessionStorage</c> keys behind <see cref="AccountSessionStorage"/>.</summary>
public class AccountSessionStorageTests : TrackingBunitContext
{
	[Test]
	public async Task ATabWithNoTokenHoldsNoSession()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(null);

		var stored = await new AccountSessionStorage(JSInterop.JSRuntime).ReadAsync();

		stored.Expect<NotFound>();
	}

	[Test]
	public async Task AStoredSessionComesBackWithItsCharacterNameAndFlag()
	{
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult("True");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.character").SetResult("7:70");

		var stored = (await new AccountSessionStorage(JSInterop.JSRuntime).ReadAsync())
			.Expect<AccountSessionStorage.StoredSession>();

		await Assert.That(stored).IsEqualTo(new AccountSessionStorage.StoredSession("token-1", "headwiz", true,
			new AccountSessionStorage.BoundCharacter(7, 70)));
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("7")]
	[Arguments("x:70")]
	[Arguments("7:70:1")]
	public async Task AnUnreadableCharacterIsNone(string? stored)
	{
		await Assert.That(AccountSessionStorage.BoundCharacter.Parse(stored)).IsNull();
	}

	[Test]
	public async Task ASwitchWritesTheTokenWithItsCharacter()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;

		var written = await new AccountSessionStorage(JSInterop.JSRuntime)
			.TryWriteTokenAsync("token-2", new AccountSessionStorage.BoundCharacter(9, 90));

		await Assert.That(written).IsTrue();
		var calls = JSInterop.Invocations.Select(c => (c.Identifier, string.Join(",", c.Arguments))).ToList();
		await Assert.That(calls).IsEquivalentTo(new[]
		{
			("sessionStorage.setItem", "sharpmush.account.sessionToken,token-2"),
			("sessionStorage.setItem", "sharpmush.account.character,9:90"),
		});
	}

	[Test]
	public async Task AWriteThatFailsPartWayDropsTheToken()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.SetupVoid("sessionStorage.setItem", "sharpmush.account.username", "headwiz")
			.SetException(new JSException("quota exceeded"));

		var written = await new AccountSessionStorage(JSInterop.JSRuntime)
			.TryWriteAsync("token-1", character: null, "headwiz", mustChangePassword: false, role: null, permissions: []);

		await Assert.That(written).IsFalse();
		var last = JSInterop.Invocations.Last();
		await Assert.That(last.Identifier).IsEqualTo("sessionStorage.removeItem");
		await Assert.That(last.Arguments[0]).IsEqualTo("sharpmush.account.sessionToken");
	}

	[Test]
	public async Task SigningOutLatchesAfterForgetting()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;

		await new AccountSessionStorage(JSInterop.JSRuntime).ClearAndLatchLoggedOutAsync();

		var calls = JSInterop.Invocations.ToList();
		await Assert.That(calls.Count(c => c.Identifier == "sessionStorage.removeItem")).IsEqualTo(6);
		await Assert.That(calls[^1].Identifier).IsEqualTo("sessionStorage.setItem");
		await Assert.That(calls[^1].Arguments[0]).IsEqualTo("sharpmush.account.loggedOut");
	}
}

/// <summary>
/// <see cref="AccountApiClient"/> answers like every other typed client: the server's sentence on a
/// refusal, not the raw body, and an <see cref="ApiFailure"/> kind a caller can act on.
/// </summary>
public class AccountApiClientTests
{
	private static (AccountApiClient Client, CapturingHttpHandler Handler) Answering(
		HttpStatusCode status, string body = "", string mediaType = "text/plain")
	{
		var handler = new CapturingHttpHandler(() => new HttpResponseMessage(status)
		{
			Content = new StringContent(body, Encoding.UTF8, mediaType)
		});
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(_ => new HttpClient(handler, disposeHandler: false)
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});
		return (new AccountApiClient(factory), handler);
	}

	[Test]
	public async Task ALoginRefusalIsTheServersSentence()
	{
		var (client, _) = Answering(HttpStatusCode.Unauthorized, "Invalid account credentials.");

		var failure = (await client.LoginAsync("headwiz", "wrong", rememberMe: false)).Expect<ApiFailure>();

		await Assert.That(failure.Message).IsEqualTo("Invalid account credentials.");
		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.Unauthenticated);
	}

	[Test]
	public async Task TheSessionReadNamesItsOwnBearer()
	{
		var (client, handler) = Answering(HttpStatusCode.Unauthorized);

		var result = await client.SessionAsync("token-1", CancellationToken.None);

		await Assert.That(result.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.Unauthenticated);
		await Assert.That(handler.LastRequest!.Headers.Authorization!.Parameter).IsEqualTo("token-1");
		await Assert.That(handler.LastRequest.RequestUri!.AbsolutePath).IsEqualTo("/api/account/session");
	}

	[Test]
	public async Task AnAccountChangeIsAPutThatAnswersOnlyWhetherItWorked()
	{
		var (client, handler) = Answering(HttpStatusCode.NoContent);

		var result = await client.ChangePasswordAsync("old-password", "new-password");

		result.Expect<Success>();
		await Assert.That(handler.LastRequest!.Method).IsEqualTo(HttpMethod.Put);
		await Assert.That(handler.LastBody).Contains("\"newPassword\":\"new-password\"");
	}

	[Test]
	public async Task ASetupStatusThatIsNotJsonIsAFailureNotAnAnswer()
	{
		var (client, _) = Answering(HttpStatusCode.OK, "<!DOCTYPE html><html></html>", "text/html");

		var failure = (await client.SetupStatusAsync()).Expect<ApiFailure>();

		await Assert.That(failure.Kind).IsEqualTo(ApiFailureKind.Unexpected);
	}
}
