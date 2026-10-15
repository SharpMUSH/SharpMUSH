using System.Globalization;
using System.Text;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>
/// The whole report of one database import as plain text: what was converted, every error and warning, and
/// each rewritten attribute with its text before and after. The import page offers it as a download and a
/// start-up import writes it to disk, so an administrator can read it at leisure or send it in for help.
/// </summary>
public static class ImportLog
{
	/// <summary>The name a downloaded or written log takes, from the time the import finished.</summary>
	public static string FileName(DateTimeOffset finished)
		=> $"sharpmush-import-{finished.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log";

	/// <summary>The log of <paramref name="result"/>, which finished at <paramref name="finished"/>.</summary>
	public static string Write(ConversionResult result, DateTimeOffset finished)
	{
		var log = new StringBuilder();
		log.AppendLine("SharpMUSH database import log");
		if (result.SourceName.Length > 0)
		{
			log.AppendLine($"Source: {result.SourceName}");
		}

		log.AppendLine(CultureInfo.InvariantCulture, $"Finished: {finished.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
		log.AppendLine(CultureInfo.InvariantCulture, $"Duration: {result.Duration:hh\\:mm\\:ss}");
		log.AppendLine($"Result: {(result.Aborted ? "stopped part-way" : result.IsSuccessful ? "completed" : "completed with errors")}");
		log.AppendLine();

		log.AppendLine("Converted");
		Count(log, "Players", result.PlayersConverted);
		Count(log, "Rooms", result.RoomsConverted);
		Count(log, "Things", result.ThingsConverted);
		Count(log, "Exits", result.ExitsConverted);
		Count(log, "Attributes", result.AttributesConverted);
		Count(log, "Locks", result.LocksConverted);
		Count(log, "Mail aliases", result.MailAliasesConverted);
		Count(log, "Mail messages", result.MailMessagesConverted);
		Count(log, "Channels", result.ChannelsConverted);
		Count(log, "Channel members", result.ChannelMembersConverted);

		Lines(log, "Errors", result.Errors);
		Lines(log, "Warnings", result.Warnings);

		if (result.SoftcodeRewrites.Count > 0)
		{
			log.AppendLine();
			log.AppendLine(CultureInfo.InvariantCulture, $"Rewritten softcode ({result.SoftcodeRewrites.Count})");
			foreach (var rewrite in result.SoftcodeRewrites)
			{
				log.AppendLine();
				log.AppendLine(rewrite.ToString());
				Text(log, "Before", rewrite.Before);
				Text(log, "After", rewrite.After);
			}
		}

		return log.ToString();
	}

	private static void Count(StringBuilder log, string label, int count)
		=> log.AppendLine(CultureInfo.InvariantCulture, $"  {label}: {count}");

	private static void Lines(StringBuilder log, string heading, List<string> lines)
	{
		if (lines.Count == 0)
		{
			return;
		}

		log.AppendLine();
		log.AppendLine(CultureInfo.InvariantCulture, $"{heading} ({lines.Count})");
		foreach (var line in lines)
		{
			log.AppendLine($"  {line}");
		}
	}

	/// <summary>An attribute's text under its label, every line indented so a line of the text never reads as a heading.</summary>
	private static void Text(StringBuilder log, string label, string text)
	{
		log.AppendLine($"  {label}:");
		foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
		{
			log.AppendLine($"    {line}");
		}
	}
}
