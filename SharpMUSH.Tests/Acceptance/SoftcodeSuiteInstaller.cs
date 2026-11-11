using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.DatabaseConversion.Softcode;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services.Imports;

namespace SharpMUSH.Tests.Acceptance;

/// <summary>
/// Installs a published softcode suite into a world of its own, as a game moved from the suite's server would hold
/// it, and reads what the post-import report says about it. The suites are other people's code, so they are not kept
/// in this repository: CI checks each out at a pinned commit, and a local run finds one through its variable or a
/// checkout beside this repository, or skips.
/// </summary>
internal static partial class SoftcodeSuiteInstaller
{
	/// <summary>
	/// The file or folder <paramref name="variable"/> names, else <paramref name="beside"/> next to this repository.
	/// Missing on CI is a failure; missing on a developer's machine skips the test.
	/// </summary>
	public static string Locate(string variable, string beside)
	{
		var path = Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured
			? configured
			: Path.Join(Path.GetDirectoryName(TestPaths.RepositoryRoot)!, beside);
		if (File.Exists(path) || Directory.Exists(path)) return path;
		if (Environment.GetEnvironmentVariable("CI") is { Length: > 0 })
			throw new FileNotFoundException($"{variable} names nothing at {path}; the workflow checks the suite out.");
		Skip.Test($"No checkout of the suite: set {variable} or put it at {path}.");
		return path;
	}

	/// <summary>
	/// The commands of an installer file, as a client's upload would send them: a line that starts with white space
	/// continues the one before it, and blank lines, <c>/* */</c> blocks and <c>//</c> and <c>@@</c> lines are
	/// comments. A plain one-command-per-line file comes through unchanged.
	/// </summary>
	public static IEnumerable<string> Commands(IEnumerable<string> lines)
	{
		var inComment = false;
		StringBuilder? current = null;
		foreach (var line in lines)
		{
			if (inComment)
			{
				inComment = !line.Contains("*/", StringComparison.Ordinal);
				continue;
			}

			if (line.StartsWith("/*", StringComparison.Ordinal))
			{
				if (current is not null) yield return current.ToString();
				current = null;
				inComment = !line.Contains("*/", StringComparison.Ordinal);
				continue;
			}

			if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal)
					|| line.StartsWith("@@", StringComparison.Ordinal))
				continue;

			if (char.IsWhiteSpace(line[0]) && current is not null)
			{
				current.Append(line.TrimStart());
				continue;
			}

			if (current is not null) yield return current.ToString();
			current = new StringBuilder(line);
		}

		if (current is not null) yield return current.ToString();
	}

	/// <summary>
	/// Turns on what the import of <paramref name="options"/>'s server turns on.
	/// </summary>
	public static async Task ConfigureAsync(ServerWebAppFactory factory, IReadOnlyDictionary<string, object> options)
	{
		foreach (var (key, value) in options)
		{
			var text = value switch
			{
				bool flag => flag ? "yes" : "no",
				string written => written,
				_ => null
			};
			if (text is not null)
				await RunAsync(factory, $"@config/set {key}={text}");
		}
	}

	/// <summary>Lets every thing hear <c>$</c>-commands, as an imported thing that never had NO_COMMAND does.</summary>
	public static async Task HearCommandsAsync(ServerWebAppFactory factory)
		=> await RunAsync(factory, "@dolist [lsearch(all,type,thing)]=@set ##=!no_command");

	/// <summary>Runs <paramref name="command"/> as God, waits for what it queued, and returns what God was told.</summary>
	public static async Task<IReadOnlyList<string>> RunAsync(ServerWebAppFactory factory, string command)
	{
		var before = factory.Notifications.DeliveryCountFor(factory.ExecutorDBRef);
		await factory.Services.GetRequiredService<IMUSHCodeParser>().CommandParse(1,
			factory.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command));
		await factory.QueueBarrierAsync();
		return factory.Notifications.DeliveriesFor(factory.ExecutorDBRef).Skip(before).Select(delivery => delivery.Message).ToList();
	}

	/// <summary>
	/// Runs each command of <paramref name="files"/> as God, after the rewrites the server's import makes to stored
	/// softcode (<paramref name="rewrite"/>), so the world holds what an imported game would.
	/// </summary>
	public static async Task InstallAsync(ServerWebAppFactory factory, IEnumerable<string> files, Func<string, string> rewrite)
	{
		foreach (var file in files)
			foreach (var command in Commands(await File.ReadAllLinesAsync(file)))
				await RunAsync(factory, rewrite(command));
	}

	/// <summary>
	/// The renames an import makes for the attribute names <paramref name="files"/> set with <c>&amp;</c> that SharpMUSH
	/// refuses, which rewrite both the setting command and the code that names them.
	/// </summary>
	public static async Task<AttributeNameRewriter> AttributeNamesAsync(IEnumerable<string> files)
	{
		var names = new List<string>();
		foreach (var file in files)
			names.AddRange(Commands(await File.ReadAllLinesAsync(file))
				.Select(command => SetAttribute().Match(command))
				.Where(match => match.Success)
				.Select(match => match.Groups["name"].Value));
		return AttributeNameRewriter.For(names);
	}

	[GeneratedRegex(@"^&(?<name>\S+)\s")]
	private static partial Regex SetAttribute();

	/// <summary>What a RhostMUSH import does to a stored value: colour substitutions, then the Rhost rewrites.</summary>
	public static string RhostRewrite(string value)
		=> RhostSoftcodeRewriter.Rewrite(ColourSubstitutionRewriter.Rewrite(value,
			new ColourRewriteOptions(ColourSubstitutionDialect.RhostMush, ColourRewriteMode.Auto)).Text).Text;

	/// <summary>What a TinyMUX import does to a stored value: colour substitutions.</summary>
	public static string MuxRewrite(string value)
		=> ColourSubstitutionRewriter.Rewrite(value,
			new ColourRewriteOptions(ColourSubstitutionDialect.TinyMux, ColourRewriteMode.Auto)).Text;

	/// <summary>The post-import report on the world, one key per warning (<see cref="Key"/>).</summary>
	public static async Task<IReadOnlyList<string>> ReportAsync(ServerWebAppFactory factory, string dialect)
	{
		var parser = factory.Services.GetRequiredService<IMUSHCodeParser>();
		var result = new ConversionResult();
		await ImportedCodeScan.ReportAsync(factory.Services.GetRequiredService<IMediator>(), parser.FunctionLibrary,
			parser.CommandLibrary,
			new SoftcodeSyntaxAnalyzer(parser.FunctionLibrary, factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()),
			dialect, '+', result, CancellationToken.None);
		return [.. result.Warnings.Where(warning => warning.Kind == ImportWarningKind.Softcode).Select(warning => Key(warning.Text))];
	}

	/// <summary>
	/// What one warning is about, without its counts and places: <c>pedit()</c>, <c>the command n</c>,
	/// <c>@switch/f</c>, <c>elist() with 6 arguments</c>, <c>the attribute CAPTION</c>.
	/// </summary>
	public static string Key(string warning)
		=> WarningSubject().Match(warning) is { Success: true } match
			? match.Groups["subject"].Value + match.Groups["count"].Value
			: warning.Contains("registers @functions under names it computes", StringComparison.Ordinal)
				? "computed @function names"
				: warning;

	[GeneratedRegex(@"^Softcode: (?<subject>.+?) is (?:called|run)(?<count> with \d+ arguments?)?")]
	private static partial Regex WarningSubject();
}
