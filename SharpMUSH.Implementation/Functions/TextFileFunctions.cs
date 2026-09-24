using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// The entries of a text file whose names match <c>&lt;pattern&gt;</c>, joined by
	/// <c>&lt;osep&gt;</c>.
	/// </summary>
	/// <remarks>
	/// <c>fun_textentries</c> (<c>src/help.c:1170</c>) is
	/// <c>textentries(&lt;type&gt;, &lt;pattern&gt;[, &lt;osep&gt;])</c>: the pattern is required and
	/// the separator is the third argument. SharpMUSH declared it 1..2 and read the separator out of
	/// argument 2, so there was no way to filter and anyone who passed a pattern had it used as a
	/// separator instead.
	/// </remarks>
	[SharpFunction(Name = "textentries", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["type", "pattern", "osep"])]
	public async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var pattern = args["1"].Message!.ToPlainText();
		var separator = args.TryGetValue("2", out var sep)
			? sep.Message!.ToPlainText()
			: " ";

		return await ReadableCorpusAsync(parser, fileReference) switch
		{
			string corpus => await MatchingEntriesAsync(corpus, pattern, separator),
			Error<string> refused => new CallState(refused.Value)
		};

		async ValueTask<CallState> MatchingEntriesAsync(string corpus, string glob, string osep)
		{
			try
			{
				var entries = await TextFileService.SearchEntriesAsync(corpus, glob);
				return new CallState(string.Join(osep, entries.Order(StringComparer.OrdinalIgnoreCase)));
			}
			catch (Exception ex)
			{
				Logger?.LogError(ex, "Error in textentries({File}, {Pattern})", corpus, glob);
				return new CallState(ErrorMessages.Returns.Error);
			}
		}
	}

	/// <summary>
	/// One entry of a text file.
	/// </summary>
	/// <remarks>
	/// <c>fun_textfile</c> (<c>src/help.c:1138</c>) refuses an unknown file and an admin file the
	/// caller has no privileges for before it reads anything, exactly as <see cref="TextEntries"/>
	/// does. Without that gate a mortal could read the <c>ahelp</c> corpus that the AHELP command
	/// refuses them.
	/// </remarks>
	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["type", "entry"])]
	public async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var entryName = args["1"].Message!.ToPlainText();

		return await ReadableCorpusAsync(parser, fileReference) switch
		{
			string corpus => await EntryAsync(corpus, entryName),
			Error<string> refused => new CallState(refused.Value)
		};

		async ValueTask<CallState> EntryAsync(string corpus, string entry)
		{
			try
			{
				var content = await TextFileService.GetEntryAsync(corpus, entry);
				return content is null
					? new CallState(ErrorMessages.Returns.EntryNotFound)
					: new CallState(content);
			}
			catch (Exception ex)
			{
				Logger?.LogError(ex, "Error in textfile({File}, {Entry})", corpus, entry);
				return new CallState(ErrorMessages.Returns.Error);
			}
		}
	}

	/// <summary>
	/// The corpus <paramref name="fileReference"/> names, or why the executor cannot read it.
	/// </summary>
	/// <remarks>
	/// PennMUSH looks the file up in its <c>help_files</c> table and answers
	/// <c>#-1 NO SUCH FILE</c> when it is not there, then refuses an <c>admin</c> file to anyone
	/// without <c>Hasprivs</c> (<c>src/help.c:1142-1150</c>). SharpMUSH's corpora are the category
	/// directories, and its wording for the first is <c>#-1 FILE NOT FOUND</c>.
	/// </remarks>
	private async ValueTask<Result<string>> ReadableCorpusAsync(IMUSHCodeParser parser, string fileReference)
	{
		var corpus = (await TextFileService.ListCategoriesAsync())
			.FirstOrDefault(category => string.Equals(category, fileReference, StringComparison.OrdinalIgnoreCase));

		if (corpus is null) return new Error<string>(ErrorMessages.Returns.FileNotFound);
		if (!string.Equals(corpus, HelpCorpora.Admin, StringComparison.OrdinalIgnoreCase)) return corpus;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await executor.IsPriv() ? corpus : new Error<string>(ErrorMessages.Returns.PermissionDenied);
	}
}
