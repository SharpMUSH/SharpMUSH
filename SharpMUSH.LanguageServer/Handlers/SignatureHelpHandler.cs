using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles signature help requests for MUSH code.
/// Displays parameter hints while typing function calls.
/// </summary>
public class SignatureHelpHandler : SignatureHelpHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly IMUSHCodeParser _parser;

	public SignatureHelpHandler(DocumentManager documentManager, IMUSHCodeParser parser)
	{
		_documentManager = documentManager;
		_parser = parser;
	}

	public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<SignatureHelp?>(null);
		}

		try
		{
			var lines = document.Text.Split('\n');
			var line = request.Position.Line < lines.Length ? lines[request.Position.Line] : string.Empty;
			var character = (int)request.Position.Character;

			// Find the function name before the current position
			var functionInfo = FindFunctionAtPosition(line, character);
			if (functionInfo == null)
			{
				return Task.FromResult<SignatureHelp?>(null);
			}

			var (functionName, currentParam) = functionInfo.Value;

			// Look up the function in the library
			if (_parser.FunctionLibrary.TryGetValue(functionName, out var functionDef))
			{
				var attr = functionDef.LibraryInformation.Attribute;
				var signature = BuildSignatureInformation(functionName, attr, currentParam);

				return Task.FromResult<SignatureHelp?>(new SignatureHelp
				{
					Signatures = new Container<SignatureInformation>(signature),
					ActiveSignature = 0,
					ActiveParameter = currentParam
				});
			}
		}
		catch (Exception ex)
		{
			#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating signature help: {ex.Message}");
			#pragma warning restore VSTHRD103
		}

		return Task.FromResult<SignatureHelp?>(null);
	}

	private static (string functionName, int currentParam)? FindFunctionAtPosition(string line, int position)
	{
		// Work backwards from position to find the function call
		var depth = 0;
		var paramCount = 0;
		var i = position - 1;

		// Count parameters by counting commas at depth 0
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
					// Found the opening parenthesis
					// Now find the function name
					var nameEnd = i;
					i--;
					while (i >= 0 && IsWordCharacter(line[i]))
					{
						i--;
					}
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

	private static bool IsWordCharacter(char c)
	{
		return char.IsLetterOrDigit(c) || c == '_';
	}

	private static SignatureInformation BuildSignatureInformation(
		string functionName, 
		Library.Attributes.SharpFunctionAttribute attr,
		int activeParam)
	{
		var parameters = new List<ParameterInformation>();
		var label = functionName + "(";

		// Build parameter list
		for (int i = 0; i < attr.MaxArgs; i++)
		{
			// Use parameter names from the attribute if available
			var paramName = GetParameterName(i, attr);
			var isOptional = i >= attr.MinArgs;

			if (i > 0)
				label += ", ";

			var paramStart = label.Length;
			if (isOptional)
				label += "[";
			
			label += paramName;
			
			if (isOptional)
				label += "]";

			var paramEnd = label.Length;

			parameters.Add(new ParameterInformation
			{
				Label = paramName,
				Documentation = isOptional ? $"Optional parameter {i + 1}" : $"Required parameter {i + 1}"
			});
		}

		label += ")";

		var documentation = $"**Function**: {functionName}\n\n";
		documentation += $"**Arguments**: {attr.MinArgs}-{attr.MaxArgs}\n\n";
		if (attr.Flags != 0)
		{
			documentation += $"**Flags**: {attr.Flags}\n\n";
		}

		return new SignatureInformation
		{
			Label = label,
			Documentation = new StringOrMarkupContent(new MarkupContent
			{
				Kind = MarkupKind.Markdown,
				Value = documentation
			}),
			Parameters = new Container<ParameterInformation>(parameters),
			ActiveParameter = activeParam
		};
	}

	private static string GetParameterName(int index, Library.Attributes.SharpFunctionAttribute attr)
	{
		// Use parameter names from the attribute if available
		if (attr.ParameterNames != null && attr.ParameterNames.Length > 0)
		{
			return ExpandParameterName(attr.ParameterNames, index);
		}
		
		// Fallback to generic parameter name
		return $"arg{index + 1}";
	}

	/// <summary>
	/// Expands parameter names with special patterns:
	/// - "param..." generates "param1", "param2", etc.
	/// - "case...|result..." generates alternating "case1", "result1", "case2", "result2"
	/// - Mixed patterns like ["expression", "case...|result...", "default"] for complex functions
	/// </summary>
	private static string ExpandParameterName(string[] parameterNames, int index)
	{
		// Find which parameter pattern applies to this index
		int currentIndex = 0;
		
		foreach (var paramName in parameterNames)
		{
			if (paramName.Contains("..."))
			{
				// This is a repeating parameter pattern
				if (paramName.Contains("|"))
				{
					// Paired repeating pattern like "case...|result..."
					var parts = paramName.Split('|');
					var cleanParts = parts.Select(p => p.Replace("...", "").Trim()).ToArray();
					
					// Calculate which part of the pair this index represents
					var pairIndex = (index - currentIndex) / cleanParts.Length;
					var partIndex = (index - currentIndex) % cleanParts.Length;
					
					return $"{cleanParts[partIndex]}{pairIndex + 1}";
				}
				else
				{
					// Simple repeating pattern like "value..."
					var cleanName = paramName.Replace("...", "").Trim();
					return $"{cleanName}{index - currentIndex + 1}";
				}
			}
			else
			{
				// Regular fixed parameter
				if (index == currentIndex)
				{
					return paramName;
				}
				currentIndex++;
			}
		}
		
		// If we get here, we've gone past all defined parameters
		return $"arg{index + 1}";
	}

	protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
		SignatureHelpCapability capability, 
		ClientCapabilities clientCapabilities)
	{
		return new SignatureHelpRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			TriggerCharacters = new Container<string>("(", ","),
			RetriggerCharacters = new Container<string>(",")
		};
	}
}
