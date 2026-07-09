using System.Text.RegularExpressions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;
using Range = SharpMUSH.Library.Models.Range;

namespace SharpMUSH.CodeAnalysis;

/// <summary>
/// Position-based code intelligence (hover, completion, signature help, symbols) extracted
/// from the Language Server handlers so the LSP and the in-server MCP tools share one
/// implementation. All methods are read-only and never throw.
/// </summary>
public partial class MushCodeAnalyzer
{
	public HoverInfo? Hover(string code, int line, int character)
	{
		try
		{
			var lines = code.Split('\n');
			var text = line >= 0 && line < lines.Length ? lines[line] : string.Empty;
			character = Math.Clamp(character, 0, text.Length);

			var wordStart = character;
			var wordEnd = character;
			while (wordStart > 0 && IsHoverWordChar(text[wordStart - 1])) wordStart--;
			while (wordEnd < text.Length && IsHoverWordChar(text[wordEnd])) wordEnd++;
			if (wordStart >= wordEnd) return null;

			var word = text.Substring(wordStart, wordEnd - wordStart);

			string? markdown;
			if (parser.FunctionLibrary.TryGetValue(word, out var fd))
			{
				markdown = BuildFunctionHover(word, fd.LibraryInformation.Attribute);
			}
			else if (parser.CommandLibrary.TryGetValue(word, out var cd))
			{
				markdown = BuildCommandHover(word, cd.LibraryInformation.Attribute);
			}
			else
			{
				markdown = GetPatternInfo(word);
			}

			if (markdown is null) return null;

			return new HoverInfo(
				markdown,
				new Range { Start = new Position(line, wordStart), End = new Position(line, wordEnd) });
		}
		catch
		{
			return null;
		}
	}

	public IReadOnlyList<CompletionSuggestion> Complete(string code, int line, int character)
	{
		var completions = new List<CompletionSuggestion>();
		try
		{
			var lines = code.Split('\n');
			var text = line >= 0 && line < lines.Length ? lines[line] : string.Empty;
			character = Math.Clamp(character, 0, text.Length);

			var wordStart = character;
			while (wordStart > 0 && IsCompletionWordChar(text[wordStart - 1])) wordStart--;
			var prefix = wordStart < text.Length ? text.Substring(wordStart, character - wordStart) : string.Empty;

			foreach (var (name, definition) in parser.FunctionLibrary)
			{
				if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					var attr = definition.LibraryInformation.Attribute;
					completions.Add(new CompletionSuggestion(
						name,
						"Function",
						$"{name}({GetParameterList(attr.MinArgs, attr.MaxArgs, ", ...")})",
						$"Min args: {attr.MinArgs}, Max args: {attr.MaxArgs}",
						$"{name}($0)",
						IsSnippet: true));
				}
			}

			// Offer commands at the start of a line/after whitespace, or while typing an @command.
			if (character == 0 || (character > 0 && char.IsWhiteSpace(text[character - 1])) ||
			    prefix.StartsWith('@'))
			{
				foreach (var (name, definition) in parser.CommandLibrary)
				{
					if (prefix.Length == 0 || name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
					{
						var switches = definition.LibraryInformation.Attribute.Switches ?? [];
						completions.Add(new CompletionSuggestion(
							name,
							"Keyword",
							$"Command: {name}",
							$"Switches: {string.Join(", ", switches)}",
							name,
							IsSnippet: false));
					}
				}
			}

			AddCommonPatterns(completions, prefix);
		}
		catch
		{
			// fall through with whatever was collected
		}

		return completions;
	}

	public SignatureInfo? SignatureHelp(string code, int line, int character)
	{
		try
		{
			var lines = code.Split('\n');
			var text = line >= 0 && line < lines.Length ? lines[line] : string.Empty;
			character = Math.Clamp(character, 0, text.Length);

			var functionInfo = FindFunctionAtPosition(text, character);
			if (functionInfo is null) return null;

			var (functionName, currentParam) = functionInfo.Value;
			if (parser.FunctionLibrary.TryGetValue(functionName, out var fd))
			{
				return BuildSignatureInfo(functionName, fd.LibraryInformation.Attribute, currentParam);
			}

			return null;
		}
		catch
		{
			return null;
		}
	}

	public IReadOnlyList<CodeSymbol> DocumentSymbols(string code)
	{
		var symbols = new List<CodeSymbol>();
		try
		{
			var lines = code.Split('\n');
			for (var i = 0; i < lines.Length; i++)
			{
				// Drop a trailing CR so symbol ranges aren't off-by-one on CRLF documents.
				var line = lines[i].TrimEnd('\r');

				var attributeMatch = AttributeDefinitionRegex().Match(line);
				if (attributeMatch.Success)
				{
					symbols.Add(new CodeSymbol(
						attributeMatch.Groups[1].Value, "Property", "Attribute definition",
						FullLineRange(i, line),
						SelectionRange(i, attributeMatch.Index, attributeMatch.Length)));
				}

				var setMatch = SetAttributeRegex().Match(line);
				if (setMatch.Success)
				{
					symbols.Add(new CodeSymbol(
						setMatch.Groups[1].Value, "Property", "@set attribute",
						FullLineRange(i, line),
						SelectionRange(i, setMatch.Groups[1].Index, setMatch.Groups[1].Length)));
				}

				var functionMatch = FunctionCallRegex().Match(line);
				if (functionMatch.Success)
				{
					symbols.Add(new CodeSymbol(
						functionMatch.Groups[1].Value, "Function", "Function call",
						FullLineRange(i, line),
						SelectionRange(i, functionMatch.Groups[1].Index, functionMatch.Groups[1].Length)));
				}

				var commandMatch = CommandRegex().Match(line);
				if (commandMatch.Success)
				{
					symbols.Add(new CodeSymbol(
						commandMatch.Groups[1].Value, "Method", "MUSH command",
						FullLineRange(i, line),
						SelectionRange(i, commandMatch.Index, commandMatch.Length)));
				}
			}
		}
		catch
		{
			// fall through with whatever was collected
		}

		return symbols;
	}

	// ── shared helpers ─────────────────────────────────────────────────────────

	private static Range FullLineRange(int line, string text)
		=> new() { Start = new Position(line, 0), End = new Position(line, text.Length) };

	private static Range SelectionRange(int line, int start, int length)
		=> new() { Start = new Position(line, start), End = new Position(line, start + length) };

	private static bool IsHoverWordChar(char c)
		=> char.IsLetterOrDigit(c) || c is '_' or '@' or '%' or '#';

	private static bool IsCompletionWordChar(char c)
		=> char.IsLetterOrDigit(c) || c is '_' or '@' or '%' or '#';

	private static bool IsSignatureWordChar(char c)
		=> char.IsLetterOrDigit(c) || c == '_';

	private static string GetParameterList(int minArgs, int maxArgs, string optionalSuffix)
	{
		if (minArgs == 0 && maxArgs == 0) return "";
		if (minArgs == maxArgs)
			return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}"));
		return string.Join(", ", Enumerable.Range(1, minArgs).Select(i => $"arg{i}")) +
		       (maxArgs > minArgs ? optionalSuffix : "");
	}

	private static string BuildFunctionHover(string name, SharpFunctionAttribute attr)
	{
		var markdown = $"### Function: `{name}`\n\n";
		markdown += $"**Signature:** `{name}({GetParameterList(attr.MinArgs, attr.MaxArgs, ", [optional...]")})`\n\n";
		markdown += "**Arguments:**\n";
		markdown += $"- Minimum: {attr.MinArgs}\n";
		markdown += $"- Maximum: {attr.MaxArgs}\n\n";

		if (attr.Flags != 0)
		{
			markdown += $"**Flags:** {attr.Flags}\n\n";
		}

		if (attr.Restrict is { Length: > 0 })
		{
			markdown += $"**Restrictions:** {string.Join(", ", attr.Restrict)}\n\n";
		}

		return markdown;
	}

	private static string BuildCommandHover(string name, SharpCommandAttribute attr)
	{
		var markdown = $"### Command: `{name}`\n\n";

		if (attr.Switches is { Length: > 0 })
		{
			markdown += $"**Switches:** {string.Join(", ", attr.Switches)}\n\n";
		}

		markdown += "**Arguments:**\n";
		markdown += $"- Minimum: {attr.MinArgs}\n";
		markdown += $"- Maximum: {attr.MaxArgs}\n\n";

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			markdown += $"**Lock:** {attr.CommandLock}\n\n";
		}

		markdown += $"**Behavior:** {attr.Behavior}\n\n";

		return markdown;
	}

	private static string? GetPatternInfo(string word)
		=> word switch
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
				$"**Object reference** - References object #{word[1..]}",
			_ => null
		};

	private static void AddCommonPatterns(List<CompletionSuggestion> completions, string prefix)
	{
		var patterns = new[]
		{
			("%#", "Current object (#dbref)"),
			("%!", "Executing object (#dbref)"),
			("%@", "Calling object (#dbref)"),
			("%N", "Player name"),
			("%0", "Argument 0"),
			("%1", "Argument 1"),
			("%qa", "Q-register a"),
			("%va", "V-register a")
		};

		foreach (var (label, detail) in patterns)
		{
			if (prefix.Length == 0 || label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				completions.Add(new CompletionSuggestion(label, "Variable", detail, null, label, IsSnippet: false));
			}
		}
	}

	private static (string functionName, int currentParam)? FindFunctionAtPosition(string line, int position)
	{
		var depth = 0;
		var paramCount = 0;
		var i = position - 1;

		while (i >= 0)
		{
			if (line[i] == ')')
			{
				depth++;
			}
			else if (line[i] == '(')
			{
				depth--;
				if (depth < 0)
				{
					var nameEnd = i;
					i--;
					while (i >= 0 && IsSignatureWordChar(line[i])) i--;
					var functionName = line.Substring(i + 1, nameEnd - i - 1);
					return (functionName, paramCount);
				}
			}
			else if (line[i] == ',' && depth == 0)
			{
				paramCount++;
			}

			i--;
		}

		return null;
	}

	private static SignatureInfo BuildSignatureInfo(string functionName, SharpFunctionAttribute attr, int activeParam)
	{
		var parameters = new List<ParameterInfo>();
		var label = functionName + "(";

		for (var i = 0; i < attr.MaxArgs; i++)
		{
			var paramName = GetSignatureParameterName(i, attr);
			var isOptional = i >= attr.MinArgs;

			if (i > 0) label += ", ";
			if (isOptional) label += "[";
			label += paramName;
			if (isOptional) label += "]";

			parameters.Add(new ParameterInfo(
				paramName,
				isOptional ? $"Optional parameter {i + 1}" : $"Required parameter {i + 1}"));
		}

		label += ")";

		var documentation = $"**Function**: {functionName}\n\n";
		documentation += $"**Arguments**: {attr.MinArgs}-{attr.MaxArgs}\n\n";
		if (attr.Flags != 0)
		{
			documentation += $"**Flags**: {attr.Flags}\n\n";
		}

		return new SignatureInfo(label, documentation, parameters, activeParam);
	}

	private static string GetSignatureParameterName(int index, SharpFunctionAttribute attr)
		=> attr.ParameterNames is { Length: > 0 }
			? ExpandParameterName(attr.ParameterNames, index)
			: $"arg{index + 1}";

	/// <summary>
	/// Expands parameter-name patterns: "param..." → param1, param2, …;
	/// "case...|result..." → alternating case1, result1, case2, result2, ….
	/// </summary>
	private static string ExpandParameterName(string[] parameterNames, int index)
	{
		var currentIndex = 0;

		foreach (var paramName in parameterNames)
		{
			if (paramName.Contains("..."))
			{
				if (paramName.Contains('|'))
				{
					var parts = paramName.Split('|');
					var cleanParts = parts.Select(p => p.Replace("...", "").Trim()).ToArray();

					var pairIndex = (index - currentIndex) / cleanParts.Length;
					var partIndex = (index - currentIndex) % cleanParts.Length;

					return $"{cleanParts[partIndex]}{pairIndex + 1}";
				}

				var cleanName = paramName.Replace("...", "").Trim();
				return $"{cleanName}{index - currentIndex + 1}";
			}

			if (index == currentIndex)
			{
				return paramName;
			}

			currentIndex++;
		}

		return $"arg{index + 1}";
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
