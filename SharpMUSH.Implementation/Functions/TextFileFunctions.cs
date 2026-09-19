using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "textentries", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> TextEntries(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var separator = args.TryGetValue("1", out var sep)
			? sep.Message!.ToPlainText()
			: " ";

		if (TextFileService == null)
		{
			return new CallState(ErrorMessages.Returns.TextFileServiceNotAvailable);
		}

		try
		{
			var entries = await TextFileService.ListEntriesAsync(fileReference, separator);
			return new CallState(entries);
		}
		catch (FileNotFoundException)
		{
			return new CallState(ErrorMessages.Returns.FileNotFound);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textentries({File})", fileReference);
			return new CallState(ErrorMessages.Returns.Error);
		}
	}

	[SharpFunction(Name = "textfile", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
	public async ValueTask<CallState> TextFile(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var fileReference = args["0"].Message!.ToPlainText();
		var entryName = args["1"].Message!.ToPlainText();

		if (TextFileService == null)
		{
			return new CallState(ErrorMessages.Returns.TextFileServiceNotAvailable);
		}

		try
		{
			var content = await TextFileService.GetEntryAsync(fileReference, entryName);
			return content != null
				? new CallState(content)
				: new CallState(ErrorMessages.Returns.EntryNotFound);
		}
		catch (FileNotFoundException)
		{
			return new CallState(ErrorMessages.Returns.FileNotFound);
		}
		catch (Exception ex)
		{
			Logger?.LogError(ex, "Error in textfile({File}, {Entry})", fileReference, entryName);
			return new CallState(ErrorMessages.Returns.Error);
		}
	}
}
