using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles semantic tokens requests for MUSH code highlighting.
/// </summary>
public class SemanticTokensHandler : SemanticTokensHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly LSPMUSHCodeParser _parser;

	public SemanticTokensHandler(DocumentManager documentManager, LSPMUSHCodeParser parser)
	{
		_documentManager = documentManager;
		_parser = parser;
	}

	protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
		ITextDocumentIdentifierParams @params,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
	}

	protected override Task Tokenize(
		SemanticTokensBuilder builder,
		ITextDocumentIdentifierParams identifier,
		CancellationToken cancellationToken)
	{
		var uri = identifier.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
			return Task.CompletedTask;

		try
		{
			var tokensData = _parser.GetSemanticTokens(document.Text, MushParseMode.ForFileName(uri));

			for (int i = 0; i < tokensData.Data.Length; i += 5)
			{
				var deltaLine = tokensData.Data[i];
				var deltaChar = tokensData.Data[i + 1];
				var length = tokensData.Data[i + 2];
				var tokenType = tokensData.Data[i + 3];
				var modifiers = tokensData.Data[i + 4];

				builder.Push(deltaLine, deltaChar, length, tokenType, modifiers);
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating semantic tokens: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.CompletedTask;
	}

	protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
		SemanticTokensCapability capability,
		ClientCapabilities clientCapabilities)
	{
		var sampleData = _parser.GetSemanticTokens("add(1,2)", ParseType.Function);

		return new SemanticTokensRegistrationOptions
		{
			DocumentSelector = MushDocument.Selector,
			Legend = new SemanticTokensLegend
			{
				TokenTypes = new Container<SemanticTokenType>(sampleData.TokenTypes.Select(t =>
					new SemanticTokenType(t.ToLowerInvariant()))),
				TokenModifiers = new Container<SemanticTokenModifier>(sampleData.TokenModifiers.Select(m =>
					new SemanticTokenModifier(m.ToLowerInvariant())))
			},
			Full = new SemanticTokensCapabilityRequestFull
			{
				Delta = false
			},
			Range = true
		};
	}
}
