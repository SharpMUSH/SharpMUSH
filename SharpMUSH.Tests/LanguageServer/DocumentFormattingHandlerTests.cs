using NSubstitute;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.LanguageServer.Handlers;
using SharpMUSH.LanguageServer.Services;

namespace SharpMUSH.Tests.LanguageServer;

/// <summary>
/// <see cref="DocumentFormattingHandler"/> returns a whole-document <see cref="TextEdit"/>, so
/// whatever it produces is what the editor writes to the file. That makes the mode gate a
/// correctness question rather than a cosmetic one: a <c>.mush</c>/<c>.mu</c> file is one command per
/// line and <see cref="IMushCodeAnalyzer.FormatIndented"/> has no per-line layout, so laying such a
/// file out as one expression would insert newlines into saved lines — including inside a
/// <c>$pattern:</c>, where a newline changes what the command matches.
/// <para>
/// The analyzer is a substitute that always reports a change. That is the point: it removes every
/// reason the handler could return no edit except the mode gate itself, so the negative test cannot
/// pass for an accidental reason (an input that happened not to reflow, say).
/// </para>
/// </summary>
public class DocumentFormattingHandlerTests
{
	private const string Original = "&CMD #1=$give [a,b] to *:@pemit %#=ok";

	private static (DocumentFormattingHandler Handler, IMushCodeAnalyzer Analyzer) HandlerFor(string uri, string text)
	{
		var documents = new DocumentManager();
		documents.OpenDocument(uri, text, 1);

		var analyzer = Substitute.For<IMushCodeAnalyzer>();
		analyzer.FormatIndented(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<MushAnalysisMode>())
			.Returns("&CMD #1=$give [\n  a,b] to *:@pemit %#=ok");

		return (new DocumentFormattingHandler(documents, analyzer), analyzer);
	}

	private static DocumentFormattingParams Request(string uri) => new()
	{
		TextDocument = new TextDocumentIdentifier(DocumentUri.From(uri)),
		Options = new FormattingOptions()
	};

	[Test]
	public async Task MushDocument_YieldsNoEdit()
	{
		const string uri = "file:///game/quote.mush";
		var (handler, analyzer) = HandlerFor(uri, Original);

		var result = await handler.Handle(Request(uri), CancellationToken.None);

		await Assert.That(result).IsNull();
		// Not merely "no edit was returned": the whole-buffer layout must not have been attempted.
		analyzer.DidNotReceive().FormatIndented(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<MushAnalysisMode>());
	}

	[Test]
	public async Task MuDocument_YieldsNoEdit()
	{
		const string uri = "file:///game/quote.mu";
		var (handler, _) = HandlerFor(uri, Original);

		await Assert.That(await handler.Handle(Request(uri), CancellationToken.None)).IsNull();
	}

	/// <summary>
	/// The gate is specific to the per-line mode. A <c>.mushcmd</c> document really is one command list
	/// end to end, so it still formats — without this the negative tests above would be satisfied by a
	/// handler that had simply been turned off.
	/// </summary>
	[Test]
	public async Task MushCmdDocument_StillYieldsAnEdit()
	{
		const string uri = "file:///game/commands.mushcmd";
		var (handler, _) = HandlerFor(uri, Original);

		var result = await handler.Handle(Request(uri), CancellationToken.None);

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.Count()).IsEqualTo(1);
	}
}
