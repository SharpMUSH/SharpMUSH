using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@ascii</c> edits the <c>ascii_translations</c> table: one entry per character, set again to replace it, taken
/// out with <c>/remove</c>.
/// </summary>
/// <remarks>
/// <see cref="NotInParallelAttribute"/>: the table is stored inside the one options row, as the sitelock rules are, so
/// two writers at once could clobber each other's read-modify-write. Assertions read
/// <see cref="IOptionsMonitor{TOptions}"/>, which follows reloads in this fixture (see <c>SitelockCommandTests</c>).
/// </remarks>
[NotInParallel]
public class AsciiTranslationCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IOptionsMonitor<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
	private IConfigOptionWriter ConfigWriter => WebAppFactoryArg.Services.GetRequiredService<IConfigOptionWriter>();

	private IReadOnlyDictionary<string, string> Table => Configuration.CurrentValue.AsciiTranslations.Translations;

	private async Task CleanupAsync(params string[] characters)
		=> await ConfigWriter.UpdateAsync(current =>
		{
			var table = new Dictionary<string, string>(current.AsciiTranslations.Translations);
			return characters.Count(character => table.Remove(character)) > 0
				? current with { AsciiTranslations = new AsciiTranslationsOptions(table) }
				: current;
		});

	[Test]
	public async ValueTask SettingACharacterAgain_ReplacesItsText()
	{
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii ⟁=%b-%b"));
			await Assert.That(Table["⟁"]).IsEqualTo(" - ");

			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii ⟁=+"));
			await Assert.That(Table["⟁"]).IsEqualTo("+");
			await Assert.That(Table.Keys.Count(key => key == "⟁")).IsEqualTo(1);
		}
		finally
		{
			await CleanupAsync("⟁");
		}
	}

	[Test]
	public async ValueTask EmptyText_LeavesTheCharacterOut_AndRemoveTakesTheEntryAway()
	{
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii ⧫="));
			await Assert.That(Table["⧫"]).IsEqualTo("");

			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii/remove ⧫"));
			await Assert.That(Table.ContainsKey("⧫")).IsFalse();
		}
		finally
		{
			await CleanupAsync("⧫");
		}
	}

	[Test]
	public async ValueTask WhatCannotWork_IsRefused()
	{
		try
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii x=y"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@ascii ⌘=é"));

			await Assert.That(Table.ContainsKey("x")).IsFalse();
			await Assert.That(Table.ContainsKey("⌘")).IsFalse();
		}
		finally
		{
			await CleanupAsync("x", "⌘");
		}
	}
}
