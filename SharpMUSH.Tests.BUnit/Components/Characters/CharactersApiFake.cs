using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;
using AccountCharacter = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components.Characters;

/// <summary>
/// The character directory as the Characters section reads it: every character (<c>http/characters</c>),
/// who is online (<c>http/online</c>), the signed-in account's own characters (<c>api/account/characters</c>),
/// profiles, the gallery and the scene API. Paths not listed answer 404.
/// </summary>
public sealed class CharactersApiFake : HttpMessageHandler
{
	public static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	private static readonly long LongAgo = DateTimeOffset.UtcNow.AddYears(-1).ToUnixTimeMilliseconds();

	public string Characters { get; set; } = $$"""
		[{"name":"Dace Kellan","objid":"#315:1","created":{{LongAgo}},"category":"Guard"},
		 {"name":"Ilsa Varn","objid":"#313:1","created":{{LongAgo}},"category":"Lamplighter","image":"/api/wiki-assets/i/ilsa.jpg"},
		 {"name":"Magister Oake","objid":"#316:1","created":{{Now}},"category":""},
		 {"name":"Tomas Reyes","objid":"#312:1","created":{{LongAgo}},"category":"Lamplighter","image":"/api/wiki-assets/t/tomas.jpg"},
		 {"name":"Wren Halloway","objid":"#314:1","created":{{Now}},"category":"Guard"}]
		""";

	public string Online { get; set; } = """
		[{"name":"Tomas Reyes","objid":"#312:1","created":1,"category":""},
		 {"name":"Wren Halloway","objid":"#314:1","created":1,"category":""}]
		""";

	/// <summary>Answers by path for GET, and by "METHOD path" for anything else.</summary>
	public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);

	/// <summary>Sees every request before it is answered (to capture a write's body).</summary>
	public Func<HttpRequestMessage, Task>? OnRequest { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		if (OnRequest is not null) await OnRequest(request);
		var path = request.RequestUri!.PathAndQuery;
		string? body = request.Method == HttpMethod.Get
			? path switch
			{
				"/http/characters" => Characters,
				"/http/online" => Online,
				_ => Extra.GetValueOrDefault(path),
			}
			: Extra.GetValueOrDefault($"{request.Method.Method} {path}");
		return body is null
			? new HttpResponseMessage(HttpStatusCode.NotFound)
			: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
	}

	/// <summary>Registers the fake, the directory, profile and scene services, MudBlazor, localization and authorization.</summary>
	public static (CharactersApiFake Fake, IHttpClientFactory Factory, BunitAuthorizationContext Auth) Install(TrackingBunitContext ctx)
	{
		var fake = new CharactersApiFake();
		var client = ctx.Track(new HttpClient(fake) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		ctx.Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance))
			.AddSingleton(sp => new CharacterProfileService(factory, sp.GetRequiredService<CharacterDirectoryService>()))
			.AddSingleton(sp => new SceneService(factory, TestAccountAuth.Of(sp)))
			.AddSingleton(sp => new GalleryService(factory, sp.GetRequiredService<CharacterDirectoryService>()))
			.AddSingleton<SidebarCollapseService>()
			.AddLocalization();
		// The profile's Page button asks whether a visitor could play as a guest.
		ctx.Services.TryAddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
		var auth = ctx.AddAuthorization();
		return (fake, factory, auth);
	}

	/// <summary>An AccountAuthService signed in with <paramref name="characters"/> as its roster.</summary>
	public static async Task<AccountAuthService> SignedInAsync(TrackingBunitContext ctx, params AccountCharacter[] characters)
	{
		var client = ctx.Track(new HttpClient(new AccountHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		var auth = new AccountAuthService(factory, ctx.JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
		await auth.InitAsync();
		if (await auth.LoginAsync("player", "password") is ApiFailure loginFailure)
			throw new InvalidOperationException($"Test setup login failed: {loginFailure.Message}");
		return auth;
	}

	/// <summary>An AccountAuthService nobody signed in to.</summary>
	public static AccountAuthService Anonymous(TrackingBunitContext ctx)
	{
		var client = ctx.Track(new HttpClient(new AccountHandler([])) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		return new AccountAuthService(factory, ctx.JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []);
	}

	private sealed class AccountHandler(IReadOnlyList<AccountCharacter> characters) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var path = request.RequestUri!.AbsolutePath.TrimStart('/');
			if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(new
					{
						accountId = "acct-1",
						username = "player",
						characters,
						accountSessionToken = "session-token-1",
						mustChangePassword = false,
						role = (string?)null,
						permissions = Array.Empty<string>(),
					})
				});
			}

			return Task.FromResult(request.Method == HttpMethod.Get && path == "api/account/characters"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(characters) }
				: new HttpResponseMessage(HttpStatusCode.NotFound));
		}
	}
}
