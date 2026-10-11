using System.Text;
using Microsoft.Extensions.Options;

namespace SharpMUSH.Plugins.Lsp;

/// <summary>Atomic session/document quotas; URIs are keys, never server paths.</summary>
public sealed class LspResources : IDisposable
{
	internal readonly object Gate = new();
	internal readonly LspOptions Options;
	private readonly Dictionary<LspSessionDocuments, string> _sessions = [];
	private readonly Dictionary<string, int> _principals = new(StringComparer.Ordinal);
	internal long StoredBytes;
	private bool _disposed;
	public SemaphoreSlim Readers { get; }

	public LspResources(IOptions<LspOptions> options)
	{
		Options = options.Value;
		if (new[] { Options.MaxDocumentBytes, Options.MaxMessageBytes, Options.MaxResponseBytes, Options.MaxSessions,
			Options.MaxSessionsPerPrincipal, Options.MaxDocumentsPerSession, Options.MaxPendingRequests, Options.MaxConcurrentReaders }.Any(value => value <= 0) ||
			Options.MaxBytesPerSession <= 0 || Options.MaxStoredBytes <= 0 ||
			new[] { Options.MessageReadTimeout, Options.IdleTimeout, Options.SessionTimeout }.Any(value => value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1))
			throw new ArgumentException("LSP limits and deadlines must be positive and finite.", nameof(options));
		Readers = new SemaphoreSlim(Options.MaxConcurrentReaders);
	}

	private sealed class ReaderLease(SemaphoreSlim semaphore) : IDisposable
	{
		public void Dispose() => semaphore.Release();
	}
	internal async Task<IDisposable> AcquireReaderAsync(CancellationToken token)
	{
		await Readers.WaitAsync(token);
		return new ReaderLease(Readers);
	}

	public LspSessionDocuments OpenSession(string principal)
	{
		lock (Gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_sessions.Count >= Options.MaxSessions || _principals.GetValueOrDefault(principal) >= Options.MaxSessionsPerPrincipal)
				throw new InvalidOperationException("LSP session capacity exceeded.");
			var session = new LspSessionDocuments(this);
			_sessions.Add(session, principal);
			_principals[principal] = _principals.GetValueOrDefault(principal) + 1;
			return session;
		}
	}

	internal void Remove(LspSessionDocuments session, long bytes)
	{
		lock (Gate)
		{
			if (!_sessions.Remove(session, out var principal)) return;
			StoredBytes -= bytes;
			if (--_principals[principal] == 0) _principals.Remove(principal);
			if (_disposed && _sessions.Count == 0) Readers.Dispose();
		}
	}

	public void Dispose()
	{
		lock (Gate)
		{
			if (_disposed) return;
			_disposed = true;
			if (_sessions.Count == 0) Readers.Dispose();
		}
	}
}

public sealed record LspDocument(string Text, int Version, long Generation);

public sealed class LspSessionDocuments : IDisposable
{
	private readonly LspResources _resources;
	private readonly Dictionary<string, (LspDocument Document, int Bytes)> _documents = new(StringComparer.Ordinal);
	private long _bytes;
	private long _generation;
	private bool _disposed;
	internal LspSessionDocuments(LspResources resources) => _resources = resources;

	private int TextBytes(string uri, string text)
	{
		if (uri.Length > 4096 || !Uri.TryCreate(uri, UriKind.Absolute, out _)) throw new ArgumentException("Invalid document URI.");
		if (text.Length > _resources.Options.MaxDocumentBytes) throw new ArgumentException("LSP document exceeds UTF-8 limit.");
		var bytes = Encoding.UTF8.GetByteCount(text);
		if (bytes > _resources.Options.MaxDocumentBytes) throw new ArgumentException("LSP document exceeds UTF-8 limit.");
		return bytes;
	}

	private void Set(string uri, string text, int version, int bytes, int previousBytes)
	{
		var delta = (long)bytes - previousBytes;
		if (_bytes + delta > _resources.Options.MaxBytesPerSession || _resources.StoredBytes + delta > _resources.Options.MaxStoredBytes)
			throw new ArgumentException("LSP document storage capacity exceeded.");
		_documents[uri] = (new LspDocument(text, version, ++_generation), bytes);
		_bytes += delta;
		_resources.StoredBytes += delta;
	}

	public void Open(string uri, string text, int version)
	{
		var bytes = TextBytes(uri, text);
		lock (_resources.Gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_documents.ContainsKey(uri) || _documents.Count >= _resources.Options.MaxDocumentsPerSession) throw new ArgumentException("LSP document already open or count limit reached.");
			Set(uri, text, version, bytes, 0);
		}
	}

	public bool Change(string uri, string text, int version)
	{
		var bytes = TextBytes(uri, text);
		lock (_resources.Gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (!_documents.TryGetValue(uri, out var previous)) throw new ArgumentException("LSP document is not open.");
			if (version <= previous.Document.Version) return false;
			Set(uri, text, version, bytes, previous.Bytes);
			return true;
		}
	}

	public LspDocument? Get(string uri)
	{
		lock (_resources.Gate) return _disposed ? null : _documents.GetValueOrDefault(uri).Document;
	}

	public void Close(string uri)
	{
		lock (_resources.Gate)
		{
			if (!_documents.Remove(uri, out var previous)) return;
			_bytes -= previous.Bytes;
			_resources.StoredBytes -= previous.Bytes;
		}
	}

	public void Dispose()
	{
		lock (_resources.Gate)
		{
			if (_disposed) return;
			_disposed = true;
			_documents.Clear();
			_resources.Remove(this, _bytes);
			_bytes = 0;
		}
	}
}
