using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles go-to-definition requests for MUSH code.
/// Currently provides navigation to attribute definitions within the same document.
/// </summary>
public class DefinitionHandler : DefinitionHandlerBase
{
	private readonly DocumentManager _documentManager;

	public DefinitionHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
	}

	public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<LocationOrLocationLinks?>(null);
		}

		try
		{
			var lines = document.Text.Split('\n');
			var line = request.Position.Line < lines.Length ? lines[request.Position.Line] : string.Empty;
			var character = (int)request.Position.Character;

			// Find the word at the cursor position
			var wordStart = character;
			var wordEnd = character;

			while (wordStart > 0 && IsWordCharacter(line[wordStart - 1]))
			{
				wordStart--;
			}

			while (wordEnd < line.Length && IsWordCharacter(line[wordEnd]))
			{
				wordEnd++;
			}

			if (wordStart >= wordEnd)
			{
				return Task.FromResult<LocationOrLocationLinks?>(null);
			}

			var word = line.Substring(wordStart, wordEnd - wordStart);

			// Look for attribute definitions in the document
			// Pattern: @set <object>/<attribute> or &<attribute>
			var locations = new List<Location>();

			for (int i = 0; i < lines.Length; i++)
			{
				var currentLine = lines[i];
				
				// Check for &<attribute> pattern
				if (currentLine.Contains($"&{word}"))
				{
					var index = currentLine.IndexOf($"&{word}", StringComparison.OrdinalIgnoreCase);
					if (index >= 0)
					{
						locations.Add(new Location
						{
							Uri = request.TextDocument.Uri,
							Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
								new Position(i, index),
								new Position(i, index + word.Length + 1))
						});
					}
				}

				// Check for @set pattern
				if (currentLine.Contains("@set") && currentLine.Contains($"/{word}"))
				{
					var index = currentLine.IndexOf($"/{word}", StringComparison.OrdinalIgnoreCase);
					if (index >= 0)
					{
						locations.Add(new Location
						{
							Uri = request.TextDocument.Uri,
							Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
								new Position(i, index + 1),
								new Position(i, index + word.Length + 1))
						});
					}
				}
			}

			if (locations.Count > 0)
			{
				return Task.FromResult<LocationOrLocationLinks?>(
					new LocationOrLocationLinks(locations.Select(l => new LocationOrLocationLink(l))));
			}
		}
		catch (Exception ex)
		{
			#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error finding definition: {ex.Message}");
			#pragma warning restore VSTHRD103
		}

		return Task.FromResult<LocationOrLocationLinks?>(null);
	}

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == '-';
	}

	protected override DefinitionRegistrationOptions CreateRegistrationOptions(
		DefinitionCapability capability, 
		ClientCapabilities clientCapabilities)
	{
		return new DefinitionRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}
}
