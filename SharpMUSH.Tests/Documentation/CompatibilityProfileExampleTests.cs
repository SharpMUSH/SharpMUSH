using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Runs the examples in the PennMUSH compatibility profile (<c>pennmush-compatibility.md</c>), which
/// promises that every example is a line you can type. Each <c>```sharp</c> block is run top to bottom
/// as God: a <c>&gt; think …</c> line is compared with the lines that follow it up to the next
/// <c>&gt;</c> line, and any other <c>&gt;</c> line is run for its effect.
///
/// <para>Two conventions keep this honest without cluttering what a reader sees, since the renderer
/// and GitHub both take a fence's language from its first word and ignore the rest:</para>
/// <list type="bullet">
/// <item>Words after <c>sharp</c> on the fence line name compatibility options the block needs:
/// <c>```sharp paren_groups</c> turns <c>paren_groups</c> on, <c>paren_groups=off</c> turns it off.
/// The surrounding prose has to say so, because the reader does not see the fence line.</item>
/// <item><c>```sharp unchecked</c> marks a block whose output is illustrative — a clock reading, a
/// description of output rather than the output. An expected line ending <c>...</c> matches any
/// output that starts with the text before it.</item>
/// </list>
/// </summary>
public class CompatibilityProfileExampleTests
{
	private const string Profile = "pennmush-compatibility.md";
	private const string Unchecked = "unchecked";
	private const string Elision = "...";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>One <c>&gt;</c> line; <see cref="Expected"/> is null for a line run only for its effect.</summary>
	public sealed record ExampleStep(int Line, string Command, string? Expected);

	/// <summary>One <c>```sharp</c> block, named by the line its fence is on.</summary>
	public sealed record ExampleBlock(int Line, IReadOnlyList<string> Options, IReadOnlyList<ExampleStep> Steps)
	{
		public override string ToString() => $"{Profile}:{Line}";
	}

	[Test]
	[MethodDataSource(nameof(Examples))]
	public async ValueTask ExampleOutputMatches(ExampleBlock block)
	{
		using var configuration = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = WithOptions(options.Compatibility, block.Options)
		});

		foreach (var step in block.Steps)
		{
			var output = (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(step.Command)))
				?.Message?.ToPlainText() ?? string.Empty;

			switch (step.Expected)
			{
				case null:
					break;
				case var prefix when prefix.EndsWith(Elision, StringComparison.Ordinal):
					await Assert.That(output).StartsWith(prefix[..^Elision.Length])
						.Because($"{Profile}:{step.Line} `{step.Command}`");
					break;
				case var expected:
					await Assert.That(output).IsEqualTo(expected)
						.Because($"{Profile}:{step.Line} `{step.Command}`");
					break;
			}
		}
	}

	/// <summary>A parser regression here would otherwise pass vacuously, with no blocks to run.</summary>
	[Test]
	public async ValueTask ProfileHasCheckedExamples()
	{
		var blocks = ReadBlocks().ToList();

		await Assert.That(blocks.Count).IsGreaterThan(10);
		await Assert.That(blocks.Any(block => block.Options.Contains("paren_groups"))).IsTrue();
	}

	public static IEnumerable<Func<ExampleBlock>> Examples() => ReadBlocks().Select(block => (Func<ExampleBlock>)(() => block));

	private static IEnumerable<ExampleBlock> ReadBlocks()
	{
		var lines = File.ReadAllLines(Path.Combine(TestPaths.Helpfiles.FullName, Profile));

		for (var index = 0; index < lines.Length; index++)
		{
			if (!lines[index].StartsWith("```sharp", StringComparison.Ordinal))
				continue;

			var fence = index;
			var arguments = lines[fence]["```sharp".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var body = lines.Skip(fence + 1).TakeWhile(line => !line.StartsWith("```", StringComparison.Ordinal)).ToList();
			index = fence + body.Count + 1;

			if (arguments.Contains(Unchecked))
				continue;

			yield return new ExampleBlock(fence + 1, arguments, ReadSteps(body, fence + 2).ToList());
		}
	}

	private static IEnumerable<ExampleStep> ReadSteps(List<string> body, int firstLine)
	{
		for (var index = 0; index < body.Count; index++)
		{
			if (!body[index].StartsWith("> ", StringComparison.Ordinal))
				continue;

			var command = body[index][2..];
			var output = body.Skip(index + 1).TakeWhile(line => !line.StartsWith("> ", StringComparison.Ordinal)).ToList();
			var expected = command.StartsWith("think ", StringComparison.OrdinalIgnoreCase)
				? string.Join('\n', output)
				: null;

			yield return new ExampleStep(firstLine + index, command, expected);
		}
	}

	/// <summary>
	/// Applies <c>name</c> (on) or <c>name=off</c> for each option the fence names, found by its
	/// configuration-file name so the fence reads like the configuration it stands for.
	/// </summary>
	private static CompatibilityOptions WithOptions(CompatibilityOptions options, IEnumerable<string> settings)
	{
		var result = options with { };
		foreach (var setting in settings)
		{
			var (name, value) = setting.Split('=', 2) switch
			{
				[var only] => (only, true),
				[var key, var state] => (key, state is not ("off" or "0" or "no")),
				_ => throw new InvalidOperationException(setting)
			};

			var property = typeof(CompatibilityOptions).GetProperties()
				.SingleOrDefault(candidate => candidate.GetCustomAttribute<SharpConfigAttribute>()?.Name == name)
				?? throw new InvalidOperationException($"{Profile}: no compatibility option named '{name}'");

			property.SetValue(result, value);
		}

		return result;
	}
}
