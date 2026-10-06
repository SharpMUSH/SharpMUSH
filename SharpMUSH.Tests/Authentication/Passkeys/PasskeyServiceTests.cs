using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Authentication.Passkeys;

/// <summary>
/// Registering a passkey and signing in with it, end to end against a real world file, with a software
/// authenticator standing in for the browser and the device.
/// </summary>
public class PasskeyServiceTests
{
	private const string Origin = "https://play.example.test";

	private string _path = null!;
	private LightningDatabase _db = null!;
	private ManualTime _time = null!;
	private PasskeyService _passkeys = null!;
	private SharpAccount _alice = null!;
	private SharpAccount _bob = null!;

	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_alice = await _db.CreateAccountAsync("alice", null, "hash");
		_bob = await _db.CreateAccountAsync("bob", null, "hash");

		_time = new ManualTime();
		_passkeys = Service(new ConfigurationBuilder().Build());
	}

	private PasskeyService Service(IConfiguration configuration) => new(_db,
		new PasskeyRelyingParty(configuration, new TestSharpMushOptions.FixedWrapper(TestSharpMushOptions.Create())),
		new PasskeyCeremonyStore(_time), _time, NullLogger<PasskeyService>.Instance);

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort, as in AccountsAndSessionsTests: the lock file can outlive the writer briefly.
		}
	}

	/// <summary>A request from the portal at <paramref name="origin"/>, served by this server.</summary>
	private static HttpRequest Request(string origin = Origin, string? originHeader = null)
	{
		var uri = new Uri(origin);
		var context = new DefaultHttpContext();
		context.Request.Scheme = uri.Scheme;
		context.Request.Host = new HostString(uri.Authority);
		if (originHeader is not null) context.Request.Headers.Origin = originHeader;
		return context.Request;
	}

	private static SoftwareAuthenticator Authenticator(SharpAccount account)
		=> new(Origin, Encoding.UTF8.GetBytes(account.Id!));

	private async Task<AccountPasskey> RegisterAsync(SharpAccount account, SoftwareAuthenticator authenticator, string name = "Phone")
	{
		var challenge = (await _passkeys.BeginRegistrationAsync(account, Request())).Expect<PasskeyService.Challenge>();
		return (await _passkeys.CompleteRegistrationAsync(account.Id!, challenge.CeremonyId, name,
			authenticator.Create(challenge.Options))).Expect<AccountPasskey>();
	}

	private async Task<Result<SharpAccount>> SignInAsync(SoftwareAuthenticator authenticator)
	{
		var challenge = _passkeys.BeginSignIn(Request()).Expect<PasskeyService.Challenge>();
		return await _passkeys.CompleteSignInAsync(challenge.CeremonyId, authenticator.Get(challenge.Options));
	}

	[Test]
	public async Task A_registered_passkey_signs_in_to_its_account()
	{
		using var phone = Authenticator(_alice);
		var registered = await RegisterAsync(_alice, phone);

		var signedIn = (await SignInAsync(phone)).Expect<SharpAccount>();

		await Assert.That(signedIn.Id).IsEqualTo(_alice.Id);
		await Assert.That(registered.Name).IsEqualTo("Phone");
		await Assert.That(registered.Transports).IsEquivalentTo(["internal"]);
		var stored = (await _db.GetAccountPasskeysAsync(_alice.Id!)).Single();
		await Assert.That(stored.SignCount).IsEqualTo(1u);
		await Assert.That(stored.LastUsedAt).IsEqualTo(_time.Now);
	}

	[Test]
	public async Task A_sign_in_challenge_answers_once()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);

		var challenge = _passkeys.BeginSignIn(Request()).Expect<PasskeyService.Challenge>();
		var answer = phone.Get(challenge.Options);

		await Assert.That(await _passkeys.CompleteSignInAsync(challenge.CeremonyId, answer) is SharpAccount).IsTrue();
		await Assert.That(await _passkeys.CompleteSignInAsync(challenge.CeremonyId, answer) is Error<string>).IsTrue();
	}

	[Test]
	public async Task An_expired_challenge_is_refused()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);

		var challenge = _passkeys.BeginSignIn(Request()).Expect<PasskeyService.Challenge>();
		_time.Now += PasskeyCeremonyStore.Lifetime + TimeSpan.FromSeconds(1);

		await Assert.That(await _passkeys.CompleteSignInAsync(challenge.CeremonyId, phone.Get(challenge.Options)) is Error<string>)
			.IsTrue();
	}

	[Test]
	public async Task A_removed_passkey_no_longer_signs_in()
	{
		using var phone = Authenticator(_alice);
		var registered = await RegisterAsync(_alice, phone);

		await Assert.That(await _passkeys.RemoveAsync(_alice.Id!, registered.CredentialId)).IsTrue();

		await Assert.That(await SignInAsync(phone) is Error<string>).IsTrue();
	}

	[Test]
	public async Task A_passkey_answering_for_another_account_is_refused()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);
		phone.UserHandle = Encoding.UTF8.GetBytes(_bob.Id!);

		await Assert.That(await SignInAsync(phone) is Error<string>).IsTrue();
	}

	[Test]
	public async Task An_answer_from_another_origin_is_refused()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);
		phone.Origin = "https://evil.example.test";

		await Assert.That(await SignInAsync(phone) is Error<string>).IsTrue();
	}

	[Test]
	public async Task A_malformed_answer_is_refused()
	{
		using var phone = Authenticator(_alice);
		var registration = (await _passkeys.BeginRegistrationAsync(_alice, Request())).Expect<PasskeyService.Challenge>();
		var good = phone.Create(registration.Options);
		var garbled = JsonSerializer.SerializeToElement(new
		{
			id = good.GetProperty("id").GetString(),
			rawId = good.GetProperty("rawId").GetString(),
			type = "public-key",
			response = new
			{
				clientDataJSON = good.GetProperty("response").GetProperty("clientDataJSON").GetString(),
				attestationObject = "AAECAwQF",
			},
		});

		var finished = await _passkeys.CompleteRegistrationAsync(_alice.Id!, registration.CeremonyId, "Phone", garbled);

		await Assert.That(finished is Error<string>).IsTrue();
		await Assert.That(await _db.GetAccountPasskeysAsync(_alice.Id!)).IsEmpty();
	}

	[Test]
	public async Task A_registration_started_by_one_account_cannot_be_finished_by_another()
	{
		using var phone = Authenticator(_alice);
		var challenge = (await _passkeys.BeginRegistrationAsync(_alice, Request())).Expect<PasskeyService.Challenge>();

		var finished = await _passkeys.CompleteRegistrationAsync(_bob.Id!, challenge.CeremonyId, "Phone", phone.Create(challenge.Options));

		await Assert.That(finished is Error<string>).IsTrue();
		await Assert.That(await _db.GetAccountPasskeysAsync(_bob.Id!)).IsEmpty();
	}

	[Test]
	public async Task The_same_passkey_cannot_be_registered_twice()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);

		var again = (await _passkeys.BeginRegistrationAsync(_alice, Request())).Expect<PasskeyService.Challenge>();
		var excluded = again.Options.GetProperty("excludeCredentials").EnumerateArray().Single().GetProperty("id").GetString();
		var finished = await _passkeys.CompleteRegistrationAsync(_alice.Id!, again.CeremonyId, "Phone", phone.Create(again.Options));

		await Assert.That(excluded).IsEqualTo(System.Buffers.Text.Base64Url.EncodeToString(phone.CredentialId));
		await Assert.That(finished is Error<string>).IsTrue();
		await Assert.That((await _db.GetAccountPasskeysAsync(_alice.Id!)).Count).IsEqualTo(1);
	}

	[Test]
	public async Task Renaming_and_removing_touch_only_the_holders_passkeys()
	{
		using var phone = Authenticator(_alice);
		var registered = await RegisterAsync(_alice, phone);

		await Assert.That(await _passkeys.RenameAsync(_bob.Id!, registered.CredentialId, "Mine now")).IsFalse();
		await Assert.That(await _passkeys.RemoveAsync(_bob.Id!, registered.CredentialId)).IsFalse();
		await Assert.That(await _passkeys.RenameAsync(_alice.Id!, registered.CredentialId, "Laptop")).IsTrue();

		var stored = (await _db.GetAccountPasskeysAsync(_alice.Id!)).Single();
		await Assert.That(stored.Name).IsEqualTo("Laptop");
	}

	[Test]
	public async Task An_account_lists_its_passkeys_oldest_first()
	{
		using var phone = Authenticator(_alice);
		using var laptop = Authenticator(_alice);
		await RegisterAsync(_alice, phone, "Phone");
		_time.Now += TimeSpan.FromMinutes(1);
		await RegisterAsync(_alice, laptop, "Laptop");

		var names = (await _passkeys.ListAsync(_alice.Id!)).Select(p => p.Name).ToArray();

		await Assert.That(names).IsEquivalentTo(["Phone", "Laptop"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task A_portal_on_another_origin_must_be_listed()
	{
		var refused = _passkeys.BeginSignIn(Request(originHeader: "https://elsewhere.example.test"));

		var listed = Service(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Cors:AllowedOrigins:0"] = "https://elsewhere.example.test",
		}).Build()).BeginSignIn(Request(originHeader: "https://elsewhere.example.test"));

		await Assert.That(refused is Error<string>).IsTrue();
		await Assert.That(listed.Expect<PasskeyService.Challenge>().Options.GetProperty("rpId").GetString())
			.IsEqualTo("elsewhere.example.test");
	}

	/// <summary>Behind a proxy whose forwarded scheme is not trusted the request reads as http; the page is still this host's.</summary>
	[Test]
	public async Task A_page_on_this_host_is_accepted_whatever_scheme_the_proxy_left()
	{
		using var phone = Authenticator(_alice);
		await RegisterAsync(_alice, phone);

		var challenge = _passkeys.BeginSignIn(Request("http://play.example.test", originHeader: Origin)).Expect<PasskeyService.Challenge>();
		var signedIn = await _passkeys.CompleteSignInAsync(challenge.CeremonyId, phone.Get(challenge.Options));

		await Assert.That(signedIn.Expect<SharpAccount>().Id).IsEqualTo(_alice.Id);
	}

	[Test]
	public async Task A_configured_relying_party_id_scopes_passkeys_to_the_parent_domain()
	{
		var service = Service(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Passkeys:RelyingPartyId"] = "example.test",
		}).Build());

		var challenge = service.BeginSignIn(Request()).Expect<PasskeyService.Challenge>();

		await Assert.That(challenge.Options.GetProperty("rpId").GetString()).IsEqualTo("example.test");
	}
}
