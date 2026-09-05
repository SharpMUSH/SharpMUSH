using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes <c>api/admin/guests</c>: one available guest and one currently in use, plus whichever
/// <c>Net.Guests</c> state the test wants to render against.
/// </summary>
file sealed class AdminGuestsApiHandler : HttpMessageHandler
{
	public bool GuestLoginsEnabled { get; set; } = true;
	public int MaxGuests { get; set; } = -1;
	public int DeleteCalls { get; private set; }

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Get && path == "api/admin/guests")
		{
			return Task.FromResult(Json(new
			{
				GuestLoginsEnabled,
				MaxGuests,
				NextFreeName = "Guest3",
				Guests = new object[]
				{
					new { DbrefNumber = 21, CreationTime = 1L, Name = "Guest1", InUse = false },
					new { DbrefNumber = 22, CreationTime = 2L, Name = "Guest2", InUse = true }
				}
			}));
		}

		if (request.Method == HttpMethod.Delete && path.StartsWith("api/admin/guests/", StringComparison.Ordinal))
		{
			DeleteCalls++;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}

	private static HttpResponseMessage Json<T>(T value) =>
		new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

file static class AdminGuestsTestServices
{
	/// <summary>
	/// Wires a real <see cref="AdminGuestsService"/> onto the fake handler. The <see cref="HttpClient"/>
	/// is handed to <c>Track</c>, so the context disposes it — and with it the handler, which
	/// <see cref="HttpClient"/> owns by default.
	/// </summary>
	public static void AddAdminGuestsTestServices(this TrackingBunitContext ctx, AdminGuestsApiHandler handler)
	{
		var apiClient = ctx.Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance,
				Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()))
			.AddSingleton(sp => new AdminGuestsService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<AccountAuthService>()))
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
	}
}

/// <summary>
/// bUnit tests for /admin/players, which is now the guest-character panel. Replacing the
/// "coming soon" placeholder is the point: an operator has to be able to stock a game with guests
/// without opening a MU* client, because until they do, every anonymous visitor who clicks Play is
/// told there are no guest characters available.
/// </summary>
public class AdminGuestsPageTests : TrackingBunitContext
{
	private BunitAuthorizationContext Auth { get; }

	// The handler is deliberately not held as a field: it is a file-local type, which C# will not
	// allow in any member signature of a non-file-local class (CS9051). A test that needs a
	// different API response re-wires with its own handler, exactly as AdminAccountsPageTests does.
	public AdminGuestsPageTests()
	{
		Auth = this.AddAuthorization();
		this.AddAdminGuestsTestServices(new AdminGuestsApiHandler());
	}

	private Bunit.IRenderedComponent<SharpMUSH.Client.Pages.Admin.Players> RenderPage()
	{
		Auth.SetAuthorized("headwiz");
		Auth.SetRoles("Wizard");
		Auth.SetPolicies("players.view");
		return Render<SharpMUSH.Client.Pages.Admin.Players>();
	}

	[TUnit.Core.Test]
	public async Task RendersTheGuestRoster()
	{
		var cut = RenderPage();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Guest1"))
				throw new InvalidOperationException("guest rows not rendered yet");
		});

		await Assert.That(cut.Markup).Contains("Guest1");
		await Assert.That(cut.Markup).Contains("Guest2");
	}

	/// <summary>
	/// The name field starts on a name the server has confirmed is free, so stocking a game is one
	/// click. A blank field would make the operator invent a name and risk a collision on submit.
	/// </summary>
	[TUnit.Core.Test]
	public async Task PrefillsTheCreateFieldWithAFreeName()
	{
		var cut = RenderPage();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Guest1"))
				throw new InvalidOperationException("guest rows not rendered yet");
		});

		await Assert.That(cut.Find("#guest-name").GetAttribute("value")).IsEqualTo("Guest3");
	}

	/// <summary>
	/// The game refuses to destroy a connected player, so a delete button on an occupied guest could
	/// only ever fail. Disabling it is the difference between an explained "not now" and an error.
	/// </summary>
	[TUnit.Core.Test]
	public async Task DoesNotOfferToDeleteAGuestThatIsInUse()
	{
		var cut = RenderPage();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Guest2"))
				throw new InvalidOperationException("guest rows not rendered yet");
		});

		var deleteButtons = cut.FindAll("button[data-testid='guest-delete']");
		await Assert.That(deleteButtons.Count).IsEqualTo(2);
		await Assert.That(deleteButtons[0].HasAttribute("disabled")).IsFalse();
		await Assert.That(deleteButtons[1].HasAttribute("disabled")).IsTrue();
	}

	/// <summary>
	/// A stocked roster and a game that still refuses guests is the confusing case this panel exists
	/// to explain, so the Net.Guests state has to be on the page and not only in the config section.
	/// </summary>
	[TUnit.Core.Test]
	public async Task WarnsWhenGuestLoginsAreTurnedOff()
	{
		this.AddAdminGuestsTestServices(new AdminGuestsApiHandler { GuestLoginsEnabled = false });

		var cut = RenderPage();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Guest1"))
				throw new InvalidOperationException("guest rows not rendered yet");
		});

		await Assert.That(cut.FindAll("#guests-disabled-warning").Count).IsEqualTo(1);
	}

	[TUnit.Core.Test]
	public async Task DoesNotWarnWhenGuestLoginsAreOn()
	{
		var cut = RenderPage();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Guest1"))
				throw new InvalidOperationException("guest rows not rendered yet");
		});

		await Assert.That(cut.FindAll("#guests-disabled-warning").Count).IsEqualTo(0);
	}
}
