using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles hover requests to show information about MUSH code elements.
/// </summary>
public class HoverHandler : HoverHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMUSHCodeParser _parser;

	public HoverHandler(DocumentManager documentManager, IMUSHCodeParser parser)
	{
		_documentManager = documentManager;
		_parser = parser;
	}

	public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<Hover?>(null);
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
				return Task.FromResult<Hover?>(null);
			}

			var word = line.Substring(wordStart, wordEnd - wordStart);

			// Check if it's a function
			if (_parser.FunctionLibrary.TryGetValue(word, out var functionDef))
			{
				var markdown = BuildFunctionHover(word, functionDef.LibraryInformation.Attribute);
				return Task.FromResult<Hover?>(new Hover
				{
					Contents = new MarkedStringsOrMarkupContent(new MarkupContent
					{
						Kind = MarkupKind.Markdown,
						Value = markdown
					}),
					Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
						new Position(request.Position.Line, wordStart),
						new Position(request.Position.Line, wordEnd))
				});
			}

			// Check if it's a command
			if (_parser.CommandLibrary.TryGetValue(word, out var commandDef))
			{
				var markdown = BuildCommandHover(word, commandDef.LibraryInformation.Attribute);
				return Task.FromResult<Hover?>(new Hover
				{
					Contents = new MarkedStringsOrMarkupContent(new MarkupContent
					{
						Kind = MarkupKind.Markdown,
						Value = markdown
					}),
					Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
						new Position(request.Position.Line, wordStart),
						new Position(request.Position.Line, wordEnd))
				});
			}

			// Check for special patterns
			var patternInfo = GetPatternInfo(word);
			if (patternInfo != null)
			{
				return Task.FromResult<Hover?>(new Hover
				{
					Contents = new MarkedStringsOrMarkupContent(new MarkupContent
					{
						Kind = MarkupKind.Markdown,
						Value = patternInfo
					}),
					Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
						new Position(request.Position.Line, wordStart),
						new Position(request.Position.Line, wordEnd))
				});
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating hover info: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult<Hover?>(null);
	}

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '%' || c == '#';
	}

	private static string BuildFunctionHover(string name, Library.Attributes.SharpFunctionAttribute attr)
	{
		var markdown = $"### Function: `{name}`\n\n";
		markdown += $"**Signature:** `{name}({GetParameterList(attr.MinArgs, attr.MaxArgs)})`\n\n";
		markdown += $"**Arguments:**\n";
		markdown += $"- Minimum: {attr.MinArgs}\n";
		markdown += $"- Maximum: {attr.MaxArgs}\n\n";

		if (attr.Flags != 0)
		{
			markdown += $"**Flags:** {attr.Flags}\n\n";
		}

		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			markdown += $"**Restrictions:** {string.Join(", ", attr.Restrict)}\n\n";
		}

		return markdown;
	}

	private static string BuildCommandHover(string name, Library.Attributes.SharpCommandAttribute attr)
	{
		var markdown = $"### Command: `{name}`\n\n";

		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			markdown += $"**Switches:** {string.Join(", ", attr.Switches)}\n\n";
		}

		markdown += $"**Arguments:**\n";
		markdown += $"- Minimum: {attr.MinArgs}\n";
		markdown += $"- Maximum: {attr.MaxArgs}\n\n";

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			markdown += $"**Lock:** {attr.CommandLock}\n\n";
		}

		markdown += $"**Behavior:** {attr.Behavior}\n\n";

		return markdown;
	}

	private static string GetParameterList(int minArgs, int maxArgs)
	{
		if (minArgs == 0 && maxArgs == 0)
			return "";
		if (minArgs == maxArgs)
			return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}"));
		return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}")) +
					 (maxArgs > minArgs ? ", [optional...]" : "");
	}

	private static string? GetPatternInfo(string word)
	{
		return word switch
		{
			"%#" => "**Current object** - The #dbref of the object this code is set on",
			"%!" => "**Executing object** - The #dbref of the object executing the code",
			"%@" => "**Calling object** - The #dbref of the object that called this code",
			"%N" or "%n" => "**Player name** - The name of the player executing the code",
			"%l" or "%L" => "**Location** - The location of the executing object",
			"%" when word.Length == 2 && char.IsDigit(word[1]) =>
				$"**Argument {word[1]}** - The {word[1]}th argument passed to this function/command",
			"%" when word.Length == 3 && word[1] == 'q' && char.IsLetter(word[2]) =>
				$"**Q-register {word[2]}** - Q-register storage",
			"%" when word.Length == 3 && word[1] == 'v' && char.IsLetter(word[2]) =>
				$"**V-register {word[2]}** - V-register storage",
			"#" when word.Length > 1 && word.Skip(1).All(char.IsDigit) =>
				$"**Object reference** - References object #{word.Substring(1)}",
			_ => null
		};
	}

	protected override HoverRegistrationOptions CreateRegistrationOptions(
		HoverCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new HoverRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}
}
