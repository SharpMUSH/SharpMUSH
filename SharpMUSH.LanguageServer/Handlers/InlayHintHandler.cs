using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Serilog;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles inlay hint requests for MUSH code.
/// Shows parameter names inline in function calls to improve code readability.
/// </summary>
public partial class InlayHintHandler : InlayHintsHandlerBase
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
			Log.Error(ex, "Error generating inlay hints");
			return Task.FromResult<InlayHintContainer?>(null);
		}
	}

	private void ProcessLineForHints(string line, int lineNum, List<InlayHint> hints)
	{
		var matches = FunctionCallWithArgsRegex().Matches(line);

		foreach (Match match in matches)
		{
			var functionName = match.Groups[1].Value;
			var argsText = match.Groups[2].Value;
			var argsAbsoluteStart = match.Groups[2].Index;

			if (_parser.FunctionLibrary.TryGetValue(functionName.ToUpperInvariant(), out var functionDef))
			{
				var attr = functionDef.LibraryInformation.Attribute;

				var argPositions = FindArgumentPositions(argsText);

				for (int i = 0; i < argPositions.Count && i < attr.MaxArgs; i++)
				{
					var argPos = argPositions[i];

					var paramName = GetParameterName(functionName, i, attr);

					hints.Add(new InlayHint
					{
						Position = new Position(lineNum, argsAbsoluteStart + argPos),
						Label = new StringOrInlayHintLabelParts($"{paramName}:"),
						Kind = InlayHintKind.Parameter,
						PaddingRight = true,
						Tooltip = new StringOrMarkupContent($"Parameter {i + 1} of {functionName}")
					});
				}
			}
		}
	}

	private static List<int> FindArgumentPositions(string argsText)
	{
		var positions = new List<int>();
		var depth = 0;
		var i = 0;

		while (i < argsText.Length && char.IsWhiteSpace(argsText[i]))
		{
			i++;
		}

		if (i < argsText.Length)
		{
			positions.Add(i);
		}

		while (i < argsText.Length)
		{
			if (argsText[i] == '(' || argsText[i] == '[' || argsText[i] == '{')
			{
				depth++;
			}
			else if (argsText[i] == ')' || argsText[i] == ']' || argsText[i] == '}')
			{
				depth--;
			}
			else if (argsText[i] == ',' && depth == 0)
			{
				i++;
				while (i < argsText.Length && char.IsWhiteSpace(argsText[i]))
				{
					i++;
				}
				if (i < argsText.Length)
				{
					positions.Add(i);
				}
				continue;
			}

			i++;
		}

		return positions;
	}

	private static string GetParameterName(string functionName, int index, Library.Attributes.SharpFunctionAttribute attr)
	{
		if (attr.ParameterNames != null && attr.ParameterNames.Length > 0)
		{
			return ExpandParameterName(attr.ParameterNames, index);
		}

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
		int currentIndex = 0;

		foreach (var paramName in parameterNames)
		{
			if (paramName.Contains("..."))
			{
				if (paramName.Contains("|"))
				{
					var parts = paramName.Split('|');
					var cleanParts = parts.Select(p => p.Replace("...", "").Trim()).ToArray();

					var pairIndex = (index - currentIndex) / cleanParts.Length;
					var partIndex = (index - currentIndex) % cleanParts.Length;

					return $"{cleanParts[partIndex]}{pairIndex + 1}";
				}
				else
				{
					var cleanName = paramName.Replace("...", "").Trim();
					return $"{cleanName}{index - currentIndex + 1}";
				}
			}
			else
			{
				if (index == currentIndex)
				{
					return paramName;
				}
				currentIndex++;
			}
		}

		return $"arg{index + 1}";
	}

	protected override InlayHintRegistrationOptions CreateRegistrationOptions(
		InlayHintClientCapabilities capability,
		ClientCapabilities clientCapabilities)
	{
		return new InlayHintRegistrationOptions
		{
			DocumentSelector = MushDocument.Selector,
			ResolveProvider = false
		};
	}

	/// <summary>
	/// Resolve method for inlay hints. This implementation provides all hint information
	/// upfront in the Handle method, so resolution is not needed. The hint is returned as-is.
	/// </summary>
	public override Task<InlayHint> Handle(InlayHint request, CancellationToken cancellationToken)
	{
		return Task.FromResult(request);
	}

	[GeneratedRegex(@"(\w+)\s*\(([^)]*)\)")]
	private static partial Regex FunctionCallWithArgsRegex();
}
