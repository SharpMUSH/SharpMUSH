using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class TelnetTlsCertificateTests
{
	private static readonly DateTimeOffset IssuedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private sealed class Files : IDisposable
	{
		private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("sharpmush-telnet-tls-");
		private int _writes;

		public string Certificate => Path.Combine(_directory.FullName, "telnet.crt");
		public string Key => Path.Combine(_directory.FullName, "telnet.key");

		public TelnetTlsCertificate Reader() => new(Certificate, Key, NullLogger<TelnetTlsCertificate>.Instance);

		// Each write gets its own modification time, so a change is seen however coarse the file system's clock is.
		public void Write(string path, string contents)
		{
			File.WriteAllText(path, contents);
			File.SetLastWriteTimeUtc(path, IssuedAt.UtcDateTime.AddMinutes(++_writes));
		}

		public void Write(X509Certificate2 leaf, params X509Certificate2[] intermediates)
		{
			Write(Certificate, string.Concat(new[] { leaf }.Concat(intermediates).Select(c => c.ExportCertificatePem() + "\n")));
			Write(Key, leaf.GetECDsaPrivateKey()!.ExportPkcs8PrivateKeyPem());
		}

		public void Dispose() => _directory.Delete(recursive: true);
	}

	private static X509Certificate2 SelfSigned(string name)
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
		return request.CreateSelfSigned(IssuedAt, IssuedAt.AddDays(90));
	}

	private static X509Certificate2 SignedBy(X509Certificate2 issuer, string name, bool authority = false)
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
		if (authority)
			request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
		using var signed = request.Create(issuer, IssuedAt, IssuedAt.AddDays(90), [1, 2, 3, 4]);
		return signed.CopyWithPrivateKey(key);
	}

	private static X509Certificate2 Root(string name)
	{
		using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 1, true));
		return request.CreateSelfSigned(IssuedAt, IssuedAt.AddDays(365));
	}

	[Test]
	public async Task RefusesUntilTheFilesExist()
	{
		using var files = new Files();
		var reader = files.Reader();

		await Assert.That(reader.Current()).IsNull();

		using var leaf = SelfSigned("mush.example.com");
		files.Write(leaf);

		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=mush.example.com");
	}

	[Test]
	public async Task ServesARenewedCertificateWithoutARestart()
	{
		using var files = new Files();
		var reader = files.Reader();
		using var first = SelfSigned("first.example.com");
		using var renewed = SelfSigned("renewed.example.com");

		files.Write(first);
		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=first.example.com");

		files.Write(renewed);
		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=renewed.example.com");
	}

	// A copy that has replaced the certificate but not yet its key must not take TLS telnet down.
	[Test]
	public async Task KeepsTheLastGoodCertificateWhenTheNewPairDoesNotLoad()
	{
		using var files = new Files();
		var reader = files.Reader();
		using var first = SelfSigned("first.example.com");
		using var renewed = SelfSigned("renewed.example.com");

		files.Write(first);
		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=first.example.com");

		files.Write(files.Certificate, renewed.ExportCertificatePem());
		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=first.example.com");

		files.Write(files.Key, renewed.GetECDsaPrivateKey()!.ExportPkcs8PrivateKeyPem());
		await Assert.That(reader.Current()?.TargetCertificate.Subject).IsEqualTo("CN=renewed.example.com");
	}

	// As Let's Encrypt issues it: the leaf, then the intermediate that signed it, without the root.
	[Test]
	public async Task SendsTheIntermediatesAfterTheLeaf()
	{
		using var files = new Files();
		using var root = Root("Test Root");
		using var intermediate = SignedBy(root, "Test Intermediate", authority: true);
		using var leaf = SignedBy(intermediate, "mush.example.com");

		files.Write(leaf, intermediate);
		var current = files.Reader().Current();

		await Assert.That(current?.TargetCertificate.Thumbprint).IsEqualTo(leaf.Thumbprint);
		await Assert.That(current!.IntermediateCertificates.Select(c => c.Thumbprint)).IsEquivalentTo([intermediate.Thumbprint]);
	}
}
