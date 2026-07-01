using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SharpMUSH.ConnectionServer;

/// <summary>
/// Generates a self-signed ECDSA P-256 certificate valid for &lt;14 days so a browser accepts it via
/// <c>serverCertificateHashes</c> for local WebTransport dev. The SHA-256 fingerprint is written to
/// stdout so the client can pin it. Not for production — use a real trusted certificate there.
/// </summary>
public static class WebTransportDevCert
{
	public static X509Certificate2 Generate()
	{
		using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest("CN=localhost", ec, HashAlgorithmName.SHA256);

		var san = new SubjectAlternativeNameBuilder();
		san.AddDnsName("localhost");
		san.AddIpAddress(IPAddress.Loopback);
		request.CertificateExtensions.Add(san.Build());

		var now = DateTimeOffset.UtcNow;
		using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(13));

		var fingerprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
		Console.WriteLine($"[WebTransport] dev cert SHA-256: {fingerprint}");

		return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pkcs12), null);
	}
}
