using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles document formatting requests for MUSH code.
/// Delegates the actual formatting to the shared <see cref="IMushCodeAnalyzer"/> so the
/// LSP and the in-server MCP tools format identically, and emits one text edit per line
/// the analyzer changed.
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
			var originalLines = document.Text.Split('\n');
			var formattedLines = _analyzer.Format(document.Text).Split('\n');
			var edits = new List<TextEdit>();

			for (int i = 0; i < originalLines.Length && i < formattedLines.Length; i++)
			{
				if (formattedLines[i] != originalLines[i])
				{
					edits.Add(new TextEdit
					{
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, originalLines[i].Length)),
						NewText = formattedLines[i]
					});
				}
			}

			if (edits.Count > 0)
			{
				return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
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
