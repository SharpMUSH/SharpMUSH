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
	public static async ValueTask<Option<CallState>> News(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (TextFileService == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsSystemNotInitialized));
			return new CallState("#-1 NEWS SYSTEM NOT INITIALIZED");
		}

		// No arguments - show main news
		if (args.Count == 0)
		{
			var mainNews = await TextFileService.GetEntryAsync("news", "news");
			if (mainNews != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(mainNews);
				await NotifyService!.Notify(executor, rendered, executor);
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoTopicAvailable));
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		// /search switch - search content
		if (switches.Contains("SEARCH"))
		{
			var matches = (await TextFileService.SearchEntriesAsync("news", topic)).ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoEntriesFoundContaining), topic);
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var searchContent = await TextFileService.GetEntryAsync("news", matches[0]);
				if (searchContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(searchContent);
					await NotifyService!.Notify(executor, rendered, executor);
				}
			}
			else
			{
				// Multiple matches, list them
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsEntriesContaining), topic);
				await NotifyService!.Notify(executor, string.Join(", ", matches.OrderBy(x => x)), executor);
			}
			return CallState.Empty;
		}

		// Check for wildcard pattern
		if (topic.Contains('*') || topic.Contains('?'))
		{
			var matches = (await TextFileService.SearchEntriesAsync("news", topic)).ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), topic);
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var wildcardContent = await TextFileService.GetEntryAsync("news", matches[0]);
				if (wildcardContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(wildcardContent);
					await NotifyService!.Notify(executor, rendered, executor);
				}
			}
			else
			{
				// Multiple matches, list them
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsTopicsMatchingFormat), topic);
				await NotifyService!.Notify(executor, string.Join(", ", matches.OrderBy(x => x)), executor);
			}
			return CallState.Empty;
		}

		// Try exact match
		var exactContent = await TextFileService.GetEntryAsync("news", topic);
		if (exactContent != null)
		{
			var rendered = RecursiveMarkdownHelper.RenderMarkdown(exactContent);
			await NotifyService!.Notify(executor, rendered, executor);
		}
		else
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsNoNewsForTopic), topic);
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NewsTryPattern));
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "AHELP", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public static async ValueTask<Option<CallState>> Ahelp(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		// Permission check - only wizards and royalty
		if (!await executor.IsWizard())
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AdminCommandOnly));
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (TextFileService == null)
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpSystemNotInitialized));
			return new CallState("#-1 AHELP SYSTEM NOT INITIALIZED");
		}

		// No arguments - show main admin help
		if (args.Count == 0)
		{
			var mainAhelp = await TextFileService.GetEntryAsync("ahelp", "ahelp");
			if (mainAhelp != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(mainAhelp);
				await NotifyService!.Notify(executor, rendered, executor);
			}
			else
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoHelpAvailable));
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		// /search switch - search content
		if (switches.Contains("SEARCH"))
		{
			var matches = (await TextFileService.SearchEntriesAsync("ahelp", topic)).ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoEntriesFoundContaining), topic);
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var searchContent = await TextFileService.GetEntryAsync("ahelp", matches[0]);
				if (searchContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(searchContent);
					await NotifyService!.Notify(executor, rendered, executor);
				}
			}
			else
			{
				// Multiple matches, list them
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpEntriesContaining), topic);
				await NotifyService!.Notify(executor, string.Join(", ", matches.OrderBy(x => x)), executor);
			}
			return CallState.Empty;
		}

		// Check for wildcard pattern
		if (topic.Contains('*') || topic.Contains('?'))
		{
			var matches = (await TextFileService.SearchEntriesAsync("ahelp", topic)).ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoHelpForTopic), topic);
			}
			else if (matches.Count == 1)
			{
				// Only one match, show it
				var wildcardContent = await TextFileService.GetEntryAsync("ahelp", matches[0]);
				if (wildcardContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(wildcardContent);
					await NotifyService!.Notify(executor, rendered, executor);
				}
			}
			else
			{
				// Multiple matches, list them
				await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpTopicsMatchingFormat), topic);
				await NotifyService!.Notify(executor, string.Join(", ", matches.OrderBy(x => x)), executor);
			}
			return CallState.Empty;
		}

		// Try exact match
		var exactContent = await TextFileService.GetEntryAsync("ahelp", topic);
		if (exactContent != null)
		{
			var rendered = RecursiveMarkdownHelper.RenderMarkdown(exactContent);
			await NotifyService!.Notify(executor, rendered, executor);
		}
		else
		{
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpNoHelpForTopic), topic);
			await NotifyService!.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AhelpTryPattern));
		}

		return CallState.Empty;
	}

	// ANEWS is an alias for AHELP in PennMUSH
	[SharpCommand(Name = "ANEWS", Switches = ["SEARCH"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public static async ValueTask<Option<CallState>> Anews(IMUSHCodeParser parser, SharpCommandAttribute attr)
	{
		// Just forward to Ahelp
		return await Ahelp(parser, attr);
	}
}
