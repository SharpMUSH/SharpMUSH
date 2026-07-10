using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace SharpMUSH.LanguageServer;

/// <summary>
/// Shared definition of which files are SharpMUSH softcode: per-line command files
/// (<c>.mush</c>/<c>.mu</c>), function files (<c>.mushfn</c>/<c>.fun</c>), and command-list
/// files (<c>.mushcmd</c>). The parse mode is resolved per file by extension (see
/// <see cref="CodeAnalysis.MushParseMode.ForFileName"/>).
/// </summary>
internal static class MushDocument
{
	public static TextDocumentSelector Selector =>
		TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu", "**/*.mushfn", "**/*.fun", "**/*.mushcmd");
}
