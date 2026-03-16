using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles rename symbol requests for MUSH code.
/// Safely renames attributes across the document.
/// </summary>
public class RenameHandler : RenameHandlerBase
{
	private readonly DocumentManager _documentManager;

	public RenameHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
	}

	public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<WorkspaceEdit?>(null);
		}

		try
		{
			var lines = document.Text.Split('\n');
			var line = request.Position.Line < lines.Length ? lines[request.Position.Line] : string.Empty;
			var character = (int)request.Position.Character;

			// Find the symbol at the cursor position
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
				return Task.FromResult<WorkspaceEdit?>(null);
			}

			var oldName = line.Substring(wordStart, wordEnd - wordStart);
			var newName = request.NewName;

			// Find all occurrences of this symbol
			var edits = new List<TextEdit>();

			for (int i = 0; i < lines.Length; i++)
			{
				var currentLine = lines[i];
				var startIndex = 0;

				while (startIndex < currentLine.Length)
				{
					var index = currentLine.IndexOf(oldName, startIndex, StringComparison.OrdinalIgnoreCase);
					if (index == -1) break;

					// Check if this is a whole word match
					var isWholeWord = true;
					if (index > 0 && IsWordCharacter(currentLine[index - 1]))
					{
						isWholeWord = false;
					}
					if (index + oldName.Length < currentLine.Length && IsWordCharacter(currentLine[index + oldName.Length]))
					{
						isWholeWord = false;
					}

					if (isWholeWord)
					{
						edits.Add(new TextEdit
						{
							Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
								new Position(i, index),
								new Position(i, index + oldName.Length)),
							NewText = newName
						});
					}

					startIndex = index + 1;
				}
			}

			if (edits.Count > 0)
			{
				return Task.FromResult<WorkspaceEdit?>(new WorkspaceEdit
				{
					Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
					{
						[request.TextDocument.Uri] = edits
					}
				});
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error renaming symbol: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult<WorkspaceEdit?>(null);
	}

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == '-';
	}

	protected override RenameRegistrationOptions CreateRegistrationOptions(
		RenameCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new RenameRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			PrepareProvider = false
		};
	}
}
