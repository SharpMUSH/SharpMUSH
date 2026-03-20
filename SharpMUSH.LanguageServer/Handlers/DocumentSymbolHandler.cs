using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles document symbol requests for MUSH code.
/// Provides an outline view of attributes and functions in the document.
/// </summary>
public partial class DocumentSymbolHandler : DocumentSymbolHandlerBase
{
	private readonly DocumentManager _documentManager;

	public DocumentSymbolHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
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

		var symbols = new List<SymbolInformationOrDocumentSymbol>();

		try
		{
			var lines = document.Text.Split('\n');

			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];

				// Look for attribute definitions: &ATTRIBUTE_NAME
				var attributeMatch = AttributeDefinitionRegex().Match(line);
				if (attributeMatch.Success)
				{
					var attributeName = attributeMatch.Groups[1].Value;
					symbols.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol
					{
						Name = attributeName,
						Kind = SymbolKind.Property,
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, line.Length)),
						SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, attributeMatch.Index),
							new Position(i, attributeMatch.Index + attributeMatch.Length)),
						Detail = "Attribute definition"
					}));
				}

				// Look for @set commands with attributes: @set object/ATTRIBUTE
				var setMatch = SetAttributeRegex().Match(line);
				if (setMatch.Success)
				{
					var attributeName = setMatch.Groups[1].Value;
					symbols.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol
					{
						Name = attributeName,
						Kind = SymbolKind.Property,
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, line.Length)),
						SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, setMatch.Groups[1].Index),
							new Position(i, setMatch.Groups[1].Index + setMatch.Groups[1].Length)),
						Detail = "@set attribute"
					}));
				}

				// Look for function calls at the start of lines (potential function definitions in softcode)
				var functionMatch = FunctionCallRegex().Match(line);
				if (functionMatch.Success)
				{
					var functionName = functionMatch.Groups[1].Value;
					symbols.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol
					{
						Name = functionName,
						Kind = SymbolKind.Function,
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, line.Length)),
						SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, functionMatch.Groups[1].Index),
							new Position(i, functionMatch.Groups[1].Index + functionMatch.Groups[1].Length)),
						Detail = "Function call"
					}));
				}

				// Look for commands at the start of lines
				var commandMatch = CommandRegex().Match(line);
				if (commandMatch.Success)
				{
					var commandName = commandMatch.Groups[1].Value;
					symbols.Add(new SymbolInformationOrDocumentSymbol(new DocumentSymbol
					{
						Name = commandName,
						Kind = SymbolKind.Method,
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, line.Length)),
						SelectionRange = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, commandMatch.Index),
							new Position(i, commandMatch.Index + commandMatch.Length)),
						Detail = "MUSH command"
					}));
				}
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error extracting document symbols: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		if (symbols.Count > 0)
		{
			return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
				new SymbolInformationOrDocumentSymbolContainer(symbols));
		}

		return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(null);
	}

	protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
		DocumentSymbolCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new DocumentSymbolRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}

	[GeneratedRegex(@"&([a-zA-Z_][a-zA-Z0-9_\-]*)")]
	private static partial Regex AttributeDefinitionRegex();

	[GeneratedRegex(@"@set\s+[^/]+/([a-zA-Z_][a-zA-Z0-9_\-]*)")]
	private static partial Regex SetAttributeRegex();

	[GeneratedRegex(@"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*\(")]
	private static partial Regex FunctionCallRegex();

	[GeneratedRegex(@"^\s*(@[a-zA-Z][a-zA-Z0-9_\-]*)")]
	private static partial Regex CommandRegex();
}
