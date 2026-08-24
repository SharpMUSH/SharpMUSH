using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles document formatting requests for MUSH code.
/// Delegates the actual formatting to the shared <see cref="IMushCodeAnalyzer"/> so the
/// LSP and the in-server MCP tools are backed by the same softcode intelligence, and emits a
/// single whole-document text edit built from <see cref="IMushCodeAnalyzer.FormatIndented"/> —
/// not <see cref="IMushCodeAnalyzer.Format"/>, whose line-preserving contract the MCP
/// <c>format</c> tool depends on and which therefore cannot express indentation (a per-line edit
/// structurally requires the line count to stay fixed).
/// <para>
/// Passes <see cref="MushParseMode.ForFileName"/> for the document's URI — the same per-document
/// dialect channel <see cref="SemanticTokensHandler"/> already uses — rather than
/// <c>FormatIndented</c>'s own conservative default, so a <c>.mushcmd</c>/<c>.mush</c> document's
/// root <c>;</c> actually breaks instead of staying on one long line.
/// </para>
/// </summary>
public class DocumentFormattingHandler : DocumentFormattingHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMushCodeAnalyzer _analyzer;

	public DocumentFormattingHandler(DocumentManager documentManager, IMushCodeAnalyzer analyzer)
	{
		_documentManager = documentManager;
		_analyzer = analyzer;
	}

	public override Task<TextEditContainer?> Handle(
		DocumentFormattingParams request,
		CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<TextEditContainer?>(null);
		}

		try
		{
			var original = document.Text;
			var mode = MushParseMode.ForFileName(uri);
			var formatted = _analyzer.FormatIndented(original, mode: mode);

			if (formatted != original)
			{
				var originalLines = original.Split('\n');
				var lastLine = originalLines.Length - 1;
				// A trailing CR is part of the line terminator in LSP positions, not the line
				// content — exclude it from the end position so a CRLF document isn't off-by-one.
				var endCharacter = originalLines[lastLine].TrimEnd('\r').Length;

				var edit = new TextEdit
				{
					Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
						new Position(0, 0),
						new Position(lastLine, endCharacter)),
					NewText = formatted
				};

				return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error formatting document: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult<TextEditContainer?>(null);
	}

	protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
		DocumentFormattingCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new DocumentFormattingRegistrationOptions
		{
			DocumentSelector = MushDocument.Selector
		};
	}
}
