namespace SharpMUSH.Plugins.Lsp;

public class LspOptions
{
	public bool Enabled { get; set; }
	public string Path { get; set; } = "/lsp";
	public int MaxDocumentBytes { get; set; } = 5 * 1024 * 1024;
	public int MaxMessageBytes { get; set; } = 32 * 1024 * 1024;
	public int MaxResponseBytes { get; set; } = 1024 * 1024;
	public int MaxSessions { get; set; } = 16;
	public int MaxSessionsPerPrincipal { get; set; } = 2;
	public int MaxDocumentsPerSession { get; set; } = 8;
	public long MaxBytesPerSession { get; set; } = 32 * 1024 * 1024;
	public long MaxStoredBytes { get; set; } = 128 * 1024 * 1024;
	public int MaxPendingRequests { get; set; } = 8;
	public int MaxConcurrentReaders { get; set; } = 2;
	public TimeSpan MessageReadTimeout { get; set; } = TimeSpan.FromSeconds(10);
	public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
	public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(15);
}
