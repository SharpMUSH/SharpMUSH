using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Mcp;

/// <summary>
/// Principal-owned temporary documents. Admission, expiry and quota accounting are atomic;
/// pressure rejects new documents instead of evicting another character's work.
/// </summary>
public sealed class McpDocumentStore
{
	private sealed record Owner(int Key, long CreationTime, string? Account);
	private sealed class Entry(Owner owner, string text, int bytes, DateTimeOffset accessed)
	{
		public Owner Owner { get; } = owner;
		public string Text { get; } = text;
		public int Bytes { get; } = bytes;
		public DateTimeOffset Accessed { get; set; } = accessed;
	}

	private readonly object _gate = new();
	private readonly Dictionary<string, Entry> _documents = new();
	private readonly McpOptions _options;
	private readonly IHttpContextAccessor _context;
	private readonly TimeProvider _clock;
	private long _storedBytes;

	public McpDocumentStore(IOptions<McpOptions> options, IHttpContextAccessor context, TimeProvider clock)
	{
		_options = options.Value;
		_context = context;
		_clock = clock;
		if (_options.MaxDocumentBytes <= 0 || _options.MaxDocumentsPerPrincipal <= 0 ||
			_options.MaxBytesPerPrincipal <= 0 || _options.MaxStoredDocuments <= 0 ||
			_options.MaxStoredBytes <= 0 || _options.DocumentIdleTimeout <= TimeSpan.Zero)
			throw new ArgumentException("MCP document budgets and idle timeout must be positive.", nameof(options));
	}

	/// <summary>Also used for inline tool code, before invoking analysis.</summary>
	public void ValidateText(string text) => TextBytes(text);

	private int TextBytes(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		// A quick UTF-16 lower bound avoids scanning obviously oversized strings.
		if (text.Length > _options.MaxDocumentBytes)
			throw new ArgumentException("MCP document exceeds the configured UTF-8 byte limit.", nameof(text));
		var bytes = Encoding.UTF8.GetByteCount(text);
		if (bytes > _options.MaxDocumentBytes)
			throw new ArgumentException("MCP document exceeds the configured UTF-8 byte limit.", nameof(text));
		return bytes;
	}

	private Owner CurrentOwner()
	{
		// Read claims from the authenticated scheme's identity, never aggregated identities or tool input.
		var identity = _context.HttpContext?.User.Identities.FirstOrDefault(identity =>
			identity.IsAuthenticated && identity.AuthenticationType == AnalysisTransportSecurity.AuthenticationScheme);
		if (identity is null ||
			!int.TryParse(identity.FindFirst("character_key")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var key) ||
			!long.TryParse(identity.FindFirst("character_creation_time")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var created))
			throw new UnauthorizedAccessException("Authenticated character identity is required.");
		return new Owner(key, created, identity.FindFirst(ClaimTypes.NameIdentifier)?.Value);
	}

	private void Remove(string id, Entry entry)
	{
		_documents.Remove(id);
		_storedBytes -= entry.Bytes;
	}

	private void Expire(DateTimeOffset now)
	{
		foreach (var (id, entry) in _documents.ToArray())
			if (now - entry.Accessed >= _options.DocumentIdleTimeout)
				Remove(id, entry);
	}

	public string Open(string text)
	{
		var owner = CurrentOwner();
		var bytes = TextBytes(text);
		lock (_gate)
		{
			var now = _clock.GetUtcNow();
			Expire(now);
			var owned = _documents.Values.Where(entry => entry.Owner == owner).ToArray();
			if (owned.Length >= _options.MaxDocumentsPerPrincipal ||
				owned.Sum(entry => (long)entry.Bytes) + bytes > _options.MaxBytesPerPrincipal ||
				_documents.Count >= _options.MaxStoredDocuments || _storedBytes + bytes > _options.MaxStoredBytes)
				throw new ArgumentException("MCP document storage budget exceeded. Close documents or retry after expiry.");
			var id = Guid.NewGuid().ToString("N");
			_documents.Add(id, new Entry(owner, text, bytes, now));
			_storedBytes += bytes;
			return id;
		}
	}

	public bool Close(string id)
	{
		var owner = CurrentOwner();
		lock (_gate)
		{
			Expire(_clock.GetUtcNow());
			if (!_documents.TryGetValue(id, out var entry) || entry.Owner != owner) return false;
			Remove(id, entry);
			return true;
		}
	}

	public bool TryGet(string id, out string text)
	{
		var owner = CurrentOwner();
		lock (_gate)
		{
			var now = _clock.GetUtcNow();
			Expire(now);
			if (!_documents.TryGetValue(id, out var entry) || entry.Owner != owner)
			{
				text = null!;
				return false;
			}
			entry.Accessed = now;
			text = entry.Text;
			return true;
		}
	}
}
