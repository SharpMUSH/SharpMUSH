using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles inlay hint requests for MUSH code.
/// Shows parameter names inline in function calls to improve code readability.
/// </summary>
public class InlayHintHandler : InlayHintsHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMUSHCodeParser _parser;

	public InlayHintHandler(DocumentManager documentManager, IMUSHCodeParser parser)
	{
		_documentManager = documentManager;
		_parser = parser;
	}

	public override Task<InlayHintContainer?> Handle(InlayHintParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<InlayHintContainer?>(null);
		}

		try
		{
			var hints = new List<InlayHint>();
			var lines = document.Text.Split('\n');

			// Process each line in the requested range
			var startLine = (int)request.Range.Start.Line;
			var endLine = (int)request.Range.End.Line;

			for (int lineNum = startLine; lineNum <= endLine && lineNum < lines.Length; lineNum++)
			{
				var line = lines[lineNum];
				ProcessLineForHints(line, lineNum, hints);
			}

			return Task.FromResult<InlayHintContainer?>(new InlayHintContainer(hints));
		}
		catch (Exception ex)
		{
			#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating inlay hints: {ex.Message}");
			#pragma warning restore VSTHRD103
			return Task.FromResult<InlayHintContainer?>(null);
		}
	}

	private void ProcessLineForHints(string line, int lineNum, List<InlayHint> hints)
	{
		// Match function calls: functionName(arg1, arg2, ...)
		// Pattern to find function calls with parameters
		var functionPattern = @"(\w+)\s*\(([^)]*)\)";
		var matches = Regex.Matches(line, functionPattern);

		foreach (Match match in matches)
		{
			var functionName = match.Groups[1].Value;
			var argsText = match.Groups[2].Value;
			var argsStart = match.Groups[2].Index;

			// Look up the function in the library
			if (_parser.FunctionLibrary.TryGetValue(functionName.ToUpperInvariant(), out var functionDef))
			{
				var attr = functionDef.LibraryInformation.Attribute;
				
				// Split arguments by commas (simplistic approach)
				var args = SplitArguments(argsText);
				
				// Add hints for each argument
				var currentPosition = argsStart;
				for (int i = 0; i < args.Count && i < attr.MaxArgs; i++)
				{
					var arg = args[i];
					
					// Create parameter name hint
					var paramName = GetParameterName(functionName, i, attr);
					
					// Add hint at the start of this argument
					hints.Add(new InlayHint
					{
						Position = new Position(lineNum, currentPosition),
						Label = new StringOrInlayHintLabelParts($"{paramName}:"),
						Kind = InlayHintKind.Parameter,
						PaddingRight = true,
						Tooltip = new StringOrMarkupContent($"Parameter {i + 1} of {functionName}")
					});

					// Move position past this argument and comma
					currentPosition += arg.Length;
					if (i < args.Count - 1)
					{
						// Skip comma and whitespace
						while (currentPosition < line.Length && (line[currentPosition] == ',' || char.IsWhiteSpace(line[currentPosition])))
						{
							currentPosition++;
						}
					}
				}
			}
		}
	}

	private static List<string> SplitArguments(string argsText)
	{
		var args = new List<string>();
		var current = string.Empty;
		var depth = 0;

		foreach (var ch in argsText)
		{
			if (ch == '(' || ch == '[' || ch == '{')
			{
				depth++;
				current += ch;
			}
			else if (ch == ')' || ch == ']' || ch == '}')
			{
				depth--;
				current += ch;
			}
			else if (ch == ',' && depth == 0)
			{
				args.Add(current.Trim());
				current = string.Empty;
			}
			else
			{
				current += ch;
			}
		}

		if (!string.IsNullOrWhiteSpace(current))
		{
			args.Add(current.Trim());
		}

		return args;
	}

	private static string GetParameterName(string functionName, int index, Library.Attributes.SharpFunctionAttribute attr)
	{
		// Try to provide meaningful parameter names for well-known functions
		return functionName.ToUpperInvariant() switch
		{
			"ADD" => index == 0 ? "num1" : "num2",
			"SUB" => index == 0 ? "num1" : "num2",
			"MUL" => index == 0 ? "num1" : "num2",
			"DIV" => index == 0 ? "dividend" : "divisor",
			"MOD" => index == 0 ? "number" : "modulus",
			"GET" => index == 0 ? "object" : "attribute",
			"SET" => index switch
			{
				0 => "object",
				1 => "attribute",
				_ => "value"
			},
			"NAME" => index == 0 ? "object" : "default",
			"LOC" => "object",
			"OWNER" => "object",
			"CONTROLS" => index == 0 ? "subject" : "object",
			"HASFLAG" => index == 0 ? "object" : "flag",
			"HASPOWER" => index == 0 ? "object" : "power",
			"HASTYPE" => index == 0 ? "object" : "type",
			_ => $"arg{index + 1}"
		};
	}

	protected override InlayHintRegistrationOptions CreateRegistrationOptions(
		InlayHintClientCapabilities capability,
		ClientCapabilities clientCapabilities)
	{
		return new InlayHintRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			ResolveProvider = false
		};
	}

	// Resolve method for inlay hints (not used in this implementation)
	public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken)
	{
		// Resolution not implemented - return the hint as-is
		return Task.FromResult(request);
	}
}
