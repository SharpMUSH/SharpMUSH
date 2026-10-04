using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// The profile banner reads profile-handler's portal-known fields (spec §3): image, banner and
/// color, plus role when a game adds it. Fields arrive either as plain strings or as the handler's
/// <c>{ value, visible }</c> shape; a colour that is not <c>#rrggbb</c> never reaches a style.
/// </summary>
public class CharacterProfileServiceTests : IDisposable
{
	private readonly List<HttpClient> _clients = [];

	public void Dispose()
	{
		foreach (var client in _clients)
		{
			client.Dispose();
		}
	}

	private HttpClient Client(Func<string, string?> answer)
	{
		var http = new HttpClient(new Handler(answer)) { BaseAddress = new Uri("https://localhost:8081/") };
		_clients.Add(http);
		return http;
	}

	private sealed class Handler(Func<string, string?> answer) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = answer(request.RequestUri!.PathAndQuery);
			return Task.FromResult(body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		}
	}

	private const string Characters = """[{"name":"Tomas Reyes","objid":"#312:1718000000","created":1718000000,"category":"","image":"/api/wiki-assets/a/tomas.jpg"}]""";

	private CharacterProfileService Build(Func<string, string?> answer)
	{
		var http = Client(answer);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		return new CharacterProfileService(factory, new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance));
	}

	[Test]
	public async Task ReadsImageBannerColorAndRole_InEitherFieldShape()
	{
		var service = Build(path => path switch
		{
			"/http/characters" => Characters,
			"/http/profile?objid=%23312%3A1718000000" => """
				{"character":"Tomas Reyes","objid":"#312:1718000000","dbref":"#312",
				 "fields":{"image":{"value":"/api/wiki-assets/a/tomas.jpg","visible":true},"banner":"/api/wiki-assets/b/docks.jpg",
				           "color":{"value":"#ffb454","visible":true},"role":"Lamplighter · Guild of Lamplighters"}}
				""",
			_ => null,
		});

		var profile = (await service.GetAsync("tomas reyes")).Expect<CharacterProfileData>();
		await Assert.That(profile.Name).IsEqualTo("Tomas Reyes");
		await Assert.That(profile.Dbref).IsEqualTo("#312");
		await Assert.That(profile.Image).IsEqualTo("/api/wiki-assets/a/tomas.jpg");
		await Assert.That(profile.Banner).IsEqualTo("/api/wiki-assets/b/docks.jpg");
		await Assert.That(profile.Color).IsEqualTo("#ffb454");
		await Assert.That(profile.Role).IsEqualTo("Lamplighter · Guild of Lamplighters");
	}

	[Test]
	public async Task AColourThatIsNotHex_IsDropped_AndBlankFieldsAreNull()
	{
		var service = Build(path => path switch
		{
			"/http/characters" => Characters,
			_ when path.StartsWith("/http/profile", StringComparison.Ordinal) => """
				{"character":"Tomas Reyes","objid":"#312:1718000000","dbref":"#312",
				 "fields":{"image":"","banner":{"value":"","visible":true},"color":"red;background:url(x)"}}
				""",
			_ => null,
		});

		var profile = (await service.GetAsync("Tomas Reyes")).Expect<CharacterProfileData>();
		await Assert.That(profile.Color).IsNull();
		await Assert.That(profile.Image).IsNull();
		await Assert.That(profile.Banner).IsNull();
		await Assert.That(profile.Role).IsNull();
	}

	[Test]
	public async Task AFieldTheHandlerMarksInvisible_IsNotShown()
	{
		var service = Build(path => path switch
		{
			"/http/characters" => Characters,
			_ when path.StartsWith("/http/profile", StringComparison.Ordinal) => """
				{"character":"Tomas Reyes","objid":"#312:1718000000","dbref":"#312",
				 "fields":{"image":{"value":"/api/wiki-assets/a/tomas.jpg","visible":false},
				           "banner":{"value":"/api/wiki-assets/b/docks.jpg","visible":false},
				           "color":{"value":"#ffb454","visible":false},"role":{"value":"Spy","visible":false}}}
				""",
			_ => null,
		});

		var profile = (await service.GetAsync("Tomas Reyes")).Expect<CharacterProfileData>();
		await Assert.That(profile.Image).IsNull().Because("the handler marked the field hidden from this viewer");
		await Assert.That(profile.Banner).IsNull();
		await Assert.That(profile.Color).IsNull();
		await Assert.That(profile.Role).IsNull();
	}

	[Test]
	public async Task NoSuchCharacter_IsNotFound_AndAnUnreadableDirectoryIsNot()
	{
		var missing = await Build(path => path == "/http/characters" ? Characters : null).GetAsync("Nobody");
		await Assert.That(missing.Expect<ApiFailure>().Kind).IsEqualTo(ApiFailureKind.NotFound);

		var down = await Build(_ => null).GetAsync("Tomas Reyes");
		await Assert.That(down.Expect<ApiFailure>().Kind).IsNotEqualTo(ApiFailureKind.NotFound)
			.Because("\"no such character\" is a 404 page; \"we could not ask the game\" is not");
	}

	[Test]
	public async Task AProfileHookThatAnswers404_IsNotANameThatDoesNotExist()
	{
		var result = await Build(path => path == "/http/characters" ? Characters : null).GetAsync("Tomas Reyes");
		await Assert.That(result.Expect<ApiFailure>().Kind).IsNotEqualTo(ApiFailureKind.NotFound)
			.Because("the directory found Tomas; only the profile could not be read");
	}

	[Test]
	public async Task TheDirectory_IsReadOnceForABurstOfCallers_AndAFailureIsNotRemembered()
	{
		var requests = 0;
		var fail = true;
		var http = Client(path =>
		{
			if (path != "/http/characters") return null;
			requests++;
			return fail ? null : Characters;
		});
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(http);
		var directory = new CharacterDirectoryService(factory, NullLogger<CharacterDirectoryService>.Instance);

		(await directory.ListAsync()).Expect<ApiFailure>();
		fail = false;
		var burst = await Task.WhenAll(directory.ListAsync(), directory.ListAsync(), directory.ListAsync());
		await Assert.That(burst.All(r => r is IReadOnlyList<CharacterDirectoryService.CharacterSummary>)).IsTrue();
		await Assert.That(requests).IsEqualTo(2).Because("one failed read, then one read shared by the three callers");
	}

	[Test]
	public async Task DirectoryRows_CarryTheImage()
	{
		var directory = new CharacterDirectoryService(
			Substitute.For<IHttpClientFactory>().Tap(f => f.CreateClient(Arg.Any<string>()).Returns(
				Client(p => p == "/http/characters" ? Characters : null))),
			NullLogger<CharacterDirectoryService>.Instance);
		var rows = (await directory.ListAsync()).Expect<IReadOnlyList<CharacterDirectoryService.CharacterSummary>>();
		await Assert.That(rows[0].Image).IsEqualTo("/api/wiki-assets/a/tomas.jpg");
	}
}

file static class SubstituteExtensions
{
	public static T Tap<T>(this T value, Action<T> action)
	{
		action(value);
		return value;
	}
}
