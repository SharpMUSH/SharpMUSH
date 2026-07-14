using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Authentication;

/// <summary>
/// Regression coverage for <see cref="AccountAuthService.InitAsync"/>'s null-session-token path.
/// sessionStorage (tab-scoped) is empty in a fresh tab while localStorage (survives tab closure)
/// may still hold a Username from a previous session. InitAsync must clear all in-memory auth
/// state in that case rather than restoring Username from localStorage — otherwise a returning
/// user in a new tab gets a phantom identity with no live session.
/// </summary>
public class AccountAuthServiceInitTests : BunitContext
{
	[TUnit.Core.Test]
	public async Task InitAsync_NoSessionToken_ClearsStateEvenWhenLocalStorageHasUsername()
	{
		// sessionStorage has no AccountSessionToken...
		JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(null);
		// ...but localStorage still has a username from an earlier session in this browser.
		// If InitAsync ever regresses to reading this before checking the session token, this
		// setup makes that regression observable (Username would come back non-null below).
		JSInterop.Setup<string?>("localStorage.getItem", _ => true).SetResult("returning-user");

		var service = new AccountAuthService(
			Substitute.For<IHttpClientFactory>(),
			JSInterop.JSRuntime,
			NullLogger<AccountAuthService>.Instance);

		await service.InitAsync();

		await Assert.That(service.IsLoggedIn).IsFalse();
		await Assert.That(service.Username).IsNull();
		await Assert.That(service.Role).IsNull();
		await Assert.That(service.Permissions).IsEmpty();
	}
}
