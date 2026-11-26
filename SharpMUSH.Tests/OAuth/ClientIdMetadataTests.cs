using System.Net;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.OAuth;

/// <summary>
/// The rules for an app that describes itself at its https <c>client_id</c>: which addresses name one, what its
/// document must say, where it may send codes, the block list, and which network addresses the reader may reach.
/// </summary>
public class ClientIdMetadataTests
{
	private const string ClientId = "https://valyria.example.com/oauth/client.json";
	private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

	private static string Document(string? clientId = ClientId, string redirects = "\"http://127.0.0.1/callback\"",
		string extra = "")
		=> $$"""{"client_id":"{{clientId}}","client_name":"Valyria","redirect_uris":[{{redirects}}]{{extra}}}""";

	[Test]
	[Arguments("https://valyria.example.com/client.json", true)]
	[Arguments("https://valyria.example.com/a/b?x=1", true)]
	[Arguments("https://valyria.example.com", false)]
	[Arguments("https://valyria.example.com/", false)]
	[Arguments("http://valyria.example.com/client.json", false)]
	[Arguments("https://user:pw@valyria.example.com/client.json", false)]
	[Arguments("https://valyria.example.com/client.json#top", false)]
	[Arguments("https://valyria.example.com/a/../client.json", false)]
	[Arguments("https://valyria.example.com/a/./client.json", false)]
	[Arguments("https://valyria.example.com/a/%2E%2E/client.json", false)]
	[Arguments("https://valyria.example.com./client.json", false)]
	[Arguments("https://203.0.113.7/client.json", false)]
	[Arguments("https://[2001:db8::1]/client.json", false)]
	[Arguments("grid-builder", false)]
	public async Task OnlyAnHttpsAddressWithAPathNamesAnApp(string clientId, bool expected)
		=> await Assert.That(ClientIdMetadata.IsClientIdUrl(clientId)).IsEqualTo(expected);

	[Test]
	public async Task AGoodDocumentIsAPublicUnreviewedApp()
	{
		var client = ClientIdMetadata.Read(ClientId, Document(extra: ""","scope":"profile scenes.read","client_uri":"https://valyria.example.com/","token_endpoint_auth_method":"none" """), Now)
			.Expect<OAuthClient>();
		await Assert.That(client.Id).IsEqualTo(ClientId);
		await Assert.That(client.Name).IsEqualTo("Valyria");
		await Assert.That(client.Homepage).IsEqualTo("https://valyria.example.com/");
		await Assert.That(client.SecretHash).IsNull();
		await Assert.That(client.Scopes).Contains("scenes.read");
		await Assert.That(client.Status).IsEqualTo(OAuthClientStatus.Unreviewed);
		await Assert.That(client.MetadataFetchedAt).IsEqualTo(Now);
	}

	[Test]
	public async Task ANameIsCleanedAndAHomepageElsewhereIsDropped()
	{
		const string json = """{"client_id":"https://valyria.example.com/oauth/client.json","client_name":"  Val\u0007yria \n  \u202eClient\u200b ","client_uri":"https://elsewhere.example/","redirect_uris":["http://127.0.0.1/cb"]}""";
		var client = ClientIdMetadata.Read(ClientId, json, Now).Expect<OAuthClient>();
		await Assert.That(client.Homepage).IsNull();
		await Assert.That(client.Name).IsEqualTo("Valyria Client");
	}

	[Test]
	[Arguments("not json", "not JSON")]
	[Arguments("[1]", "not a JSON object")]
	[Arguments("""{"client_id":"https://other.example.com/c.json","redirect_uris":["http://127.0.0.1/cb"]}""", "not the address")]
	[Arguments("""{"client_id":"https://valyria.example.com/oauth/client.json","redirect_uris":["http://127.0.0.1/cb"],"client_secret":"x"}""", "secret")]
	[Arguments("""{"client_id":"https://valyria.example.com/oauth/client.json","redirect_uris":["http://127.0.0.1/cb"],"token_endpoint_auth_method":"client_secret_basic"}""", "must be public")]
	[Arguments("""{"client_id":"https://valyria.example.com/oauth/client.json","redirect_uris":["http://127.0.0.1/cb"],"grant_types":["client_credentials"]}""", "authorization_code")]
	[Arguments("""{"client_id":"https://valyria.example.com/oauth/client.json","redirect_uris":[]}""", "redirect_uris")]
	[Arguments("""{"client_id":"https://valyria.example.com/oauth/client.json","redirect_uris":["https://evil.example/cb"]}""", "cannot be its redirect address")]
	public async Task ABadDocumentIsRefusedWithItsReason(string json, string reason)
	{
		var error = ClientIdMetadata.Read(ClientId, json, Now) switch
		{
			Error<string> refused => refused.Value,
			OAuthClient client => $"read {client.Name}",
		};
		await Assert.That(error).Contains(reason);
	}

	[Test]
	public async Task ADocumentOverTheLimitIsRefused()
	{
		var big = Document(extra: $",\"padding\":\"{new string('x', ClientIdMetadata.MaxDocumentBytes)}\"");
		await Assert.That(ClientIdMetadata.Read(ClientId, big, Now) is Error<string>).IsTrue();
	}

	[Test]
	[Arguments("https://valyria.example.com/callback", true)]
	[Arguments("https://VALYRIA.example.com/callback", true)]
	[Arguments("https://evil.example/callback", false)]
	[Arguments("https://sub.valyria.example.com/callback", false)]
	[Arguments("http://127.0.0.1:5000/callback", true)]
	[Arguments("http://[::1]/callback", true)]
	[Arguments("http://valyria.example.com/callback", false)]
	[Arguments("com.example.valyria:/callback", true)]
	[Arguments("com.example.valyria.desktop:/callback", true)]
	[Arguments("com.example:/callback", false)]
	[Arguments("com.example.valyriax:/callback", false)]
	public async Task RedirectsStayWithTheAppsHost(string redirect, bool expected)
		=> await Assert.That(ClientIdMetadata.RedirectFits(ClientId, redirect)).IsEqualTo(expected);

	[Test]
	[Arguments("example.com", true)]
	[Arguments(".example.com", true)]
	[Arguments("https://example.com/", true)]
	[Arguments("valyria.example.com", true)]
	[Arguments("EXAMPLE.COM", true)]
	[Arguments("example.com:8080", true)]
	[Arguments("example.com.", true)]
	[Arguments("ample.com", false)]
	[Arguments("other.example.com", false)]
	[Arguments("", false)]
	public async Task ABlockedDomainCoversItsSubdomains(string entry, bool expected)
		=> await Assert.That(ClientIdMetadata.IsBlocked(ClientId, [entry])).IsEqualTo(expected);

	[Test]
	[Arguments("8.8.8.8", true)]
	[Arguments("1.1.1.1", true)]
	[Arguments("2606:4700:4700::1111", true)]
	[Arguments("127.0.0.1", false)]
	[Arguments("10.1.2.3", false)]
	[Arguments("172.16.0.1", false)]
	[Arguments("172.31.255.255", false)]
	[Arguments("192.168.1.1", false)]
	[Arguments("169.254.169.254", false)]
	[Arguments("100.64.0.1", false)]
	[Arguments("0.0.0.0", false)]
	[Arguments("224.0.0.1", false)]
	[Arguments("255.255.255.255", false)]
	[Arguments("192.0.2.1", false)]
	[Arguments("198.18.0.1", false)]
	[Arguments("::1", false)]
	[Arguments("::", false)]
	[Arguments("fe80::1", false)]
	[Arguments("fc00::1", false)]
	[Arguments("fd12:3456::1", false)]
	[Arguments("ff02::1", false)]
	[Arguments("::ffff:127.0.0.1", false)]
	[Arguments("::ffff:8.8.8.8", true)]
	[Arguments("::127.0.0.1", false)]
	[Arguments("64:ff9b::a00:1", false)]
	[Arguments("2002:a00:1::1", false)]
	[Arguments("2001:db8::1", false)]
	[Arguments("2001::1", false)]
	public async Task TheReaderReachesOnlyPublicAddresses(string address, bool expected)
		=> await Assert.That(ClientMetadataFetcher.IsPublic(IPAddress.Parse(address))).IsEqualTo(expected);
}
