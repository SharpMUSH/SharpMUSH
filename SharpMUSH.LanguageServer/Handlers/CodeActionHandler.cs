using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.LanguageServer.Handlers;

/// <summary>
/// Handles code action requests for MUSH code.
/// Provides quick fixes for common errors.
/// </summary>
public class CodeActionHandler : CodeActionHandlerBase
{
	private readonly DocumentManager _documentManager;
	private readonly LSPMUSHCodeParser _parser;
	private readonly IMUSHCodeParser _underlyingParser;

	public CodeActionHandler(
		DocumentManager documentManager,
		LSPMUSHCodeParser parser,
		IMUSHCodeParser underlyingParser)
	{
		_documentManager = documentManager;
		_parser = parser;
		_underlyingParser = underlyingParser;
	}

	public override Task<CommandOrCodeActionContainer?> Handle(
		CodeActionParams request,
		CancellationToken cancellationToken)
	{
		var uri = request.TextDocument.Uri.ToString();
		var document = _documentManager.GetDocument(uri);

		if (document == null)
		{
			return Task.FromResult<CommandOrCodeActionContainer?>(null);
		}

		var codeActions = new List<CodeAction>();

		try
		{
			var diagnostics = _parser.GetDiagnostics(document.Text, ParseType.Function);

			// Process diagnostics to suggest fixes
			foreach (var diagnostic in diagnostics)
			{
				if (diagnostic.Severity == Library.Models.DiagnosticSeverity.Error)
				{
					// Check for common error patterns and suggest fixes
					var actions = GetCodeActionsForDiagnostic(diagnostic, request.TextDocument.Uri, document.Text);
					codeActions.AddRange(actions);
				}
			}

			// Check for unclosed parentheses
			var lines = document.Text.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];
				var openCount = line.Count(c => c == '(');
				var closeCount = line.Count(c => c == ')');

				if (openCount > closeCount)
				{
					// Suggest adding closing parentheses
					codeActions.Add(new CodeAction
					{
						Title = $"Add {openCount - closeCount} closing parenthesis",
						Kind = CodeActionKind.QuickFix,
						Edit = new WorkspaceEdit
						{
							Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
							{
								[request.TextDocument.Uri] = new[]
								{
									new TextEdit
									{
										Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
											new Position(i, line.Length),
											new Position(i, line.Length)),
										NewText = new string(')', openCount - closeCount)
									}
								}
							}
						}
					});
				}
			}

			// Check for potential typos in function names
			var functionNames = _underlyingParser.FunctionLibrary.Keys.ToList();
			foreach (var line in lines.Select((text, index) => new { text, index }))
			{
				var words = System.Text.RegularExpressions.Regex.Matches(line.text, @"\b[a-zA-Z_][a-zA-Z0-9_]*\b");
				foreach (System.Text.RegularExpressions.Match match in words)
				{
					var word = match.Value;
					// Check if word looks like a function call (followed by parenthesis)
					if (match.Index + word.Length < line.text.Length && line.text[match.Index + word.Length] == '(')
					{
						// Check if it's not in the function library
						if (!functionNames.Any(f => f.Equals(word, StringComparison.OrdinalIgnoreCase)))
						{
							// Find similar function names
							var similar = functionNames
								.Where(f => LevenshteinDistance(f.ToLower(), word.ToLower()) <= 2)
								.Take(3)
								.ToList();

							foreach (var suggestion in similar)
							{
								codeActions.Add(new CodeAction
								{
									Title = $"Did you mean '{suggestion}'?",
									Kind = CodeActionKind.QuickFix,
									Edit = new WorkspaceEdit
									{
										Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
										{
											[request.TextDocument.Uri] = new[]
											{
												new TextEdit
												{
													Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
														new Position(line.index, match.Index),
														new Position(line.index, match.Index + word.Length)),
													NewText = suggestion
												}
											}
										}
									}
								});
							}
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
#pragma warning disable VSTHRD103
			Console.Error.WriteLine($"Error generating code actions: {ex.Message}");
#pragma warning restore VSTHRD103
		}

		if (codeActions.Count > 0)
		{
			return Task.FromResult<CommandOrCodeActionContainer?>(
				new CommandOrCodeActionContainer(codeActions.Select(ca => new CommandOrCodeAction(ca))));
		}

		return Task.FromResult<CommandOrCodeActionContainer?>(null);
	}

	public override Task<CodeAction> Handle(CodeAction request, CancellationToken cancellationToken)
	{
		// No additional resolution needed for code actions
		return Task.FromResult(request);
	}

	private static List<CodeAction> GetCodeActionsForDiagnostic(
		Library.Models.Diagnostic diagnostic,
		DocumentUri uri,
		string documentText)
	{
		var actions = new List<CodeAction>();

		// Add generic quick fixes based on diagnostic messages
		if (diagnostic.Message.Contains("unexpected", StringComparison.OrdinalIgnoreCase))
		{
			// Could suggest removing unexpected tokens
		}

		return actions;
	}

	private static int LevenshteinDistance(string s1, string s2)
	{
		if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
		if (string.IsNullOrEmpty(s2)) return s1.Length;

		var distances = new int[s1.Length + 1, s2.Length + 1];

		for (int i = 0; i <= s1.Length; i++)
			distances[i, 0] = i;
		for (int j = 0; j <= s2.Length; j++)
			distances[0, j] = j;

		for (int i = 1; i <= s1.Length; i++)
		{
			for (int j = 1; j <= s2.Length; j++)
			{
				var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
				distances[i, j] = Math.Min(
					Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
					distances[i - 1, j - 1] + cost);
			}
		}

		return distances[s1.Length, s2.Length];
	}

	protected override CodeActionRegistrationOptions CreateRegistrationOptions(
		CodeActionCapability capability,
		ClientCapabilities clientCapabilities)
	{
		return new CodeActionRegistrationOptions
		{
			DocumentSelector = TextDocumentSelector.ForPattern("**/*.mush", "**/*.mu"),
			CodeActionKinds = new Container<CodeActionKind>(
				CodeActionKind.QuickFix,
				CodeActionKind.Refactor)
		};
	}
}
