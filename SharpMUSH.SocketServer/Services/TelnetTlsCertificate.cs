using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The TLS telnet certificate, read from PEM files and read again on the first handshake after
/// either file changes, so a renewed certificate is served without restarting the socket owner and
/// dropping everyone. A pair that is missing or does not load keeps the last good certificate; with
/// none yet, handshakes are refused rather than served in plaintext.
/// </summary>
/// <remarks>
/// Each file is read once per change, not once per handshake: a failed load records the files it
/// saw, so it logs once and tries again only when one of them changes. The certificate file may
/// carry intermediates after the leaf, as Caddy and Certbot write it; they are sent with the leaf.
/// </remarks>
public sealed class TelnetTlsCertificate(string certificatePath, string? keyPath, ILogger<TelnetTlsCertificate> logger)
{
	private readonly Lock _gate = new();
	private FileStamp? _seen;
	private SslStreamCertificateContext? _current;

	public SslStreamCertificateContext? Current()
	{
		lock (_gate)
		{
			var stamp = new FileStamp(Stamp(certificatePath), keyPath is null ? default : Stamp(keyPath));
			if (stamp == _seen)
				return _current;

			_seen = stamp;
			try
			{
				_current = Load();
				logger.LogInformation("Loaded the TLS telnet certificate for {Name}, valid until {NotAfter:u}.",
					_current.TargetCertificate.GetNameInfo(X509NameType.DnsName, false), _current.TargetCertificate.NotAfter.ToUniversalTime());
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
			{
				logger.LogWarning(ex, "Could not load the TLS telnet certificate from {CertificatePath}; {State}.",
					certificatePath, _current is null ? "TLS telnet handshakes are refused until it loads" : "still serving the previous one");
			}

			return _current;
		}
	}

	private SslStreamCertificateContext Load()
	{
		var leaf = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
		var bundle = new X509Certificate2Collection();
		bundle.ImportFromPemFile(certificatePath);
		var intermediates = new X509Certificate2Collection(bundle.Where(c => c.Thumbprint != leaf.Thumbprint).ToArray());
		return SslStreamCertificateContext.Create(leaf, intermediates, offline: true);
	}

	private static (DateTime Written, long Length) Stamp(string path)
	{
		var file = new FileInfo(path);
		return file.Exists ? (file.LastWriteTimeUtc, file.Length) : default;
	}

	private readonly record struct FileStamp((DateTime Written, long Length) Certificate, (DateTime Written, long Length) Key);
}
