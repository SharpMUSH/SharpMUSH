using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles signature help requests for MUSH code.
/// Delegates to the shared <see cref="IMushCodeAnalyzer"/> and adapts its result to the LSP
/// signature-help shape.
/// </summary>
public class SignatureHelpHandler : SignatureHelpHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMushCodeAnalyzer _analyzer;

	public SignatureHelpHandler(DocumentManager documentManager, IMushCodeAnalyzer analyzer)
	{
		_documentManager = documentManager;
		_analyzer = analyzer;
	}

	public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<SignatureHelp?>(null);
		}

		var info = _analyzer.SignatureHelp(
			document.Text, (int)request.Position.Line, (int)request.Position.Character);
		if (info is null)
		{
			return Task.FromResult<SignatureHelp?>(null);
		}

		var parameters = info.Parameters.Select(p => new ParameterInformation
		{
			Label = p.Label,
			Documentation = p.Documentation
		});

		var signature = new SignatureInformation
		{
			Label = info.Label,
			Documentation = new StringOrMarkupContent(new MarkupContent
			{
				Kind = MarkupKind.Markdown,
				Value = info.Documentation
			}),
			Parameters = new Container<ParameterInformation>(parameters),
			ActiveParameter = info.ActiveParameter
		};

		return Task.FromResult<SignatureHelp?>(new SignatureHelp
		{
			Signatures = new Container<SignatureInformation>(signature),
			ActiveSignature = 0,
			ActiveParameter = info.ActiveParameter
		});
	}

	protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
		SignatureHelpCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new SignatureHelpRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			TriggerCharacters = new Container<string>("(", ","),
			RetriggerCharacters = new Container<string>(",")
		};
	}
}
