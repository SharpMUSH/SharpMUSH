using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles code completion requests for MUSH code.
/// Provides completions for functions, commands, and common patterns.
/// </summary>
public class CompletionHandler : CompletionHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMUSHCodeParser _parser;

	public CompletionHandler(DocumentManager documentManager, IMUSHCodeParser parser)
	{
		_documentManager = documentManager;
		_parser = parser;
	}

	public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult(new CompletionList());
		}

		var completions = new List<CompletionItem>();

		try
		{
			// Get the current line and position
			var lines = document.Text.Split('\n');
			var line = request.Position.Line < lines.Length ? lines[request.Position.Line] : string.Empty;
			var character = (int)request.Position.Character;

			// Get the word being typed
			var wordStart = character;
			while (wordStart > 0 && IsWordCharacter(line[wordStart - 1]))
			{
				wordStart--;
			}
			var prefix = wordStart < line.Length ? line.Substring(wordStart, character - wordStart) : string.Empty;

			// Add function completions
			foreach (var (name, definition) in _parser.FunctionLibrary)
			{
				if (string.IsNullOrEmpty(prefix) || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					completions.Add(new CompletionItem
					{
						Label = name,
						Kind = CompletionItemKind.Function,
						Detail = $"{name}({GetParameterList(definition.LibraryInformation.Attribute.MinArgs, definition.LibraryInformation.Attribute.MaxArgs)})",
						Documentation = $"Min args: {definition.LibraryInformation.Attribute.MinArgs}, Max args: {definition.LibraryInformation.Attribute.MaxArgs}",
						InsertText = $"{name}($0)",
						InsertTextFormat = InsertTextFormat.Snippet
					});
				}
			}

			// Add command completions if at start of line or after whitespace
			if (character == 0 || (character > 0 && char.IsWhiteSpace(line[character - 1])))
			{
				foreach (var (name, definition) in _parser.CommandLibrary)
				{
					if (string.IsNullOrEmpty(prefix) || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						completions.Add(new CompletionItem
						{
							Label = name,
							Kind = CompletionItemKind.Keyword,
							Detail = $"Command: {name}",
							Documentation = $"Switches: {string.Join(", ", definition.LibraryInformation.Attribute.Switches ?? Array.Empty<string>())}",
							InsertText = name
						});
					}
				}
			}

			// Add common MUSH patterns
			AddCommonPatterns(completions, prefix);
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating completions: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult(new CompletionList(completions, isIncomplete: false));
	}

	public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
	{
		// No additional resolution needed
		return Task.FromResult(request);
	}

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_' || c == '@';
	}

	private static string GetParameterList(int minArgs, int maxArgs)
	{
		if (minArgs == 0 && maxArgs == 0)
			return "";
		if (minArgs == maxArgs)
			return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}"));
		return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}")) +
					 (maxArgs > minArgs ? ", ..." : "");
	}

	private static void AddCommonPatterns(List<CompletionItem> completions, string prefix)
	{
		var patterns = new[]
		{
			new { Label = "%#", Detail = "Current object (#dbref)", Kind = CompletionItemKind.Variable },
			new { Label = "%!", Detail = "Executing object (#dbref)", Kind = CompletionItemKind.Variable },
			new { Label = "%@", Detail = "Calling object (#dbref)", Kind = CompletionItemKind.Variable },
			new { Label = "%N", Detail = "Player name", Kind = CompletionItemKind.Variable },
			new { Label = "%0", Detail = "Argument 0", Kind = CompletionItemKind.Variable },
			new { Label = "%1", Detail = "Argument 1", Kind = CompletionItemKind.Variable },
			new { Label = "%qa", Detail = "Q-register a", Kind = CompletionItemKind.Variable },
			new { Label = "%va", Detail = "V-register a", Kind = CompletionItemKind.Variable }
		};

		foreach (var pattern in patterns)
		{
			if (string.IsNullOrEmpty(prefix) || pattern.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				completions.Add(new CompletionItem
				{
					Label = pattern.Label,
					Kind = pattern.Kind,
					Detail = pattern.Detail,
					InsertText = pattern.Label
				});
			}
		}
	}

	protected override CompletionRegistrationOptions CreateRegistrationOptions(
		CompletionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new CompletionRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			TriggerCharacters = new[] { "%", "@", "#" },
			ResolveProvider = false
		};
	}
}
