using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles hover requests to show information about MUSH code elements.
/// Delegates to the shared <see cref="IMushCodeAnalyzer"/> so the LSP and the in-server MCP
/// tools produce identical hover content.
/// </summary>
public class HoverHandler : HoverHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMushCodeAnalyzer _analyzer;

	public HoverHandler(DocumentManager documentManager, IMushCodeAnalyzer analyzer)
	{
		_documentManager = documentManager;
		_analyzer = analyzer;
	}

	public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<Hover?>(null);
		}

		var info = _analyzer.Hover(document.Text, (int)request.Position.Line, (int)request.Position.Character);
		if (info is null)
		{
			return Task.FromResult<Hover?>(null);
		}

		return Task.FromResult<Hover?>(new Hover
		{
			Contents = new MarkedStringsOrMarkupContent(new MarkupContent
			{
				Kind = MarkupKind.Markdown,
				Value = info.Markdown
			}),
			Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
				new Position(info.Range.Start.Line, info.Range.Start.Character),
				new Position(info.Range.End.Line, info.Range.End.Character))
		});
	}

	protected override HoverRegistrationOptions CreateRegistrationOptions(
		HoverCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new HoverRegistrationOptions
		{
			DocumentSelector = MushDocument.Selector
		};
	}
}
