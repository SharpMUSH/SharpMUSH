using System.Text;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "HELP", Switches = ["SEARCH"], Behavior = CB.Default | CB.NoParse, MinArgs = 0, MaxArgs = 1, ParameterNames = ["topic"])]
	public static async ValueTask<Option<CallState>> Help(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches;

		if (TextFileService == null)
		{
			await NotifyService!.Notify(executor, "Help system not initialized.", executor);
			return new CallState("#-1 HELP SYSTEM NOT INITIALIZED");
		}

		// No arguments - show main help (PennMUSH shows the command's own entry)
		if (args.Count == 0)
		{
			var mainHelp = await TextFileService.GetEntryAsync("help", "help");
			if (mainHelp != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(mainHelp, mushParser: parser);
				await NotifyService!.Notify(executor, rendered, executor);
			}
			else
			{
				await NotifyService!.Notify(executor, "No help available. Type 'help <topic>' for help on a specific topic.", executor);
			}
			return CallState.Empty;
		}

		var topic = args["0"].Message!.ToPlainText();

		// /search switch - search entry bodies for content containing the term (PennMUSH behavior)
		if (switches.Contains("SEARCH"))
		{
			var matches = (await TextFileService.SearchContentAsync("help", topic))
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.Notify(executor, $"No matches.", executor);
			}
			else
			{
				await NotifyService!.Notify(executor, $"Matches: {string.Join(", ", matches)}", executor);
			}
			return CallState.Empty;
		}

		// Check for wildcard pattern (user explicitly included * or ?)
		if (topic.Contains('*') || topic.Contains('?'))
		{
			var matches = (await TextFileService.SearchEntriesAsync("help", topic))
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (matches.Count == 0)
			{
				await NotifyService!.Notify(executor, $"No entries matching '{topic}' were found.", executor);
			}
			else if (matches.Count == 1)
			{
				var wildcardContent = await TextFileService.GetEntryAsync("help", matches[0]);
				if (wildcardContent != null)
				{
					var rendered = RecursiveMarkdownHelper.RenderMarkdown(wildcardContent, mushParser: parser);
					await NotifyService!.Notify(executor, rendered, executor);
				}
			}
			else
			{
				await NotifyService!.Notify(executor, $"Here are the entries which match '{topic}':", executor);
				await NotifyService!.Notify(executor, string.Join(", ", matches), executor);
			}
			return CallState.Empty;
		}

		// Non-wildcard: PennMUSH does prefix match first (name LIKE 'topic%', takes first alphabetically).
		// If nothing found, build a fuzzy pattern with * between words and at alpha-digit boundaries.
		var prefixMatches = (await TextFileService.SearchEntriesAsync("help", topic + "*"))
			.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (prefixMatches.Count > 0)
		{
			// Show the first alphabetically matching entry (matches PennMUSH's LIMIT 1 ORDER BY name)
			var firstMatch = prefixMatches[0];
			var prefixContent = await TextFileService.GetEntryAsync("help", firstMatch);
			if (prefixContent != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(prefixContent, mushParser: parser);
				await NotifyService!.Notify(executor, rendered, executor);
			}
			return CallState.Empty;
		}

		// Fuzzy pattern fallback: insert * between words (spaces) and at alpha-to-digit boundaries
		var fuzzyPattern = BuildFuzzyPattern(topic);
		var fuzzyMatches = (await TextFileService.SearchEntriesAsync("help", fuzzyPattern))
			.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (fuzzyMatches.Count == 0)
		{
			await NotifyService!.Notify(executor, $"No entry for '{topic}'.", executor);
		}
		else if (fuzzyMatches.Count == 1)
		{
			var fuzzyContent = await TextFileService.GetEntryAsync("help", fuzzyMatches[0]);
			if (fuzzyContent != null)
			{
				var rendered = RecursiveMarkdownHelper.RenderMarkdown(fuzzyContent, mushParser: parser);
				await NotifyService!.Notify(executor, rendered, executor);
			}
		}
		else
		{
			await NotifyService!.Notify(executor, $"Here are the entries which match '{topic}':", executor);
			await NotifyService!.Notify(executor, string.Join(", ", fuzzyMatches), executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Builds a fuzzy wildcard pattern from a plain topic string, matching PennMUSH's behavior:
	/// - Inserts '*' between words (at space boundaries)
	/// - Inserts '*' at transitions from alphabetic to digit characters
	/// Note: digit→alpha transitions do NOT get a wildcard (matches PennMUSH source).
	/// </summary>
	private static string BuildFuzzyPattern(string topic)
	{
		if (string.IsNullOrEmpty(topic))
			return topic;

		var sb = new StringBuilder();
		const int StateNone = 0;   // initial or after whitespace
		const int StateAlpha = 1;  // last seen character was alphabetic
		const int StateDigit = 2;  // last seen character was digit (after alpha)
		var state = StateNone;

		foreach (var c in topic)
		{
			if (char.IsWhiteSpace(c))
			{
				if (state != StateNone)
				{
					state = StateNone;
					sb.Append('*');
				}
				sb.Append(c);
			}
			else if (char.IsAsciiDigit(c))
			{
				if (state == StateAlpha)
				{
					// alpha → digit transition: insert * (PennMUSH behavior)
					state = StateDigit;
					sb.Append('*');
				}
				sb.Append(c);
			}
			else
			{
				if (state != StateAlpha)
					state = StateAlpha;
				sb.Append(c);
			}
		}

		return sb.ToString();
	}
}
