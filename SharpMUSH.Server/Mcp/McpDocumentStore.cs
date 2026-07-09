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
		ArgumentNullException.ThrowIfNull(text);

		var id = Guid.NewGuid().ToString("N");
		_documents[id] = text;
		_insertionOrder.Enqueue(id);

		// Bound the insertion-order queue itself (not just the document map). Otherwise a client
		// that repeatedly opens and closes documents would leave the closed ids sitting in the
		// queue forever — the map stays small so the old "_documents.Count > Capacity" guard never
		// fired, and the queue grew without bound. Capping the queue evicts oldest ids (removing
		// any still-live document they point at) so both structures stay bounded under churn.
		while (_insertionOrder.Count > Capacity && _insertionOrder.TryDequeue(out var oldest))
		{
			_documents.TryRemove(oldest, out _);
		}

		return id;
	}

	public bool Close(string id) => _documents.TryRemove(id, out _);

	public bool TryGet(string id, out string text) => _documents.TryGetValue(id, out text!);
}
