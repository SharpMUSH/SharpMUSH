using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration;

public sealed class HelpSourceFallbackServerFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
}

[NotInParallel]
public class PlusHelpSourceFallbackTests
{
	[ClassDataSource<HelpSourceFallbackServerFactory>(Shared = SharedType.PerTestSession)]
	public required HelpSourceFallbackServerFactory Factory { get; init; }
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;
	private ValueTask<CallState> GodAsync(string command) => Parser.CommandParse(1, Connections, MarkupText.Plain(command));
	private sealed record Source(string Object, string Librarian, TestIsolationHelpers.TestPlayer Reader);

	private async Task WithSourceAsync(Func<Source, Task> test, int topics = 2)
	{
		var registry = Factory.Services.GetRequiredService<IPackageRegistryService>();
		var librarian = DBRef.Parse((await registry.GetPackageObjectsAsync("plus-help")).Single(o => o.Ref == "librarian").Objid).ToString();
		await GodAsync($"@teleport {librarian}=#2");
		var source = (await GodAsync("@create Source fallback fixture")).Message.ToPlainText().Trim();
		var reader = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services,
			Factory.Services.GetRequiredService<IMediator>(), Connections, "SourceReader");
		await GodAsync($"&HELP {source}=Source description.");
		for (var i = 0; i < topics; i++) await GodAsync($"&HELP`ENTRY{i:D3} {source}=Readable source entry {i}.");
		await GodAsync($"&SRC`DISCORD-SCENES {librarian}={source}");
		await GodAsync($"&SRC`DISCORD-ALIAS {librarian}={source}");
		try { await test(new Source(source, librarian, reader)); }
		finally
		{
			await GodAsync($"&SRC`DISCORD-SCENES {librarian}=");
			await GodAsync($"&SRC`DISCORD-ALIAS {librarian}=");
			await GodAsync($"@destroy {source}");
		}
	}

	private async Task<MString> ShownAsync(Source source, string command)
	{
		var before = Factory.Notifications.RawCountFor(source.Reader.DbRef);
		await Parser.CommandParse(source.Reader.Handle, Connections, MarkupText.Plain(command));
		return MarkupText.Join(MarkupText.Plain("\n"), Factory.Notifications.RawFor(source.Reader.DbRef).Skip(before)
			.Select(message => message switch { MString markup => markup, string text => MarkupText.Plain(text) }));
	}

	[Test]
	[Arguments("discord-scenes")]
	[Arguments("DISCORD-SCENES")]
	[Arguments("discord-alias")]
	public Task ASourceWithoutATopicShowsTheExistingListingAndClickableQualifiedTopics(string query) => WithSourceAsync(async source =>
	{
		var explicitList = await ShownAsync(source, $"+help/list {query}");
		await Assert.That(explicitList.ToPlainText()).Contains("Topics:").And.Contains("entry000");
		var fallback = await ShownAsync(source, $"+help {query}");
		await Assert.That(fallback.ToPlainText()).IsEqualTo(explicitList.ToPlainText());
		await Assert.That(fallback.Render(MarkupFormat.Html)).Contains($"+help {query.ToLowerInvariant()}/entry000");
	});

	[Test]
	public Task AnExactTopicWinsOverTheSourceName() => WithSourceAsync(async source =>
	{
		await GodAsync($"&HELP`DISCORD-SCENES {source.Object}=Exact topic wins this collision.");
		var shown = (await ShownAsync(source, "+help discord-scenes")).ToPlainText();
		await Assert.That(shown).Contains("Exact topic wins this collision.");
		await Assert.That(shown).DoesNotContain("Topics: discord-scenes");
	});

	[Test]
	[Arguments("empty")]
	[Arguments("hidden")]
	[Arguments("missing")]
	[Arguments("unknown")]
	public Task IneligibleSourcesKeepTheOrdinaryMissAndDoNotDiscloseTopics(string state) => WithSourceAsync(async source =>
	{
		if (state == "empty")
			for (var i = 0; i < 2; i++) await GodAsync($"&HELP`ENTRY{i:D3} {source.Object}=");
		if (state == "hidden")
			for (var i = 0; i < 2; i++) await GodAsync($"@set {source.Object}/HELP`ENTRY{i:D3}=internal");
		if (state == "missing") await GodAsync($"&SRC`DISCORD-SCENES {source.Librarian}=#999999999");
		var query = state == "unknown" ? "unregistered-source-fallback" : "discord-scenes";
		var listed = (await ShownAsync(source, $"+help/list {query}")).ToPlainText();
		await Assert.That(listed).Contains("No such help source:");
		var shown = (await ShownAsync(source, $"+help {query}")).ToPlainText();
		await Assert.That(shown).Contains($"No local topic '{query}'. Try help {query} or +help/search {query}.");
		await Assert.That(shown).DoesNotContain("Readable source entry").And.DoesNotContain("Topics:");
	});

	[Test]
	public Task SourceFallbackPagesUseTheExistingListingAndNextPageCommand() => WithSourceAsync(async source =>
	{
		await Parser.CommandParse(source.Reader.Handle, Connections, MarkupText.Plain("SCREENHEIGHT 14"));
		var explicitList = await ShownAsync(source, "+help/list/2 discord-scenes");
		await Assert.That(explicitList.ToPlainText()).Contains("page 2 of");
		var fallback = await ShownAsync(source, "+help/2 discord-scenes");
		await Assert.That(fallback.ToPlainText()).IsEqualTo(explicitList.ToPlainText());
		await Assert.That(fallback.ToPlainText()).Contains("+help/list/3 discord-scenes");
		await Assert.That(fallback.Render(MarkupFormat.Html)).Contains("+help discord-scenes/entry");
	}, topics: 40);
}
