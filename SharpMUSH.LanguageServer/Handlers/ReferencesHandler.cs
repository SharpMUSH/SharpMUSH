using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles find all references requests for MUSH code.
/// Locates all usages of attributes, functions, and objects.
/// </summary>
public class ReferencesHandler : ReferencesHandlerBase
{
	private readonly DocumentManager _documentManager;

	public ReferencesHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
	}

	public override Task<LocationContainer?> Handle(ReferenceParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<LocationContainer?>(null);
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
				return Task.FromResult<LocationContainer?>(null);
			}

			var word = line.Substring(wordStart, wordEnd - wordStart);
			var locations = new List<Location>();

			// Search through all lines for references to this word
			for (int i = 0; i < lines.Length; i++)
			{
				var currentLine = lines[i];
				var startIndex = 0;

				while (startIndex < currentLine.Length)
				{
					var index = currentLine.IndexOf(word, startIndex, StringComparison.OrdinalIgnoreCase);
					if (index == -1) break;

					// Check if this is a whole word match
					var isWholeWord = true;
					if (index > 0 && IsWordCharacter(currentLine[index - 1]))
					{
						isWholeWord = false;
					}
					if (index + word.Length < currentLine.Length && IsWordCharacter(currentLine[index + word.Length]))
					{
						isWholeWord = false;
					}

					if (isWholeWord)
					{
						// Include the reference if requested, or exclude the definition
						var isDefinition = IsDefinitionLine(currentLine, word, index);

						if (request.Context.IncludeDeclaration || !isDefinition)
						{
							locations.Add(new Location
							{
								Uri = request.TextDocument.Uri,
								Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
									new Position(i, index),
									new Position(i, index + word.Length))
							});
						}
					}

					startIndex = index + 1;
				}
			}

			if (locations.Count > 0)
			{
				return Task.FromResult<LocationContainer?>(new LocationContainer(locations));
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error finding references: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult<LocationContainer?>(null);
	}

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == '-';
	}

	private static bool IsDefinitionLine(string line, string word, int index)
	{
		// Check if this line contains an attribute definition
		// &attribute syntax
		if (line.Contains($"&{word}"))
			return true;

		// @set object/attribute syntax
		if (line.Contains("@set") && index > 0 && line[index - 1] == '/')
			return true;

		return false;
	}

	protected override ReferenceRegistrationOptions CreateRegistrationOptions(
		ReferenceCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new ReferenceRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}
}
