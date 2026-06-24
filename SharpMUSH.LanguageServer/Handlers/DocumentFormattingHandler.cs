using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using System.Text.RegularExpressions;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles document formatting requests for MUSH code.
/// Provides basic auto-formatting with consistent style.
/// </summary>
public partial class DocumentFormattingHandler : DocumentFormattingHandlerBase
{
	private readonly DocumentManager _documentManager;

	public DocumentFormattingHandler(DocumentManager documentManager)
	{
		_documentManager = documentManager;
	}

	public override Task<TextEditContainer?> Handle(
		DocumentFormattingParams request,
		CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<TextEditContainer?>(null);
		}

		try
		{
			var lines = document.Text.Split('\n');
			var edits = new List<TextEdit>();

			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				var formatted = FormatLine(line, request.Options);

				if (formatted != line)
				{
					edits.Add(new TextEdit
					{
						Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
							new Position(i, 0),
							new Position(i, line.Length)),
						NewText = formatted
					});
				}
			}

			if (edits.Count > 0)
			{
				return Task.FromResult<TextEditContainer?>(new TextEditContainer(edits));
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error formatting document: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		return Task.FromResult<TextEditContainer?>(null);
	}

	private static string FormatLine(string line, FormattingOptions options)
	{
		var formatted = line.TrimEnd();

		formatted = NormalizeSpacing(formatted);

		formatted = ApplyIndentation(formatted, options);

		return formatted;
	}

	private static string NormalizeSpacing(string line)
	{
		var result = CommaWithoutSpaceRegex().Replace(line, ", ");

		result = CommandWithoutSpaceRegex().Replace(result, "$1 $2");

		return result;
	}

	private static string ApplyIndentation(string line, FormattingOptions options)
	{
		var trimmed = line.TrimStart();
		if (string.IsNullOrEmpty(trimmed))
		{
			return string.Empty;
		}

		var indent = 0;

		if (trimmed.StartsWith(")") || trimmed.StartsWith("}") || trimmed.StartsWith("]"))
		{
			indent = Math.Max(0, indent - 1);
		}

		var indentString = options.InsertSpaces
			? new string(' ', indent * (int)options.TabSize)
			: new string('\t', indent);

		return indentString + trimmed;
	}

	protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
		DocumentFormattingCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new DocumentFormattingRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu")
		};
	}

	[GeneratedRegex(@",(?!\s)")]
	private static partial Regex CommaWithoutSpaceRegex();

	[GeneratedRegex(@"^(@[a-zA-Z]+)([^\s/])")]
	private static partial Regex CommandWithoutSpaceRegex();
}
