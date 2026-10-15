using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharpMUSH.Library.Push;

namespace SharpMUSH.Tests.Push;

/// <summary>
/// The message encryption (RFC 8291), the VAPID signature (RFC 8292) and the push-service allowlist. The encryption is
/// checked against RFC 8291's own worked example, byte for byte, and by decrypting as a browser would.
/// </summary>
public class WebPushCryptoTests
{
	// RFC 8291, Appendix A.
	private const string ServerPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
	private const string ServerPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
	private const string BrowserPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
	private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
	private const string Auth = "BTBZMqHH6r4Tts7J_aSIgg";
	private const string Plaintext = "When I grow up, I want to be a watermelon";
	private const string Body =
		"DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN";

	[Test]
	public async Task EncryptionReproducesTheRfcExample()
	{
		using var serverKey = ECDiffieHellman.Create(new ECParameters
		{
			Curve = ECCurve.NamedCurves.nistP256,
			D = Base64Url.DecodeFromChars(ServerPrivate),
			Q = VapidKeys.FromPublicPoint(Base64Url.DecodeFromChars(ServerPublic)).Q
		});

		var body = WebPushEncryption.Encrypt(Encoding.UTF8.GetBytes(Plaintext), Base64Url.DecodeFromChars(BrowserPublic),
			Base64Url.DecodeFromChars(Auth), serverKey, Base64Url.DecodeFromChars(Salt));

		await Assert.That(Base64Url.EncodeToString(body)).IsEqualTo(Body);
	}

	[Test]
	public async Task ABrowserDecryptsWhatIsSent()
	{
		using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
		var browserPublic = VapidKeys.PublicPoint(browser.ExportParameters(false));
		var auth = RandomNumberGenerator.GetBytes(16);
		var message = Encoding.UTF8.GetBytes("""{"title":"Page from Tobin","body":"Bring rope, and a lantern."}""");

		var body = WebPushEncryption.Encrypt(message, browserPublic, auth);

		await Assert.That(PushTestBrowser.Decrypt(body, browser, browserPublic, auth)).IsEquivalentTo(message);
	}

	[Test]
	public async Task AMessageTooLongForOneRecordIsRefused()
	{
		using var browser = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
		var browserPublic = VapidKeys.PublicPoint(browser.ExportParameters(false));

		await Assert.That(() => WebPushEncryption.Encrypt(new byte[WebPushEncryption.MaxPlaintext + 1], browserPublic,
			RandomNumberGenerator.GetBytes(16))).Throws<ArgumentOutOfRangeException>();
		await Assert.That(WebPushEncryption.Encrypt(new byte[WebPushEncryption.MaxPlaintext], browserPublic,
			RandomNumberGenerator.GetBytes(16)).Length).IsEqualTo(WebPushEncryption.RecordSize);
	}

	[Test]
	public async Task TheVapidTokenIsSignedForTheEndpointsOrigin()
	{
		using var keys = VapidKeys.Generate();
		var expires = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

		var header = keys.Authorization(new Uri("https://fcm.googleapis.com/fcm/send/abc123"), "mailto:admin@example.com", expires);

		await Assert.That(header).StartsWith("vapid t=");
		var parts = header["vapid ".Length..].Split(", ").Select(part => part.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
		await Assert.That(parts["k"]).IsEqualTo(keys.PublicKeyText);

		var token = parts["t"].Split('.');
		var claims = JsonDocument.Parse(Base64Url.DecodeFromChars(token[1])).RootElement;
		await Assert.That(claims.GetProperty("aud").GetString()).IsEqualTo("https://fcm.googleapis.com");
		await Assert.That(claims.GetProperty("sub").GetString()).IsEqualTo("mailto:admin@example.com");
		await Assert.That(claims.GetProperty("exp").GetInt64()).IsEqualTo(expires.ToUnixTimeSeconds());

		using var verifier = ECDsa.Create(VapidKeys.FromPublicPoint(Base64Url.DecodeFromChars(parts["k"])));
		var signed = verifier.VerifyData(Encoding.ASCII.GetBytes($"{token[0]}.{token[1]}"), Base64Url.DecodeFromChars(token[2]),
			HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
		await Assert.That(signed).IsTrue();
	}

	[Test]
	public async Task AStoredKeyReadsBackAsTheSameKey()
	{
		using var keys = VapidKeys.Generate();

		using var read = VapidKeys.Parse(keys.Export());

		await Assert.That(read).IsNotNull();
		await Assert.That(read!.PublicKeyText).IsEqualTo(keys.PublicKeyText);
		await Assert.That(VapidKeys.Parse("not a key")).IsNull();
	}

	[Test]
	[Arguments("https://fcm.googleapis.com/fcm/send/abc", true)]
	[Arguments("https://updates.push.services.mozilla.com/wpush/v2/abc", true)]
	[Arguments("https://web.push.apple.com/abc", true)]
	[Arguments("https://api.push.apple.com/abc", true)]
	[Arguments("https://db5p.notify.windows.com/w/?token=abc", true)]
	[Arguments("http://fcm.googleapis.com/fcm/send/abc", false)]
	[Arguments("https://user:pass@fcm.googleapis.com/fcm/send/abc", false)]
	[Arguments("https://fcm.googleapis.com.example.com/abc", false)]
	[Arguments("https://169.254.169.254/latest/meta-data", false)]
	[Arguments("https://push.example.org/abc", false)]
	public async Task OnlyKnownPushServicesAreAllowed(string endpoint, bool allowed)
		=> await Assert.That(PushEndpoints.IsAllowed(endpoint, [])).IsEqualTo(allowed);

	[Test]
	public async Task AnOperatorCanAllowAnotherPushService()
		=> await Assert.That(PushEndpoints.IsAllowed("https://push.example.org/abc", ["push.example.org"])).IsTrue();
}
