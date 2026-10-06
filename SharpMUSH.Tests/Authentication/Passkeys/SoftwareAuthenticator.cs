using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUSH.Tests.Authentication.Passkeys;

/// <summary>
/// A passkey authenticator in software: one P-256 key, answering WebAuthn ceremonies the way a browser
/// passes them on, as WebAuthn's JSON form. Attestation is "none", as the server asks for.
/// </summary>
internal sealed class SoftwareAuthenticator(string origin, byte[] userHandle) : IDisposable
{
	private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
	private uint _signCount;

	public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);

	/// <summary>The handle the authenticator answers sign-ins with; a real one keeps what registration gave it.</summary>
	public byte[] UserHandle { get; set; } = userHandle;

	/// <summary>The origin written into the client data, as the browser would for the page it runs on.</summary>
	public string Origin { get; set; } = origin;

	private const byte UserPresent = 0x01, UserVerified = 0x04, AttestedData = 0x40;

	/// <summary>Answers registration options (<c>navigator.credentials.create</c>).</summary>
	public JsonElement Create(JsonElement options)
	{
		var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
		var clientData = ClientData("webauthn.create", options.GetProperty("challenge").GetString()!);

		var authData = new List<byte>();
		authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
		authData.Add(UserPresent | UserVerified | AttestedData);
		authData.AddRange(BigEndian(_signCount));
		authData.AddRange(new byte[16]); // AAGUID
		authData.Add((byte)(CredentialId.Length >> 8));
		authData.Add((byte)CredentialId.Length);
		authData.AddRange(CredentialId);
		authData.AddRange(CoseKey());

		var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
		attestation.WriteStartMap(3);
		attestation.WriteTextString("fmt");
		attestation.WriteTextString("none");
		attestation.WriteTextString("attStmt");
		attestation.WriteStartMap(0);
		attestation.WriteEndMap();
		attestation.WriteTextString("authData");
		attestation.WriteByteString([.. authData]);
		attestation.WriteEndMap();

		return Json(new JsonObject
		{
			["id"] = Base64Url.EncodeToString(CredentialId),
			["rawId"] = Base64Url.EncodeToString(CredentialId),
			["type"] = "public-key",
			["response"] = new JsonObject
			{
				["clientDataJSON"] = Base64Url.EncodeToString(clientData),
				["attestationObject"] = Base64Url.EncodeToString(attestation.Encode()),
				["transports"] = new JsonArray("internal"),
			},
			["clientExtensionResults"] = new JsonObject(),
		});
	}

	/// <summary>Answers sign-in options (<c>navigator.credentials.get</c>).</summary>
	public JsonElement Get(JsonElement options)
	{
		var rpId = options.GetProperty("rpId").GetString()!;
		var clientData = ClientData("webauthn.get", options.GetProperty("challenge").GetString()!);

		_signCount++;
		var authData = new List<byte>();
		authData.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
		authData.Add(UserPresent | UserVerified);
		authData.AddRange(BigEndian(_signCount));

		byte[] signed = [.. authData, .. SHA256.HashData(clientData)];
		var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

		return Json(new JsonObject
		{
			["id"] = Base64Url.EncodeToString(CredentialId),
			["rawId"] = Base64Url.EncodeToString(CredentialId),
			["type"] = "public-key",
			["response"] = new JsonObject
			{
				["clientDataJSON"] = Base64Url.EncodeToString(clientData),
				["authenticatorData"] = Base64Url.EncodeToString([.. authData]),
				["signature"] = Base64Url.EncodeToString(signature),
				["userHandle"] = Base64Url.EncodeToString(UserHandle),
			},
			["clientExtensionResults"] = new JsonObject(),
		});
	}

	private byte[] ClientData(string type, string challenge) => JsonSerializer.SerializeToUtf8Bytes(new JsonObject
	{
		["type"] = type,
		["challenge"] = challenge,
		["origin"] = Origin,
		["crossOrigin"] = false,
	});

	private byte[] CoseKey()
	{
		var parameters = _key.ExportParameters(includePrivateParameters: false);
		var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
		cose.WriteStartMap(5);
		cose.WriteInt32(1); cose.WriteInt32(2); // kty: EC2
		cose.WriteInt32(3); cose.WriteInt32(-7); // alg: ES256
		cose.WriteInt32(-1); cose.WriteInt32(1); // crv: P-256
		cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X!);
		cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y!);
		cose.WriteEndMap();
		return cose.Encode();
	}

	private static byte[] BigEndian(uint value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

	private static JsonElement Json(JsonNode node) => JsonSerializer.SerializeToElement(node);

	public void Dispose() => _key.Dispose();
}
