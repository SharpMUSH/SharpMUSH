using System.Collections.Concurrent;

namespace SharpMUSH.Server.Mcp;

/// <summary>
/// Process-wide store backing the optional MCP document-session handles. A caller that runs
/// many queries over one piece of softcode can <c>open_document</c> once and pass the returned
/// id to subsequent tools instead of resending the text. Entries live until <c>close_document</c>
/// removes them; the store is capacity-bounded so an abandoned session cannot leak unboundedly.
/// </summary>
public sealed class McpDocumentStore
{
	private const int Capacity = 1024;
	private readonly ConcurrentDictionary<string, string> _documents = new();
	private readonly ConcurrentQueue<string> _insertionOrder = new();

	public string Open(string text)
	{
		var id = Guid.NewGuid().ToString("N");
		_documents[id] = text;
		_insertionOrder.Enqueue(id);

		// Evict oldest ids while over capacity so a client that never closes cannot grow the store forever.
		while (_documents.Count > Capacity && _insertionOrder.TryDequeue(out var oldest))
		{
			_documents.TryRemove(oldest, out _);
		}

		return id;
	}

	public bool Close(string id) => _documents.TryRemove(id, out _);

	public bool TryGet(string id, out string text) => _documents.TryGetValue(id, out text!);
}
