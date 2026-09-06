using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "HELP", Switches = ["SEARCH"], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public async ValueTask<Option<CallState>> Help(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (HelpTopicResolver == null)
		{
			await NotifyService.Notify(executor, "Help system not initialized.", executor);
			return new CallState(ErrorMessages.Returns.HelpSystemNotInitialized);
		}

		// No arguments - show main help (PennMUSH shows the command's own entry)
		if (args.Count == 0)
		{
			var mainHelp = await HelpTopicResolver.GetExactAsync(HelpCorpora.Help, "help");
			if (mainHelp != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(mainHelp.Markdown, mushParser: parser);
				await NotifyService.Notify(executor, rendered, executor);
			}
			else
			{
				await NotifyService.Notify(executor, "No help available. Type 'help <topic>' for help on a specific topic.", executor);
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		// /search switch - search entry bodies for content containing the term (PennMUSH behavior)
		if (switches.Contains("SEARCH"))
		{
			var matches = await HelpTopicResolver.SearchContentAsync(HelpCorpora.Help, topic);
			if (matches.Count == 0)
			{
				await NotifyService.Notify(executor, $"No matches.", executor);
			}
			else
			{
				await NotifyService.Notify(executor, $"Matches: {string.Join(", ", matches)}", executor);
			}
			return CallState.Empty;
		}

		var isWildcard = topic.Contains('*') || topic.Contains('?');
		var resolution = await HelpTopicResolver.ResolveAsync(HelpCorpora.Help, topic);

		if (resolution.TryPickT0(out var entry, out var notAnEntry))
		{
			var rendered = RecursiveMarkdownHelper.RenderMarkdown(entry.Markdown, mushParser: parser);
			await NotifyService.Notify(executor, rendered, executor);
		}
		else if (notAnEntry.TryPickT0(out var candidates, out _))
		{
			await NotifyService.Notify(executor, $"Here are the entries which match '{topic}':", executor);
			await NotifyService.Notify(executor, string.Join(", ", candidates.Topics), executor);
		}
		else
		{
			// PennMUSH words the miss differently depending on whether the reader wildcarded.
			await NotifyService.Notify(executor, isWildcard
				? $"No entries matching '{topic}' were found."
				: $"No entry for '{topic}'.", executor);
		}

		return CallState.Empty;
	}
}
