using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "NEWS", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public async ValueTask<Option<CallState>> News(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (HelpTopicResolver == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsSystemNotInitialized), executor);
			return new CallState(ErrorMessages.Returns.NewsSystemNotInitialized);
		}

		if (args.Count == 0)
		{
			var mainNews = await HelpTopicResolver.GetExactAsync(HelpCorpora.News, "news");
			if (mainNews != null)
			{
				await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(mainNews.Markdown), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoTopicAvailable), executor);
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		if (switches.Contains("SEARCH"))
		{
			var matches = await HelpTopicResolver.SearchTopicsAsync(HelpCorpora.News, topic);
			if (matches.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoEntriesFoundContaining), executor, topic);
			}
			else if (matches.Count == 1)
			{
				var searchContent = await HelpTopicResolver.GetExactAsync(HelpCorpora.News, matches[0]);
				if (searchContent != null)
				{
					await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(searchContent.Markdown), executor);
				}
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsEntriesContaining), executor, topic);
				await NotifyService.Notify(executor, string.Join(", ", matches), executor);
			}
			return CallState.Empty;
		}

		var isWildcard = topic.Contains('*') || topic.Contains('?');
		var resolution = await HelpTopicResolver.ResolveAsync(HelpCorpora.News, topic);

		if (resolution.TryPickT0(out var entry, out var notAnEntry))
		{
			await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(entry.Markdown), executor);
		}
		else if (notAnEntry.TryPickT0(out var candidates, out _))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsTopicsMatchingFormat), executor, topic);
			await NotifyService.Notify(executor, string.Join(", ", candidates.Topics), executor);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), executor, topic);
			if (!isWildcard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsTryPattern), executor);
			}
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "AHELP", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public async ValueTask<Option<CallState>> Ahelp(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AdminCommandOnly), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (HelpTopicResolver == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpSystemNotInitialized), executor);
			return new CallState(ErrorMessages.Returns.AhelpSystemNotInitialized);
		}

		if (args.Count == 0)
		{
			var mainAhelp = await HelpTopicResolver.GetExactAsync(HelpCorpora.Admin, "ahelp");
			if (mainAhelp != null)
			{
				await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(mainAhelp.Markdown), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoHelpAvailable), executor);
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		if (switches.Contains("SEARCH"))
		{
			var matches = await HelpTopicResolver.SearchTopicsAsync(HelpCorpora.Admin, topic);
			if (matches.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoEntriesFoundContaining), executor, topic);
			}
			else if (matches.Count == 1)
			{
				var searchContent = await HelpTopicResolver.GetExactAsync(HelpCorpora.Admin, matches[0]);
				if (searchContent != null)
				{
					await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(searchContent.Markdown), executor);
				}
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpEntriesContaining), executor, topic);
				await NotifyService.Notify(executor, string.Join(", ", matches), executor);
			}
			return CallState.Empty;
		}

		var isWildcard = topic.Contains('*') || topic.Contains('?');
		var resolution = await HelpTopicResolver.ResolveAsync(HelpCorpora.Admin, topic);

		if (resolution.TryPickT0(out var entry, out var notAnEntry))
		{
			await NotifyService.Notify(executor, RecursiveMarkdownHelper.RenderMarkdown(entry.Markdown), executor);
		}
		else if (notAnEntry.TryPickT0(out var candidates, out _))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpTopicsMatchingFormat), executor, topic);
			await NotifyService.Notify(executor, string.Join(", ", candidates.Topics), executor);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoHelpForTopic), executor, topic);
			if (!isWildcard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpTryPattern), executor);
			}
		}

		return CallState.Empty;
	}

	// ANEWS is an alias for AHELP in PennMUSH
	[SharpCommand(Name = "ANEWS", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public async ValueTask<Option<CallState>> Anews(IMUSHCodeParser parser, SharpCommandAttribute attr)
	{
		return await Ahelp(parser, attr);
	}
}
