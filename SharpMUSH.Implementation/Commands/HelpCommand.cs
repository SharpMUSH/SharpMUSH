using OneOf;
using OneOf.Types;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "HELP", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public async ValueTask<Option<CallState>> Help(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(_mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (TextFileService == null)
		{
			await _notifyService.Notify(executor, "Help system not initialized.");
			return new CallState("#-1 HELP SYSTEM NOT INITIALIZED");
		}

		// No arguments - show main help
		if (args.Count == 0)
		{
			var mainHelp = await _textFileService.GetEntryAsync("help", "help");
			if (mainHelp != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(mainHelp);
				await _notifyService.Notify(executor, rendered);
			}
			else
			{
				await _notifyService.Notify(executor, "No help available. Type 'help <topic>' for help on a specific topic.");
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		// /search switch - search content
		if (switches.Contains("SEARCH"))
		{
			var matches = (await _textFileService.SearchEntriesAsync("help", topic)).ToList();
			if (matches.Count == 0)
			{
				await _notifyService.Notify(executor, $"No help entries found containing '{topic}'.");
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var searchContent = await _textFileService.GetEntryAsync("help", matches[0]);
				if (searchContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(searchContent);
					await _notifyService.Notify(executor, rendered);
				}
			}
			else
			{
				// Multiple matches, list them
				await _notifyService.Notify(executor, $"Help entries containing '{topic}':");
				await _notifyService.Notify(executor, string.Join(", ", matches.OrderBy(x => x)));
			}
			return CallState.Empty;
		}

		// Check for wildcard pattern
		if (topic.Contains('*') || topic.Contains('?'))
		{
			var matches = (await _textFileService.SearchEntriesAsync("help", topic)).ToList();
			if (matches.Count == 0)
			{
				await _notifyService.Notify(executor, $"No help available for '{topic}'.");
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var wildcardContent = await _textFileService.GetEntryAsync("help", matches[0]);
				if (wildcardContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(wildcardContent);
					await _notifyService.Notify(executor, rendered);
				}
			}
			else
			{
				// Multiple matches, list them
				await _notifyService.Notify(executor, $"Help topics matching '{topic}':");
				await _notifyService.Notify(executor, string.Join(", ", matches.OrderBy(x => x)));
			}
			return CallState.Empty;
		}

		// Try exact match
		var exactContent = await _textFileService.GetEntryAsync("help", topic);
		if (exactContent != null)
		{
			var rendered = RecursiveMarkdownHelper.RenderMarkdown(exactContent);
			await _notifyService.Notify(executor, rendered);
		}
		else
		{
			await _notifyService.Notify(executor, $"No help available for '{topic}'.");
			await _notifyService.Notify(executor, "Try 'help <pattern>' with wildcards (*) or 'help/search <text>' to search help content.");
		}

		return CallState.Empty;
	}
}
