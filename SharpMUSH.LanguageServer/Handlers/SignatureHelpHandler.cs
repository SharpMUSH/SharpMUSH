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
			var paramName = $"arg{i + 1}";
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
