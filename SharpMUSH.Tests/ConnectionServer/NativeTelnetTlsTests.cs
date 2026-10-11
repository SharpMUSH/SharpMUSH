using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// Real Kestrel sockets, certificate loading and telnet handlers. Only the external broker, durable
/// store and descriptor allocator are replaced; no database, renderer or installed certificate is needed.
/// </summary>
public class NativeTelnetTlsTests
{
	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

	[Test]
	public async Task VerifiedTlsCarriesTelnetAndReportsSecureWhilePlaintextRemainsAvailable()
	{
		using var certificates = new TestCertificates();
		using var timeout = new CancellationTokenSource(Timeout);
		var tlsPort = GetUnusedPort();
		await using var server = await TestServer.StartAsync(tlsPort, certificates, timeout.Token);

		using (var client = new TcpClient())
		{
			await client.ConnectAsync(IPAddress.Loopback, tlsPort, timeout.Token);
			await using var stream = new SslStream(client.GetStream());
			await stream.AuthenticateAsClientAsync(certificates.ClientOptions(), timeout.Token);
			await Assert.That(stream.IsAuthenticated && stream.IsEncrypted).IsTrue();
			await Assert.That(stream.RemoteCertificate?.GetCertHashString()).IsEqualTo(certificates.Thumbprint);
			await ExchangeTelnetAsync(server, stream, isSecure: true, timeout.Token);
		}

		using var plaintext = new TcpClient();
		await plaintext.ConnectAsync(IPAddress.Loopback, server.PlaintextPort, timeout.Token);
		await ExchangeTelnetAsync(server, plaintext.GetStream(), isSecure: false, timeout.Token);
	}

	[Test]
	public async Task ZeroTlsPortStartsWithoutCertificateAndOpensOnlyPlaintextAndHttp()
	{
		using var timeout = new CancellationTokenSource(Timeout);
		await using var server = await TestServer.StartAsync(0, null, timeout.Token);
		await Assert.That(server.App.Urls.Count).IsEqualTo(2);
		await Assert.That(server.App.Urls.All(url => new Uri(url).Scheme == "http")).IsTrue();

		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, server.PlaintextPort, timeout.Token);
		await ExchangeTelnetAsync(server, client.GetStream(), isSecure: false, timeout.Token);
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task MissingCertificateOrKeyFailsStartupWithoutOpeningTlsPort(bool removeCertificate)
	{
		using var certificates = new TestCertificates();
		File.Delete(removeCertificate ? certificates.CertificatePath : certificates.KeyPath);
		using var timeout = new CancellationTokenSource(Timeout);
		var tlsPort = GetUnusedPort();

		await Assert.That(async () =>
		{
			await using var server = await TestServer.StartAsync(tlsPort, certificates, timeout.Token);
		}).Throws<FileNotFoundException>()
			.WithMessageContaining(removeCertificate ? certificates.CertificatePath : certificates.KeyPath);
		await AssertPortClosedAsync(tlsPort, timeout.Token);
	}

	[Test]
	public async Task MismatchedPrivateKeyFailsStartupWithoutOpeningTlsPort()
	{
		using var certificates = new TestCertificates();
		using var wrongKey = RSA.Create(2048);
		File.WriteAllText(certificates.KeyPath, wrongKey.ExportPkcs8PrivateKeyPem());
		using var timeout = new CancellationTokenSource(Timeout);
		var tlsPort = GetUnusedPort();

		await Assert.That(async () =>
		{
			await using var server = await TestServer.StartAsync(tlsPort, certificates, timeout.Token);
		}).Throws<InvalidOperationException>().WithMessageContaining(certificates.KeyPath);
		await AssertPortClosedAsync(tlsPort, timeout.Token);
	}

	[Test]
	[Arguments("wrong-host.invalid", true)]
	[Arguments("localhost", false)]
	public async Task ClientRejectsWrongHostnameOrUntrustedCertificate(string hostname, bool trustIssuer)
	{
		using var certificates = new TestCertificates();
		using var timeout = new CancellationTokenSource(Timeout);
		var tlsPort = GetUnusedPort();
		await using var server = await TestServer.StartAsync(tlsPort, certificates, timeout.Token);
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, tlsPort, timeout.Token);
		await using var stream = new SslStream(client.GetStream());

		await Assert.That(async () => await stream.AuthenticateAsClientAsync(
			certificates.ClientOptions(hostname, trustIssuer), timeout.Token)).Throws<AuthenticationException>();
		await Assert.That(stream.IsAuthenticated).IsFalse();
	}

	[Test]
	public async Task ReplacingPemFilesAndRestartingPresentsTheNewCertificate()
	{
		using var certificates = new TestCertificates();
		using var timeout = new CancellationTokenSource(Timeout);
		var tlsPort = GetUnusedPort();
		var original = certificates.Thumbprint;
		await using (var server = await TestServer.StartAsync(tlsPort, certificates, timeout.Token))
		{
			await VerifyCertificateAndTelnetAsync(server, tlsPort, certificates, timeout.Token);
		}

		// Same configured paths and port, as in the operator's stop/refresh/start procedure.
		certificates.ReplaceLeaf();
		await Assert.That(certificates.Thumbprint).IsNotEqualTo(original);
		await using var restarted = await TestServer.StartAsync(tlsPort, certificates, timeout.Token);
		await VerifyCertificateAndTelnetAsync(restarted, tlsPort, certificates, timeout.Token);
	}

	private static async Task VerifyCertificateAndTelnetAsync(
		TestServer server, int tlsPort, TestCertificates certificates, CancellationToken ct)
	{
		using var client = new TcpClient();
		await client.ConnectAsync(IPAddress.Loopback, tlsPort, ct);
		await using var stream = new SslStream(client.GetStream());
		await stream.AuthenticateAsClientAsync(certificates.ClientOptions(), ct);
		await Assert.That(stream.RemoteCertificate?.GetCertHashString()).IsEqualTo(certificates.Thumbprint);
		await ExchangeTelnetAsync(server, stream, isSecure: true, ct);
	}

	private static async Task ExchangeTelnetAsync(TestServer server, Stream stream, bool isSecure, CancellationToken ct)
	{
		var established = await server.Backend.Established.Reader.ReadAsync(ct);
		await Assert.That(established.ConnectionType).IsEqualTo("telnet");
		await Assert.That(established.IsSecure).IsEqualTo(isSecure);
		await Assert.That(server.Backend.Persisted[established.Handle].Metadata["SSL"])
			.IsEqualTo(isSecure ? "1" : "0");

		// Read an actual telnet negotiation, then submit a line to the production input handler.
		await ReadThroughAsync(stream, [255, 253, 24], ct); // IAC DO TTYPE
		await stream.WriteAsync("look\r\n"u8.ToArray(), ct);
		var input = await server.Backend.Input.Reader.ReadAsync(ct);
		await Assert.That(input.Handle).IsEqualTo(established.Handle);
		await Assert.That(input.SessionId).IsEqualTo(established.SessionId);
		await Assert.That(input.Input).IsEqualTo("look");

		var connection = server.App.Services.GetRequiredService<IConnectionServerService>().Get(established.Handle)
			?? throw new InvalidOperationException("The telnet connection was not registered.");
		await connection.OutputFunction("tls-regression-output"u8.ToArray()).AsTask().WaitAsync(ct);
		await ReadThroughAsync(stream, "tls-regression-output"u8.ToArray(), ct);
	}

	private static async Task ReadThroughAsync(Stream stream, byte[] marker, CancellationToken ct)
	{
		var seen = new List<byte>();
		var buffer = new byte[4096];
		while (true)
		{
			var length = await stream.ReadAsync(buffer, ct);
			if (length == 0) throw new IOException("The socket closed before the expected telnet bytes arrived.");
			seen.AddRange(buffer.AsSpan(0, length).ToArray());
			if (seen.ToArray().AsSpan().IndexOf(marker) >= 0) return;
			if (seen.Count > 65536) throw new IOException("Expected telnet bytes did not arrive within 64 KiB.");
		}
	}

	private static int GetUnusedPort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}

	private static async Task AssertPortClosedAsync(int port, CancellationToken ct)
	{
		using var client = new TcpClient();
		await Assert.That(async () => await client.ConnectAsync(IPAddress.Loopback, port, ct)).Throws<SocketException>();
	}

	private sealed class TestServer(WebApplication app, TestBackend backend) : IAsyncDisposable
	{
		public WebApplication App { get; } = app;
		public TestBackend Backend { get; } = backend;
		// Program registers plaintext telnet first, then optional TLS, then HTTP.
		public int PlaintextPort => new Uri(App.Urls.First()).Port;

		public static async Task<TestServer> StartAsync(int tlsPort, TestCertificates? certificates, CancellationToken ct)
		{
			var backend = new TestBackend();
			var args = new List<string>(TestDiagnostics.HostArguments)
			{
				"--environment=Production",
				"--ConnectionServer:TelnetPort=0",
				"--ConnectionServer:HttpPort=0",
				"--ConnectionServer:MetricsPort=0",
				$"--ConnectionServer:TelnetSslPort={tlsPort}"
			};
			if (certificates is not null)
			{
				args.Add($"--Kestrel:Certificates:Default:Path={certificates.CertificatePath}");
				args.Add($"--Kestrel:Certificates:Default:KeyPath={certificates.KeyPath}");
			}

			var app = await SharpMUSH.SocketServer.Program.CreateHostBuilderAsync(
				[.. args], configureServices: backend.Configure);
			try
			{
				await app.StartAsync(ct);
				return new TestServer(app, backend);
			}
			catch
			{
				await app.DisposeAsync();
				throw;
			}
		}

		public async ValueTask DisposeAsync()
		{
			using var shutdown = new CancellationTokenSource(Timeout);
			try { await App.StopAsync(shutdown.Token); }
			finally { await App.DisposeAsync(); }
		}
	}

	private sealed class TestBackend : IMessageBus
	{
		public Channel<ConnectionEstablishedMessage> Established { get; } = Channel.CreateUnbounded<ConnectionEstablishedMessage>();
		public Channel<TelnetInputMessage> Input { get; } = Channel.CreateUnbounded<TelnetInputMessage>();
		public ConcurrentDictionary<long, ConnectionStateData> Persisted { get; } = new();

		public void Configure(IServiceCollection services)
		{
			// Remove background broker/health/cleanup work before WebApplication.Build adds its own
			// web-host service. Kestrel and the real TelnetServer/ConnectionServerService remain intact.
			services.RemoveAll<IHostedService>();
			services.RemoveAll<IMessageBus>();
			services.AddSingleton<IMessageBus>(this);

			var state = Substitute.For<IConnectionStateStore>();
			state.SetConnectionAsync(Arg.Any<long>(), Arg.Any<ConnectionStateData>(), Arg.Any<CancellationToken>())
				.Returns(call =>
				{
					Persisted[call.Arg<long>()] = call.Arg<ConnectionStateData>();
					return Task.CompletedTask;
				});
			services.RemoveAll<IConnectionStateStore>();
			services.AddSingleton(state);

			var nextHandle = 0L;
			var descriptors = Substitute.For<IDescriptorGeneratorService>();
			descriptors.GetNextTelnetDescriptorAsync(Arg.Any<CancellationToken>())
				.Returns(_ => Interlocked.Increment(ref nextHandle));
			services.RemoveAll<IDescriptorGeneratorService>();
			services.AddSingleton(descriptors);
		}

		public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
		{
			if (message is ConnectionEstablishedMessage established) Established.Writer.TryWrite(established);
			if (message is TelnetInputMessage input) Input.Writer.TryWrite(input);
			return Task.CompletedTask;
		}

		public Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
			=> Task.CompletedTask;
	}

	private sealed class TestCertificates : IDisposable
	{
		private readonly string _directory = Path.Combine(Path.GetTempPath(), "sharpmush-tls-" + Guid.NewGuid().ToString("N"));
		private readonly RSA _rootKey = RSA.Create(2048);
		private readonly RSA _issuerKey = RSA.Create(2048);
		private readonly X509Certificate2 _root;
		private readonly X509Certificate2 _issuer;
		public string CertificatePath => Path.Combine(_directory, "fullchain.pem");
		public string KeyPath => Path.Combine(_directory, "privkey.pem");
		public string Thumbprint { get; private set; } = string.Empty;

		public TestCertificates()
		{
			Directory.CreateDirectory(_directory);
			var rootRequest = new CertificateRequest("CN=SharpMUSH isolated TLS test root", _rootKey,
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
			rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
			rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
			_root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(3));

			var issuerRequest = new CertificateRequest("CN=SharpMUSH isolated TLS test intermediate", _issuerKey,
				HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			issuerRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
			issuerRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
			issuerRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(issuerRequest.PublicKey, false));
			using var issuer = issuerRequest.Create(_root, DateTimeOffset.UtcNow.AddHours(-2),
				DateTimeOffset.UtcNow.AddDays(2), RandomNumberGenerator.GetBytes(16));
			_issuer = issuer.CopyWithPrivateKey(_issuerKey);
			ReplaceLeaf();
		}

		public void ReplaceLeaf()
		{
			using var key = RSA.Create(2048);
			var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
			var names = new SubjectAlternativeNameBuilder();
			names.AddDnsName("localhost");
			request.CertificateExtensions.Add(names.Build());
			request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
			request.CertificateExtensions.Add(new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
			request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
				new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
			using var leaf = request.Create(_issuer, DateTimeOffset.UtcNow.AddHours(-1),
				DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
			File.WriteAllText(CertificatePath, leaf.ExportCertificatePem() + "\n" + _issuer.ExportCertificatePem());
			File.WriteAllText(KeyPath, key.ExportPkcs8PrivateKeyPem());
			Thumbprint = leaf.Thumbprint;
		}

		public SslClientAuthenticationOptions ClientOptions(string hostname = "localhost", bool trustIssuer = true)
		{
			var policy = new X509ChainPolicy
			{
				TrustMode = X509ChainTrustMode.CustomRootTrust,
				RevocationMode = X509RevocationMode.NoCheck,
				DisableCertificateDownloads = true
			};
			// Trust only the root. The intermediate must arrive from Kestrel's fullchain.pem.
			if (trustIssuer) policy.CustomTrustStore.Add(_root);
			// No validation callback: SslStream must check both the trust chain and the requested SAN.
			return new SslClientAuthenticationOptions
			{
				TargetHost = hostname,
				CertificateChainPolicy = policy
			};
		}

		public void Dispose()
		{
			_issuer.Dispose();
			_root.Dispose();
			_issuerKey.Dispose();
			_rootKey.Dispose();
			Directory.Delete(_directory, recursive: true);
		}
	}
}
