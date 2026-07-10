using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles code completion requests for MUSH code.
/// Delegates to the shared <see cref="IMushCodeAnalyzer"/> and adapts its suggestions to LSP
/// completion items.
/// </summary>
public class CompletionHandler : CompletionHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMushCodeAnalyzer _analyzer;

	public CompletionHandler(DocumentManager documentManager, IMushCodeAnalyzer analyzer)
	{
		_documentManager = documentManager;
		_analyzer = analyzer;
	}

	public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult(new CompletionList());
		}

		var suggestions = _analyzer.Complete(
			document.Text, (int)request.Position.Line, (int)request.Position.Character);

		var completions = suggestions.Select(s => new CompletionItem
		{
			Label = s.Label,
			Kind = MapKind(s.Kind),
			Detail = s.Detail,
			Documentation = s.Documentation,
			InsertText = s.InsertText,
			InsertTextFormat = s.IsSnippet ? InsertTextFormat.Snippet : InsertTextFormat.PlainText
		}).ToList();

		return Task.FromResult(new CompletionList(completions, isIncomplete: false));
	}

	public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
	{
		return Task.FromResult(request);
	}

	private static CompletionItemKind MapKind(string kind) => kind switch
	{
		"Function" => CompletionItemKind.Function,
		"Keyword" => CompletionItemKind.Keyword,
		_ => CompletionItemKind.Variable
	};

	protected override CompletionRegistrationOptions CreateRegistrationOptions(
		CompletionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new CompletionRegistrationOptions
		{
			DocumentSelector = MushDocument.Selector,
			TriggerCharacters = new[] { "%", "@", "#" },
			ResolveProvider = false
		};
	}
}
