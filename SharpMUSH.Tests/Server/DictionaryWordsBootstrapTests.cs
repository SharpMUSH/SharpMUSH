using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <c>dict_file</c> fills the <c>words</c> suggestion category, as PennMUSH's <c>add_dict_words</c>
/// (<c>src/help.c:1700</c>) does, and is read again only when the file changed.
/// </summary>
public class DictionaryWordsBootstrapTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IExpandedObjectDataService Data => WebAppFactoryArg.Services.GetRequiredService<IExpandedObjectDataService>();
	private DictionaryWordsBootstrapService Loader => WebAppFactoryArg.Services.GetRequiredService<DictionaryWordsBootstrapService>();

	private async Task<string> Suggest(string word)
		=> (await Parser.EvaluateAsync(MarkupText.Plain($"suggest(words,{word},|,3)"))).ToPlainText();

	// Replaces the world's suggestion data, which @suggest and every other suggest() reads.
	[Test, NotInParallel]
	public async Task FillsTheWordsCategoryAndKeepsTheOthers()
	{
		var path = Path.Combine(Path.GetTempPath(), TestIsolationHelpers.GenerateUniqueName("dict") + ".txt");
		var original = await Data.GetExpandedServerDataAsync<SuggestionData>();
		var originalStamp = await Data.GetExpandedServerDataAsync<DictionaryFileStamp>();
		try
		{
			await Data.SetExpandedServerDataAsync(new SuggestionData(new Dictionary<string, HashSet<string>>
			{
				["pets"] = ["dog"],
				["words"] = ["stale"]
			}));
			await File.WriteAllTextAsync(path, "Aardvark\nzebra\n\n");

			await Loader.LoadAsync(path, CancellationToken.None);

			await Assert.That(await Suggest("ardvark")).IsEqualTo("aardvark|zebra");
			await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain("suggest(pets,dgo)"))).ToPlainText()).IsEqualTo("dog");
		}
		finally
		{
			File.Delete(path);
			await Data.SetExpandedServerDataAsync(original ?? new SuggestionData());
			await Data.SetExpandedServerDataAsync(originalStamp ?? new DictionaryFileStamp());
		}
	}

	[Test, NotInParallel]
	public async Task ReadsTheFileAgainOnlyWhenItChanged()
	{
		var path = Path.Combine(Path.GetTempPath(), TestIsolationHelpers.GenerateUniqueName("dict") + ".txt");
		var original = await Data.GetExpandedServerDataAsync<SuggestionData>();
		var originalStamp = await Data.GetExpandedServerDataAsync<DictionaryFileStamp>();
		try
		{
			await File.WriteAllTextAsync(path, "first\n");
			File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			await Loader.LoadAsync(path, CancellationToken.None);

			await File.WriteAllTextAsync(path, "second\n");
			File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			await Loader.LoadAsync(path, CancellationToken.None);
			await Assert.That(await Suggest("first")).IsEqualTo("first");

			File.SetLastWriteTimeUtc(path, new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
			await Loader.LoadAsync(path, CancellationToken.None);
			await Assert.That(await Suggest("first")).IsEqualTo("second");
		}
		finally
		{
			File.Delete(path);
			await Data.SetExpandedServerDataAsync(original ?? new SuggestionData());
			await Data.SetExpandedServerDataAsync(originalStamp ?? new DictionaryFileStamp());
		}
	}
}
