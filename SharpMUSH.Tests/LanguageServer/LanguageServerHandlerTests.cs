using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.LanguageServer.Handlers;
using SharpMUSH.LanguageServer.Services;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace SharpMUSH.Tests.LanguageServer;

/// <summary>
/// Each language server handler over the real standalone analysis the server builds at startup
/// (<see cref="BuiltInAnalysisLibraries"/>, no world), so an answer is the one an editor gets.
/// </summary>
public class LanguageServerHandlerTests
{
	private const string FunctionFile = "file:///game/code.mushfn";
	private const string CommandFile = "file:///game/code.mush";

	private sealed class DefaultOptions : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = SharpMUSHOptions.Default();
	}

	private static readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();

	private static readonly Lazy<IMUSHCodeParser> SharedParser = new(() =>
	{
		var (functions, commands) = BuiltInAnalysisLibraries.Create();
		return new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance, functions, commands, new DefaultOptions(), Services);
	});

	private static IMUSHCodeParser Parser => SharedParser.Value;

	private static DocumentManager Documents(params (string Uri, string Text)[] documents)
	{
		var manager = new DocumentManager();
		foreach (var (uri, text) in documents)
		{
			manager.OpenDocument(uri, text, 1);
		}

		return manager;
	}

	private static TextDocumentIdentifier Document(string uri) => new(DocumentUri.From(uri));

	private static LspRange RangeOf(int line, int start, int end) => new(new Position(line, start), new Position(line, end));

	[Test]
	public async Task Hover_OnAFunctionName_DescribesItsSignature()
	{
		var handler = new HoverHandler(Documents((FunctionFile, "add(1,2)")), new MushCodeAnalyzer(Parser));

		var hover = await handler.Handle(new HoverParams { TextDocument = Document(FunctionFile), Position = new Position(0, 1) },
			CancellationToken.None);

		await Assert.That(hover).IsNotNull();
		await Assert.That(hover!.Contents.MarkupContent!.Kind).IsEqualTo(MarkupKind.Markdown);
		await Assert.That(hover.Contents.MarkupContent.Value).Contains("### Function: `add`");
		await Assert.That(hover.Range).IsEqualTo(RangeOf(0, 0, 3));
	}

	[Test]
	public async Task Hover_OnPunctuationOrAClosedDocument_ShowsNothing()
	{
		var handler = new HoverHandler(Documents((FunctionFile, "add(1,2)")), new MushCodeAnalyzer(Parser));

		var afterCall = await handler.Handle(new HoverParams { TextDocument = Document(FunctionFile), Position = new Position(0, 8) },
			CancellationToken.None);
		var closed = await handler.Handle(new HoverParams { TextDocument = Document("file:///game/other.mushfn"), Position = new Position(0, 1) },
			CancellationToken.None);

		await Assert.That(afterCall).IsNull();
		await Assert.That(closed).IsNull();
	}

	[Test]
	public async Task Completion_OffersFunctionsForThePrefixAsSnippets()
	{
		var handler = new CompletionHandler(Documents((FunctionFile, "[strle")), new MushCodeAnalyzer(Parser));

		var list = await handler.Handle(new CompletionParams { TextDocument = Document(FunctionFile), Position = new Position(0, 6) },
			CancellationToken.None);

		var strlen = list.Items.Single(item => item.Label.Equals("strlen", StringComparison.OrdinalIgnoreCase));
		await Assert.That(strlen.Kind).IsEqualTo(CompletionItemKind.Function);
		await Assert.That(strlen.InsertTextFormat).IsEqualTo(InsertTextFormat.Snippet);
		await Assert.That(list.Items.All(item => item.Label.StartsWith("strle", StringComparison.OrdinalIgnoreCase))).IsTrue();
		await Assert.That(list.IsIncomplete).IsFalse();
	}

	[Test]
	public async Task Completion_OffersCommandsAsKeywords()
	{
		var handler = new CompletionHandler(Documents((CommandFile, "@pem")), new MushCodeAnalyzer(Parser));

		var list = await handler.Handle(new CompletionParams { TextDocument = Document(CommandFile), Position = new Position(0, 4) },
			CancellationToken.None);

		var pemit = list.Items.Single(item => item.Label.Equals("@pemit", StringComparison.OrdinalIgnoreCase));
		await Assert.That(pemit.Kind).IsEqualTo(CompletionItemKind.Keyword);
		await Assert.That(pemit.InsertTextFormat).IsEqualTo(InsertTextFormat.PlainText);
	}

	[Test]
	public async Task Completion_ForAClosedDocument_IsEmpty()
	{
		var handler = new CompletionHandler(new DocumentManager(), new MushCodeAnalyzer(Parser));

		var list = await handler.Handle(new CompletionParams { TextDocument = Document(FunctionFile), Position = new Position(0, 0) },
			CancellationToken.None);

		await Assert.That(list.Items).IsEmpty();
	}

	[Test]
	public async Task SignatureHelp_TracksTheArgumentBeingTyped()
	{
		var handler = new SignatureHelpHandler(Documents((FunctionFile, "add(1,")), new MushCodeAnalyzer(Parser));

		var help = await handler.Handle(new SignatureHelpParams { TextDocument = Document(FunctionFile), Position = new Position(0, 6) },
			CancellationToken.None);

		await Assert.That(help).IsNotNull();
		await Assert.That(help!.ActiveSignature).IsEqualTo(0);
		await Assert.That(help.ActiveParameter).IsEqualTo(1);
		var signature = help.Signatures.Single();
		await Assert.That(signature.Label).Contains("add");
		await Assert.That(signature.ActiveParameter).IsEqualTo(1);
		await Assert.That(signature.Documentation!.MarkupContent!.Kind).IsEqualTo(MarkupKind.Markdown);
	}

	[Test]
	public async Task SignatureHelp_OutsideAKnownFunction_ShowsNothing()
	{
		var handler = new SignatureHelpHandler(Documents((FunctionFile, "nosuchfunction(1,")), new MushCodeAnalyzer(Parser));

		var unknown = await handler.Handle(new SignatureHelpParams { TextDocument = Document(FunctionFile), Position = new Position(0, 17) },
			CancellationToken.None);
		var closed = await handler.Handle(new SignatureHelpParams { TextDocument = Document(CommandFile), Position = new Position(0, 0) },
			CancellationToken.None);

		await Assert.That(unknown).IsNull();
		await Assert.That(closed).IsNull();
	}

	[Test]
	public async Task DocumentSymbols_ListAttributeDefinitionsAndCommands()
	{
		var handler = new DocumentSymbolHandler(Documents((CommandFile, "&GREET me=hello\n@pemit me=hi")), new MushCodeAnalyzer(Parser));

		var symbols = await handler.Handle(new DocumentSymbolParams { TextDocument = Document(CommandFile) }, CancellationToken.None);

		await Assert.That(symbols).IsNotNull();
		var found = symbols!.Select(symbol => symbol.DocumentSymbol!).ToList();
		var greet = found.Single(symbol => symbol.Name == "GREET");
		await Assert.That(greet.Kind).IsEqualTo(SymbolKind.Property);
		await Assert.That(greet.Range).IsEqualTo(RangeOf(0, 0, 15));
		var pemit = found.Single(symbol => symbol.Name == "@pemit");
		await Assert.That(pemit.Kind).IsEqualTo(SymbolKind.Method);
		await Assert.That(pemit.SelectionRange.Start.Line).IsEqualTo(1);
	}

	[Test]
	public async Task DocumentSymbols_ForPlainTextOrAClosedDocument_AreNull()
	{
		var handler = new DocumentSymbolHandler(Documents((CommandFile, "just words")), new MushCodeAnalyzer(Parser));

		var plain = await handler.Handle(new DocumentSymbolParams { TextDocument = Document(CommandFile) }, CancellationToken.None);
		var closed = await handler.Handle(new DocumentSymbolParams { TextDocument = Document(FunctionFile) }, CancellationToken.None);

		await Assert.That(plain).IsNull();
		await Assert.That(closed).IsNull();
	}

	private const string AttributeDocument = "&GREET me=hello\n@set me/GREET=hi\nthink u(me/GREET) u(me/GREETING)";

	[Test]
	public async Task Definition_FindsTheAmpersandAndSetDefinitions()
	{
		var handler = new DefinitionHandler(Documents((CommandFile, AttributeDocument)));

		// On GREET in u(me/GREET).
		var result = await handler.Handle(new DefinitionParams { TextDocument = Document(CommandFile), Position = new Position(2, 13) },
			CancellationToken.None);

		await Assert.That(result).IsNotNull();
		var ranges = result!.Select(location => location.Location!.Range).ToList();
		await Assert.That(ranges).IsEquivalentTo([RangeOf(0, 0, 6), RangeOf(1, 8, 13)]);
	}

	[Test]
	public async Task Definition_OfAWordWithNoDefinition_IsNull()
	{
		var handler = new DefinitionHandler(Documents((CommandFile, AttributeDocument)));

		var undefined = await handler.Handle(new DefinitionParams { TextDocument = Document(CommandFile), Position = new Position(2, 2) },
			CancellationToken.None);
		var onSpace = await handler.Handle(new DefinitionParams { TextDocument = Document(CommandFile), Position = new Position(2, 17) },
			CancellationToken.None);
		var closed = await handler.Handle(new DefinitionParams { TextDocument = Document(FunctionFile), Position = new Position(0, 0) },
			CancellationToken.None);

		await Assert.That(undefined).IsNull();
		await Assert.That(onSpace).IsNull();
		await Assert.That(closed).IsNull();
	}

	[Test]
	public async Task References_MatchWholeWordsOnly()
	{
		var handler = new ReferencesHandler(Documents((CommandFile, AttributeDocument)));

		var all = await handler.Handle(new ReferenceParams
		{
			TextDocument = Document(CommandFile),
			Position = new Position(2, 13),
			Context = new ReferenceContext { IncludeDeclaration = true }
		}, CancellationToken.None);

		await Assert.That(all).IsNotNull();
		// GREETING on line 2 is not a reference to GREET.
		await Assert.That(all!.Select(location => location.Range).ToList())
			.IsEquivalentTo([RangeOf(0, 1, 6), RangeOf(1, 8, 13), RangeOf(2, 11, 16)]);
	}

	[Test]
	public async Task References_WithoutDeclarations_LeaveOutTheDefiningLines()
	{
		var handler = new ReferencesHandler(Documents((CommandFile, AttributeDocument)));

		var uses = await handler.Handle(new ReferenceParams
		{
			TextDocument = Document(CommandFile),
			Position = new Position(2, 13),
			Context = new ReferenceContext { IncludeDeclaration = false }
		}, CancellationToken.None);

		await Assert.That(uses).IsNotNull();
		await Assert.That(uses!.Select(location => location.Range).ToList()).IsEquivalentTo([RangeOf(2, 11, 16)]);
	}

	[Test]
	public async Task Rename_ReplacesEveryWholeWordOccurrence()
	{
		var handler = new RenameHandler(Documents((CommandFile, AttributeDocument)));

		var edit = await handler.Handle(new RenameParams
		{
			TextDocument = Document(CommandFile),
			Position = new Position(0, 3),
			NewName = "WELCOME"
		}, CancellationToken.None);

		await Assert.That(edit).IsNotNull();
		var edits = edit!.Changes![DocumentUri.From(CommandFile)].ToList();
		await Assert.That(edits.All(change => change.NewText == "WELCOME")).IsTrue();
		await Assert.That(edits.Select(change => change.Range).ToList())
			.IsEquivalentTo([RangeOf(0, 1, 6), RangeOf(1, 8, 13), RangeOf(2, 11, 16)]);
	}

	[Test]
	public async Task Rename_OnWhitespaceOrAClosedDocument_IsNull()
	{
		var handler = new RenameHandler(Documents((CommandFile, AttributeDocument)));

		var onSpace = await handler.Handle(new RenameParams { TextDocument = Document(CommandFile), Position = new Position(2, 17), NewName = "X" },
			CancellationToken.None);
		var closed = await handler.Handle(new RenameParams { TextDocument = Document(FunctionFile), Position = new Position(0, 0), NewName = "X" },
			CancellationToken.None);

		await Assert.That(onSpace).IsNull();
		await Assert.That(closed).IsNull();
	}

	[Test]
	public async Task WorkspaceSymbols_SearchEveryOpenDocument()
	{
		var handler = new WorkspaceSymbolsHandler(Documents(
			("file:///game/a.mush", "&GREET me=hi"),
			("file:///game/b.mush", "@pemit me=[strlen(abc)]")));

		var everything = await handler.Handle(new WorkspaceSymbolParams { Query = "" }, CancellationToken.None);
		var filtered = await handler.Handle(new WorkspaceSymbolParams { Query = "GRE" }, CancellationToken.None);
		var none = await handler.Handle(new WorkspaceSymbolParams { Query = "nothing-like-this" }, CancellationToken.None);

		await Assert.That(everything).IsNotNull();
		await Assert.That(everything!.Select(symbol => (symbol.Name, symbol.ContainerName ?? "")).ToList())
			.IsEquivalentTo([("GREET", "Attributes"), ("strlen", "Functions"), ("@pemit", "Commands")]);
		await Assert.That(filtered!.Single().Location.Location!.Uri).IsEqualTo(DocumentUri.From("file:///game/a.mush"));
		await Assert.That(none).IsNull();
	}

	[Test]
	public async Task InlayHints_NameEachArgumentOfAKnownFunction()
	{
		var handler = new InlayHintHandler(Documents((FunctionFile, "strdistance(abc, abd)\nnosuch(1,2)")), Parser);

		var hints = await handler.Handle(new InlayHintParams { TextDocument = Document(FunctionFile), Range = RangeOf(0, 0, 0) with { End = new Position(1, 0) } },
			CancellationToken.None);

		await Assert.That(hints).IsNotNull();
		await Assert.That(hints!.Select(hint => (hint.Label.String ?? "", hint.Position)).ToList())
			.IsEquivalentTo([("source:", new Position(0, 12)), ("target:", new Position(0, 17))]);
		await Assert.That(hints.All(hint => hint.Kind == InlayHintKind.Parameter)).IsTrue();
	}

	[Test]
	public async Task InlayHints_NumberTheRepeatedArgumentsOfAVariadicFunction()
	{
		var handler = new InlayHintHandler(Documents((FunctionFile, "printf(%s %s,a,b)")), Parser);

		var hints = await handler.Handle(new InlayHintParams { TextDocument = Document(FunctionFile), Range = RangeOf(0, 0, 0) },
			CancellationToken.None);

		await Assert.That(hints!.Select(hint => hint.Label.String ?? "").ToList()).IsEquivalentTo(["format:", "value1:", "value2:"]);
	}

	[Test]
	public async Task CodeActions_OfferToCloseParenthesesAndFixMisspelledFunctions()
	{
		var handler = new CodeActionHandler(Documents((FunctionFile, "strlem(abc")), new LSPMUSHCodeParser(Parser), Parser);

		var actions = await handler.Handle(new CodeActionParams
		{
			TextDocument = Document(FunctionFile),
			Range = RangeOf(0, 0, 0),
			Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>() }
		}, CancellationToken.None);

		await Assert.That(actions).IsNotNull();
		var byTitle = actions!.Select(action => action.CodeAction!).ToDictionary(action => action.Title);
		var close = byTitle["Add 1 closing parenthesis"];
		var closeEdit = close.Edit!.Changes![DocumentUri.From(FunctionFile)].Single();
		await Assert.That(closeEdit.Range).IsEqualTo(RangeOf(0, 10, 10));
		await Assert.That(closeEdit.NewText).IsEqualTo(")");
		var rename = byTitle.Keys.Single(title => title.Equals("Did you mean 'strlen'?", StringComparison.OrdinalIgnoreCase));
		var renameEdit = byTitle[rename].Edit!.Changes![DocumentUri.From(FunctionFile)].Single();
		await Assert.That(renameEdit.Range).IsEqualTo(RangeOf(0, 0, 6));
	}

	[Test]
	public async Task CodeActions_ForCorrectCode_AreNull()
	{
		var handler = new CodeActionHandler(Documents((FunctionFile, "strlen(abc)")), new LSPMUSHCodeParser(Parser), Parser);

		var actions = await handler.Handle(new CodeActionParams
		{
			TextDocument = Document(FunctionFile),
			Range = RangeOf(0, 0, 0),
			Context = new CodeActionContext { Diagnostics = new Container<Diagnostic>() }
		}, CancellationToken.None);

		await Assert.That(actions).IsNull();
	}

	[Test]
	[Arguments(FunctionFile, "add(1,strlen(abc))")]
	[Arguments(CommandFile, "think add(1,2)\n@pemit me=[strlen(abc)]")]
	public async Task SemanticTokens_AreTheParsersTokens(string uri, string text)
	{
		var parser = new LSPMUSHCodeParser(Parser);
		var handler = new SemanticTokensHandler(Documents((uri, text)), parser);
		var options = ((IRegistration<SemanticTokensRegistrationOptions, SemanticTokensCapability>)handler)
			.GetRegistrationOptions(new SemanticTokensCapability(), new ClientCapabilities());

		var tokens = await handler.Handle(new SemanticTokensParams { TextDocument = Document(uri) }, CancellationToken.None);

		// Both are delta-encoded: each token's line and column count from the token before it.
		var expected = parser.GetSemanticTokens(text, MushParseMode.ForFileName(uri).ToParseType());
		await Assert.That(expected.Data.Length).IsGreaterThan(10);
		await Assert.That(tokens!.Data.ToArray()).IsEquivalentTo(expected.Data, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(options.Legend.TokenTypes.Select(type => type.ToString()).ToList())
			.IsEquivalentTo(expected.TokenTypes.Select(type => type.ToLowerInvariant()).ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	private static List<PublishDiagnosticsParams> Published(ITextDocumentLanguageServer textDocument)
		=> textDocument.ReceivedCalls()
			.SelectMany(call => call.GetArguments())
			.OfType<PublishDiagnosticsParams>()
			.ToList();

	[Test]
	public async Task DocumentSync_PublishesDiagnosticsAsTheDocumentChangesAndClearsThemOnClose()
	{
		var documents = new DocumentManager();
		var facade = Substitute.For<ILanguageServerFacade>();
		var textDocument = Substitute.For<ITextDocumentLanguageServer>();
		facade.TextDocument.Returns(textDocument);
		var handler = new TextDocumentSyncHandler(documents, new LSPMUSHCodeParser(Parser), facade);
		var uri = DocumentUri.From(FunctionFile);

		await handler.Handle(new DidOpenTextDocumentParams
		{
			TextDocument = new TextDocumentItem { Uri = uri, Text = "add(1,2", Version = 1, LanguageId = "mush" }
		}, CancellationToken.None);
		var afterOpen = Published(textDocument);

		await handler.Handle(new DidChangeTextDocumentParams
		{
			TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
			ContentChanges = new Container<TextDocumentContentChangeEvent>(new TextDocumentContentChangeEvent { Text = "add(1,2)" })
		}, CancellationToken.None);
		var afterChange = Published(textDocument);
		var changed = documents.GetDocument(FunctionFile);

		await handler.Handle(new DidCloseTextDocumentParams { TextDocument = Document(FunctionFile) }, CancellationToken.None);
		var afterClose = Published(textDocument);

		await Assert.That(afterOpen.Single().Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsTrue();
		await Assert.That(afterChange.Count).IsEqualTo(2);
		await Assert.That(afterChange[1].Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)).IsFalse();
		await Assert.That(changed).IsEqualTo(new DocumentState("add(1,2)", 2));
		await Assert.That(afterClose.Count).IsEqualTo(3);
		await Assert.That(afterClose[2].Diagnostics).IsEmpty();
		await Assert.That(documents.HasDocument(FunctionFile)).IsFalse();
		await Assert.That(handler.GetTextDocumentAttributes(uri).LanguageId).IsEqualTo("mush");
	}
}
