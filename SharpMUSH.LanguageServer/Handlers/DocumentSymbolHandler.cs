using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles document symbol requests for MUSH code.
/// Delegates to the shared <see cref="IMushCodeAnalyzer"/> and adapts its symbols to the LSP
/// outline shape.
/// </summary>
public class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMushCodeAnalyzer _analyzer;

	public DocumentSymbolHandler(DocumentManager documentManager, IMushCodeAnalyzer analyzer)
	{
		_documentManager = documentManager;
		_analyzer = analyzer;
	}

	public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
		DocumentSymbolParams request,
		CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
		}

		var symbols = _analyzer.DocumentSymbols(document.Text)
			.Select(s => new SymbolInformationOrDocumentSymbol(new DocumentSymbol
			{
				Name = s.Name,
				Kind = MapKind(s.Kind),
				Detail = s.Detail,
				Range = ToRange(s.Range),
				SelectionRange = ToRange(s.SelectionRange)
			}))
			.ToList();

		if (symbols.Count > 0)
		{
			return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
				new SymbolInformationOrDocumentSymbolContainer(symbols));
		}

		return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
	}

	private static SymbolKind MapKind(string kind) => kind switch
	{
		"Property" => SymbolKind.Property,
		"Function" => SymbolKind.Function,
		_ => SymbolKind.Method
	};

	private static OmniSharp.Extensions.LanguageServer.Protocol.Models.Range ToRange(
		SharpMUSH.Library.Models.Range range)
		=> new(
			new Position(range.Start.Line, range.Start.Character),
			new Position(range.End.Line, range.End.Character));

	protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
		DocumentSymbolCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new DocumentSymbolRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}
}
