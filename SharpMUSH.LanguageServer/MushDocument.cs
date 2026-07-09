using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpMUSH.LanguageServer;

/// <summary>
/// Shared definition of which files are SharpMUSH softcode. Command-list files
/// (<c>.mush</c>/<c>.mu</c> batches) and function files (<c>.mushfn</c>/<c>.fun</c>) are both
/// served; the parse mode is resolved per file by extension (see
/// <see cref="CodeAnalysis.MushParseMode.ForFileName"/>).
/// </summary>
internal static class MushDocument
{
	public static TextDocumentSelector Selector =>
		TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu", "**/*.mushfn", "**/*.fun");
}
