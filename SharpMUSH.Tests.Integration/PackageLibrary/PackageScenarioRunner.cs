using System.Globalization;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.PackageLibrary;

/// <summary>A step of a package behaviour test that did not show what it should.</summary>
public sealed class PackageScenarioException(string message) : Exception(message);

/// <summary>
/// Runs one <see cref="PackageScenario"/> against the integration fixture: installs its packages from the library
/// checkout, creates its players, runs its steps in order and checks each one's output, then takes the packages
/// out again and runs its cleanup, whether the steps passed or not.
/// </summary>
public sealed partial class PackageScenarioRunner(ServerWebAppFactory factory, PackageScenarioFile file)
{
	private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
	private readonly Dictionary<string, TestIsolationHelpers.TestPlayer> _players = new(StringComparer.Ordinal);
	private readonly Dictionary<string, DBRef> _rooms = new(StringComparer.Ordinal);
	private readonly List<string> _installed = [];

	private IConnectionService Connections => factory.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => factory.Services.GetRequiredService<IMediator>();

	[GeneratedRegex(@"\$\{([^}]+)\}")]
	private static partial Regex Placeholder();

	public async Task RunAsync()
	{
		if (file.Path is null) throw new PackageScenarioException(file.Problem ?? "No package library.");

		PackageScenario scenario;
		try
		{
			scenario = PackageScenario.Parse(await File.ReadAllTextAsync(file.Path));
		}
		catch (YamlDotNet.Core.YamlException invalid)
		{
			throw new PackageScenarioException($"{file.Name} does not parse at line {invalid.Start.Line}: {invalid.Message} {invalid.InnerException?.Message}");
		}

		try
		{
			foreach (var package in scenario.Install) await InstallAsync(package, "install");
			foreach (var word in scenario.Unique) _values[word] = TestIsolationHelpers.GenerateUniqueName(word);
			foreach (var (name, player) in scenario.Players) await AddPlayerAsync(name, player ?? new ScenarioPlayer());

			for (var i = 0; i < scenario.Steps.Count; i++)
			{
				var step = scenario.Steps[i];
				for (var n = 1; n <= step.Repeat; n++)
				{
					await RunStepAsync(step, $"step {i + 1}" + (step.Repeat > 1 ? $" (time {n})" : string.Empty), n);
				}
			}
		}
		catch
		{
			// The step's failure is the one to report; a cleanup that also fails is only written out.
			await CleanUpAsync(scenario, report: false);
			throw;
		}

		await CleanUpAsync(scenario, report: true);
	}

	/// <summary>
	/// Takes out every package the test installed, newest first, then runs its cleanup commands. Each goes on when
	/// one before it fails, so a failing test leaves no package behind for the next.
	/// </summary>
	private async Task CleanUpAsync(PackageScenario scenario, bool report)
	{
		var problems = new List<string>();
		foreach (var package in Enumerable.Reverse(_installed))
		{
			try
			{
				if (await factory.Services.UninstallWithBridgesAsync(package) is Error<string> error)
					problems.Add($"{package} did not uninstall: {error.Value}");
			}
			catch (Exception thrown)
			{
				problems.Add($"{package} did not uninstall: {thrown.Message}");
			}
		}

		foreach (var command in scenario.Cleanup)
		{
			try
			{
				await GodAsync(command);
			}
			catch (Exception thrown)
			{
				problems.Add($"{command} failed: {thrown.Message}");
			}
		}

		if (problems.Count == 0) return;
		var message = $"{file.Name} cleanup: {string.Join(" ", problems)}";
		if (report) throw new PackageScenarioException(message);
		Console.WriteLine(message);
	}

	private async Task AddPlayerAsync(string name, ScenarioPlayer player)
	{
		var home = player.Room is { } shared
			? _rooms.TryGetValue(shared, out var room) ? room : _rooms[shared] = await RoomAsync(shared)
			: await RoomAsync(name);
		var created = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(factory.Services, Mediator, Connections, Prefix(name), home);
		_players[name] = created;
		_values[name] = $"#{created.DbRef.Number}";
		_values[$"{name}.name"] = created.Name;
		foreach (var role in player.Roles) await GodAsync($"@role/assign #{created.DbRef.Number}={role}");
	}

	/// <summary>A player name may only hold letters and digits, so the prefix keeps those of the test's own name.</summary>
	private static string Prefix(string name) =>
		new string(name.Where(char.IsAsciiLetterOrDigit).ToArray()) is { Length: > 0 } letters ? letters : "Player";

	private async Task<DBRef> RoomAsync(string name)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{Prefix(name)}Room"), god));
	}

	/// <summary>
	/// Installs <paramref name="package"/> from the library, or, when the library has no such package, the one the
	/// server bundles. A bundled package the game already has is left as it is, and stays when the test ends.
	/// </summary>
	private async Task InstallAsync(string package, string where)
	{
		if (!File.Exists(Path.Join(TestPaths.PackageLibrary, package, "package.yaml")))
		{
			await InstallBundledAsync(package, where);
			return;
		}

		switch (await factory.Services.ApplyFromLibraryAsync(package))
		{
			case PackageApplyResult:
				_installed.Add(package);
				// AINSTALL is queued after the apply.
				await factory.QueueBarrierAsync();
				return;
			case Error<string> error:
				throw new PackageScenarioException($"{file.Name} {where}: {package} did not install: {error.Value}");
		}
	}

	private async Task InstallBundledAsync(string package, string where)
	{
		if (await factory.Services.GetRequiredService<IPackageRegistryService>().GetInstalledPackageAsync(package) is InstalledPackageRecord) return;

		var installed = await factory.Services.GetRequiredService<IBundledPackageBootstrap>()
			.InstallBundledAsync([package], CancellationToken.None);
		if (!installed.Contains(package))
			throw new PackageScenarioException(
				$"{file.Name} {where}: {package} did not install: it is not in the package library, and the server either does not bundle it or skipped it (an attach-mode package whose handler is not configured).");

		_installed.Add(package);
		await factory.QueueBarrierAsync();
	}

	private async Task RunStepAsync(ScenarioStep step, string where, int n)
	{
		var kinds = new[] { step.As, step.God, step.Eval, step.Heard, step.Install, step.Uninstall }.Count(kind => kind is not null);
		if (kinds != 1)
			throw new PackageScenarioException($"{file.Name} {where}: a step has exactly one of as, god, eval, heard, install or uninstall.");
		if (step.Repeat < 1) throw Problem(where, "repeat is 1 or more.");
		var checksOutput = step.As is not null || step.God is not null || step.Eval is not null;
		if (!checksOutput && (step.Sees is not null || step.NotSees is not null || step.Is is not null || step.Capture is not null))
			throw Problem(where, "sees, not_sees, is and capture go with an as, god or eval step.");
		if (step.Heard is null && (step.Lines is not null || step.Count is not null || step.Containing is not null))
			throw Problem(where, "containing, lines and count go with a heard step.");
		if (step.Run is not null && step.As is null) throw Problem(where, "run goes with an as step.");
		if (step.Render is not null && step.Eval is null) throw Problem(where, "render goes with an eval step.");

		if (step.Install is { } install)
		{
			await InstallAsync(install, where);
			return;
		}

		if (step.Uninstall is { } uninstall)
		{
			if (!_installed.Contains(uninstall))
				throw Problem(where, $"{uninstall} was not installed by this test, so it is not this test's to take out.");
			if (await factory.Services.UninstallWithBridgesAsync(uninstall) is Error<string> error)
				throw Problem(where, $"{uninstall} did not uninstall: {error.Value}");
			_installed.Remove(uninstall);
			return;
		}

		if (step.Heard is { } listener)
		{
			await CheckHeardAsync(step, where, listener, n);
			return;
		}

		var (action, output) = step switch
		{
			{ As: { } who } => await AsAsync(who, step.Run ?? throw Problem(where, "an as step needs a run."), where, n),
			{ God: { } command } => await GodStepAsync(command, n),
			{ Eval: { } expression } => await EvalAsync(expression, step.Render, n),
			_ => throw Problem(where, "nothing to run.")
		};
		// Kept with the test's result, so a failing scenario shows what every step before it printed.
		Console.WriteLine($"{where} {action}\n{output}");
		await CheckAsync(step, $"{where} ({action})", output, n);
	}

	private async Task<(string Action, string Output)> AsAsync(string who, string command, string where, int n)
	{
		var player = Player(who, where);
		var typed = await ExpandAsync(command, n);
		var before = factory.Notifications.CountFor(player.DbRef);
		await factory.CommandParser.CommandParse(player.Handle, Connections, MarkupText.Plain(typed));
		await factory.QueueBarrierAsync();
		return ($"{who}: {typed}", string.Join("\n", factory.Notifications.For(player.DbRef).Skip(before)));
	}

	private async Task<(string Action, string Output)> GodStepAsync(string command, int n)
	{
		var typed = await ExpandAsync(command, n);
		return ($"god: {typed}", await GodAsync(typed));
	}

	private async Task<string> GodAsync(string command)
	{
		var result = await factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
		await factory.QueueBarrierAsync();
		return result.Message?.ToPlainText() ?? string.Empty;
	}

	private async Task<(string Action, string Output)> EvalAsync(string expression, string? render, int n)
	{
		var typed = await ExpandAsync(expression, n);
		var value = await factory.FunctionParser.EvaluateAsync(MarkupText.Plain(typed));
		await factory.QueueBarrierAsync();
		return render switch
		{
			null or "plain" => ($"eval: {typed}", value.ToPlainText()),
			"html" => ($"eval as html: {typed}", value.Render(MarkupFormat.Html)),
			_ => throw new PackageScenarioException($"{file.Name}: render is plain or html, not {render}.")
		};
	}

	private async Task CheckHeardAsync(ScenarioStep step, string where, string who, int n)
	{
		var player = Player(who, where);
		var text = await ExpandAsync(step.Containing ?? throw Problem(where, "a heard step needs containing."), n);
		var heard = factory.Notifications.For(player.DbRef).Where(line => line.Contains(text, StringComparison.Ordinal)).ToList();
		var action = $"{where} (what {who} heard containing {text})";
		switch (step)
		{
			case { Lines: { } lines, Count: null }:
				var expected = new List<string>();
				foreach (var line in lines) expected.Add(await ExpandAsync(line, n));
				if (!heard.SequenceEqual(expected, StringComparer.Ordinal))
					throw Failure(step, action, $"the lines to be:\n{string.Join("\n", expected)}", string.Join("\n", heard));
				break;
			case { Count: { } count, Lines: null }:
				if (heard.Count != count)
					throw Failure(step, action, $"{count} such lines", string.Join("\n", heard));
				break;
			default:
				throw Problem(where, "a heard step needs lines or count.");
		}
	}

	private async Task CheckAsync(ScenarioStep step, string where, string output, int n)
	{
		foreach (var text in Texts(step.Sees))
		{
			var seen = await ExpandAsync(text, n);
			if (!output.Contains(seen, StringComparison.Ordinal)) throw Failure(step, where, $"to see: {seen}", output);
		}

		foreach (var text in Texts(step.NotSees))
		{
			var unseen = await ExpandAsync(text, n);
			if (output.Contains(unseen, StringComparison.Ordinal)) throw Failure(step, where, $"not to see: {unseen}", output);
		}

		if (step.Is is { } exact && await ExpandAsync(exact, n) is var expected && output.Trim() != expected)
			throw Failure(step, where, $"the output to be: {expected}", output);

		if (step.Capture is { } name)
		{
			if (_players.ContainsKey(name) || name == "n" || name.Contains(':') || name.Contains('.'))
				throw Problem(where, $"{name} is already a player's name or a reserved one; capture under another name.");
			_values[name] = output.Trim();
		}
	}

	private static IEnumerable<string> Texts(object? value) => value switch
	{
		null => [],
		string text => [text],
		IEnumerable<object> list => list.Select(item => item?.ToString() ?? string.Empty),
		_ => [value.ToString() ?? string.Empty]
	};

	private TestIsolationHelpers.TestPlayer Player(string who, string where) =>
		_players.TryGetValue(who, out var player) ? player : throw Problem(where, $"there is no player called {who}.");

	/// <summary>
	/// Replaces each <c>${name}</c>: a player's dbref (<c>${ann}</c>) or name (<c>${ann.name}</c>), a unique word, a
	/// captured output, a package object's dbref (<c>${object:package/ref}</c>), or the repeat count (<c>${n}</c>,
	/// <c>${n:00}</c>).
	/// </summary>
	private async Task<string> ExpandAsync(string text, int n)
	{
		// Looked up each time: a package installed again has new objects.
		var objects = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var reference in Placeholder().Matches(text).Select(match => match.Groups[1].Value)
			.Where(name => name.StartsWith("object:", StringComparison.Ordinal)).Distinct())
		{
			objects[reference] = await PackageObjectAsync(reference["object:".Length..]);
		}

		return Placeholder().Replace(text, match => match.Groups[1].Value switch
		{
			"n" => n.ToString(CultureInfo.InvariantCulture),
			var counted when counted.StartsWith("n:", StringComparison.Ordinal) => n.ToString(counted[2..], CultureInfo.InvariantCulture),
			var name when objects.TryGetValue(name, out var value) => value,
			var name when _values.TryGetValue(name, out var value) => value,
			var name => throw new PackageScenarioException($"{file.Name}: nothing is called ${{{name}}}.")
		});
	}

	/// <summary>The dbref of the object <c>ref</c> of a package, named <c>package/ref</c>, or <c>package</c> when the ref is the package's id.</summary>
	private async Task<string> PackageObjectAsync(string reference)
	{
		var (package, name) = reference.Split('/', 2) is [var p, var r] ? (p, r) : (reference, reference);
		var objects = await factory.Services.GetRequiredService<IPackageRegistryService>().GetPackageObjectsAsync(package);
		return objects.FirstOrDefault(o => o.Ref == name) is { } found
			? $"#{DBRef.Parse(found.Objid).Number}"
			: throw new PackageScenarioException($"{file.Name}: {package} has no object {name}.");
	}

	private PackageScenarioException Problem(string where, string message) => new($"{file.Name} {where}: {message}");

	private PackageScenarioException Failure(ScenarioStep step, string where, string expectation, string output) =>
		new($"{file.Name} {where}: expected {expectation}{(step.Because is { } why ? $"\nBecause: {why}" : string.Empty)}\nOutput:\n{output}");
}
