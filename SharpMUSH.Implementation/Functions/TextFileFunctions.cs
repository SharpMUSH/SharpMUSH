using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Authorization;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Common;
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
		var fileReference = args["0"].Message.ToPlainText();
		var pattern = args["1"].Message.ToPlainText();
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ").ToPlainText();

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
	/// The entries of a text file whose text matches <c>&lt;pattern&gt;</c>, joined by <c>&lt;osep&gt;</c>.
	/// </summary>
	/// <remarks>
	/// <c>fun_textsearch</c> (<c>src/help.c:1197</c>) finds the pattern anywhere in an entry's text,
	/// caselessly, with <c>*</c> and <c>?</c> as wildcards, and names each entry once.
	/// </remarks>
	[SharpFunction(Name = "textsearch", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["type", "pattern", "osep"])]
	public async ValueTask<CallState> TextSearch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message.ToPlainText();
		var pattern = args["1"].Message.ToPlainText();
		var separator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ").ToPlainText();

		return await ReadableCorpusAsync(parser, fileReference) switch
		{
			string corpus => new CallState(string.Join(separator,
				(await TextFileService.SearchContentAsync(corpus, pattern)).Order(StringComparer.OrdinalIgnoreCase))),
			Error<string> refused => new CallState(refused.Value)
		};
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
	/// <remarks>Under <c>mux_arguments</c>, a first argument that names no text file is TinyMUX's dynhelp()
	/// (<see cref="ObjectHelpAsync"/>), which takes a third argument.</remarks>
	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["type", "entry", "prefix"])]
	public async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message.ToPlainText();
		var entryName = args["1"].Message.ToPlainText();
		var muxForm = Configuration.CurrentValue.Compatibility.MuxArguments;

		return await ReadableCorpusAsync(parser, fileReference) switch
		{
			_ when !muxForm && parser.CurrentState.ArgumentsOrdered.Count > 2
				=> new CallState(ErrorMessages.Returns.ArgumentCount(_2.Name.ToUpperInvariant(), 2, 2, 3)),
			string corpus when parser.CurrentState.ArgumentsOrdered.Count < 3 => await EntryAsync(corpus, entryName),
			Error<string> { Value: ErrorMessages.Returns.NoSuchFile } or string when muxForm => await ObjectHelpAsync(parser, fileReference, entryName),
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
	/// TinyMUX's <c>dynhelp(&lt;object&gt;, &lt;topic&gt;[, &lt;prefix&gt;])</c>: the object's attribute named by the
	/// prefix, <c>HELP_</c> unless given, and the topic; else the one attribute whose name begins with them both.
	/// The caller must be able to examine the object.
	/// </summary>
	private async ValueTask<CallState> ObjectHelpAsync(IMUSHCodeParser parser, string objectName, string topic)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, objectName, LocateFlags.All) is not AnySharpObject found)
		{
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		if (!await PermissionService.CanExamine(executor, found))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var prefix = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, MarkupText.Empty).ToPlainText()
			is { Length: > 0 } given ? given : "HELP_";
		var name = (prefix + topic).ToUpperInvariant();
		if (await AttributeService.GetAttributeAsync(executor, found, name, IAttributeService.AttributeMode.Read, false)
				is SharpAttribute[] exact)
		{
			return new CallState(exact.Last().Value);
		}

		// The topic is a prefix, not a pattern: a * or ? in it matches only itself.
		var candidates = await AttributeService.GetAttributePatternAsync(executor, found, name + "*", false,
			IAttributeService.AttributePatternMode.Wildcard) is SharpAttribute[] matched
			? matched.Where(attribute => attribute.LongName.StartsWith(name, StringComparison.OrdinalIgnoreCase)).ToArray()
			: [];
		return candidates switch
		{
			[var only] => new CallState(only.Value),
			[] => new CallState(ErrorMessages.Returns.NoSuchTopic),
			_ => new CallState(ErrorMessages.Returns.AmbiguousTopic)
		};
	}

	/// <summary>
	/// The corpus <paramref name="fileReference"/> names, or why the executor cannot read it.
	/// </summary>
	/// <remarks>
	/// PennMUSH looks the file up in its <c>help_files</c> table and answers
	/// <c>#-1 NO SUCH FILE</c> when it is not there, then refuses an <c>admin</c> file to anyone
	/// without <c>Hasprivs</c> (<c>src/help.c:1142-1150</c>). SharpMUSH's corpora are the category
	/// directories, and it answers with the same two strings.
	/// </remarks>
	private async ValueTask<Result<string>> ReadableCorpusAsync(IMUSHCodeParser parser, string fileReference)
	{
		var corpus = (await TextFileService.ListCategoriesAsync())
			.FirstOrDefault(category => string.Equals(category, fileReference, StringComparison.OrdinalIgnoreCase));

		if (corpus is null) return new Error<string>(ErrorMessages.Returns.NoSuchFile);
		if (!string.Equals(corpus, HelpCorpora.Admin, StringComparison.OrdinalIgnoreCase)) return corpus;

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await executor.IsPriv() ? corpus : new Error<string>(PermissionRefusal.Needs(PortalPermission.GameWizard, PortalPermission.GameRoyalty).ReturnText);
	}
}
