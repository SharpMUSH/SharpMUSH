using System.Collections.Concurrent;

namespace SharpMUSH.LanguageServer.Services;

/// <summary>
/// Manages MUSH document state and versioning for the LSP server.
/// </summary>
public class DocumentManager
{
	private readonly ConcurrentDictionary<string, DocumentState> _documents = new();

	public void OpenDocument(string uri, string text, int version)
	{
		_documents[uri] = new DocumentState(text, version);
	}

	public void UpdateDocument(string uri, string text, int version)
	{
		_documents.AddOrUpdate(uri, 
			new DocumentState(text, version),
			(_, _) => new DocumentState(text, version));
	}

	public void CloseDocument(string uri)
	{
		_documents.TryRemove(uri, out _);
	}

	public DocumentState? GetDocument(string uri)
	{
		return _documents.TryGetValue(uri, out var doc) ? doc : null;
	}

	public bool HasDocument(string uri)
	{
		return _documents.ContainsKey(uri);
	}

	public IEnumerable<(string uri, DocumentState document)> GetAllDocuments()
	{
		return _documents.Select(kvp => (kvp.Key, kvp.Value));
	}
}

/// <summary>
/// Represents the state of a MUSH document.
/// </summary>
public record DocumentState
{
	public string Text { get; init; }
	public int Version { get; init; }

	public DocumentState(string text, int version)
	{
		Text = text;
		Version = version;
	}
}
