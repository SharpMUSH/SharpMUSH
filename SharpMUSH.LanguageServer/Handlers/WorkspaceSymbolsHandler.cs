using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles workspace symbol requests for MUSH code.
/// Searches for symbols across all open documents.
/// </summary>
public class WorkspaceSymbolsHandler : WorkspaceSymbolsHandlerBase
{
	private readonly DocumentManager _documentManager;

	public WorkspaceSymbolsHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
	}

	public override Task<Container<WorkspaceSymbol>?> Handle(
		WorkspaceSymbolParams request,
		CancellationToken cancellationToken)
	{
		var symbols = new List<WorkspaceSymbol>();

		try
		{
			var query = request.Query?.ToLower() ?? string.Empty;

			// Search through all documents
			foreach (var (uri, document) in _documentManager.GetAllDocuments())
			{
				var lines = document.Text.Split('\n');

				for (int i = 0; i < lines.Length; i++)
				{
					var line = lines[i];

					// Look for attribute definitions: &ATTRIBUTE_NAME
					var attributeMatch = System.Text.RegularExpressions.Regex.Match(
						line, @"&([a-zA-Z_][a-zA-Z0-9_\-]*)");
					if (attributeMatch.Success)
					{
						var attributeName = attributeMatch.Groups[1].Value;
						if (string.IsNullOrEmpty(query) || attributeName.ToLower().Contains(query))
						{
							symbols.Add(new WorkspaceSymbol
							{
								Name = attributeName,
								Kind = SymbolKind.Property,
								Location = new Location
								{
									Uri = DocumentUri.From(uri),
									Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
										new Position(i, attributeMatch.Index),
										new Position(i, attributeMatch.Index + attributeMatch.Length))
								},
								ContainerName = "Attributes"
							});
						}
					}

					// Look for function calls
					var functionMatch = System.Text.RegularExpressions.Regex.Match(
						line, @"([a-zA-Z_][a-zA-Z0-9_]*)\s*\(");
					if (functionMatch.Success)
					{
						var functionName = functionMatch.Groups[1].Value;
						if (string.IsNullOrEmpty(query) || functionName.ToLower().Contains(query))
						{
							symbols.Add(new WorkspaceSymbol
							{
								Name = functionName,
								Kind = SymbolKind.Function,
								Location = new Location
								{
									Uri = DocumentUri.From(uri),
									Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
										new Position(i, functionMatch.Groups[1].Index),
										new Position(i, functionMatch.Groups[1].Index + functionName.Length))
								},
								ContainerName = "Functions"
							});
						}
					}

					// Look for commands
					var commandMatch = System.Text.RegularExpressions.Regex.Match(
						line, @"^\s*(@[a-zA-Z][a-zA-Z0-9_\-]*)");
					if (commandMatch.Success)
					{
						var commandName = commandMatch.Groups[1].Value;
						if (string.IsNullOrEmpty(query) || commandName.ToLower().Contains(query))
						{
							symbols.Add(new WorkspaceSymbol
							{
								Name = commandName,
								Kind = SymbolKind.Method,
								Location = new Location
								{
									Uri = DocumentUri.From(uri),
									Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
										new Position(i, commandMatch.Index),
										new Position(i, commandMatch.Index + commandMatch.Length))
								},
								ContainerName = "Commands"
							});
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error searching workspace symbols: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		if (symbols.Count > 0)
		{
			return Task.FromResult<Container<WorkspaceSymbol>?>(new Container<WorkspaceSymbol>(symbols));
		}

		return Task.FromResult<Container<WorkspaceSymbol>?>(null);
	}

	protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
		WorkspaceSymbolCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new WorkspaceSymbolRegistrationOptions();
	}
}
