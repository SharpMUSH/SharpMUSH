using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@config/set</c>, <c>@config/save</c>, <c>@enable</c> and <c>@disable</c> — PennMUSH's
/// <c>cmd_config</c> (<c>src/cmds.c:314</c>), <c>config_set</c> (<c>src/conf.c:856</c>) and
/// <c>do_enable</c> (<c>src/conf.c:1780</c>). SharpMUSH's effective configuration is the one persisted
/// options document the portal also edits, so a change is observed the way
/// <see cref="SitelockCommandTests"/> observes one: through <see cref="IOptionsMonitor{TOptions}"/>,
/// which reloads it, and through the stored document a restart reads back.
/// </summary>
/// <remarks>
/// <see cref="NotInParallelAttribute"/>: every test here rewrites the whole options document, as
/// <see cref="SitelockCommandTests"/> does, and each restores what it found.
/// </remarks>
[NotInParallel]
public class ConfigSetCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private ConfigurationReloadService ConfigReloadService => WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
	private IOptionsMonitor<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
	private DBRef God => WebAppFactoryArg.ExecutorDBRef;

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	private Task<List<string>> AsGod(string command)
		=> MessagesWhile(God, async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)));

	private Task<List<string>> As(TestIsolationHelpers.TestPlayer player, string command)
		=> MessagesWhile(player.DbRef, async () => await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command)));

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<TestIsolationHelpers.TestPlayer> Wizard()
	{
		var wizard = await Player("CfgWiz");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		return wizard;
	}

	private async Task<string> StoredJson()
		=> System.Text.Json.JsonSerializer.Serialize(await Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)));

	/// <summary>What a restart builds: the options factory reading the stored document afresh.</summary>
	private SharpMUSHOptions AfterRestart()
		=> new OptionsService(
				WebAppFactoryArg.Services.GetRequiredService<IExpandedDataStore>(),
				WebAppFactoryArg.Services.GetServices<IValidateOptions<SharpMUSHOptions>>())
			.Create(Options.DefaultName);

	/// <summary>Runs <paramref name="test"/>, then puts the stored configuration back as it was.</summary>
	private async Task Restoring(Func<Task> test)
	{
		var original = await Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		try
		{
			await test();
		}
		finally
		{
			await Database.SetExpandedServerData(nameof(SharpMUSHOptions), original!);
			ConfigReloadService.SignalChange();
		}
	}

	[Test]
	public async ValueTask Set_ChangesTheLiveValueAndTheStoredOne()
		=> await Restoring(async () =>
		{
			var url = $"https://{Guid.NewGuid():N}.test/";

			var messages = await AsGod($"@config/set mud_url={url}");

			await Assert.That(messages).Contains("Option set.");
			await Assert.That(Configuration.CurrentValue.Net.MudUrl).IsEqualTo(url);
			await Assert.That(AfterRestart().Net.MudUrl).IsEqualTo(url).Because("the stored document is what a restart reads");
		});

	/// <summary><c>cf_bool</c> takes yes/no, true/false and 1/0, caselessly.</summary>
	[Test]
	[Arguments("no", false)]
	[Arguments("YES", true)]
	[Arguments("0", false)]
	[Arguments("true", true)]
	public async ValueTask Set_ParsesBooleansAsCfBoolDoes(string text, bool expected)
		=> await Restoring(async () =>
		{
			await AsGod($"@config/set noisy_whisper={!expected}");
			var messages = await AsGod($"@config/set noisy_whisper={text}");

			await Assert.That(messages).Contains("Option set.");
			await Assert.That(Configuration.CurrentValue.Command.NoisyWhisper).IsEqualTo(expected);
		});

	/// <summary>A value that does not parse for the option's type is refused, and nothing changes.</summary>
	[Test]
	[Arguments("noisy_whisper", "maybe")]
	[Arguments("max_channels", "lots")]
	[Arguments("max_channels", "-3")]
	public async ValueTask Set_RefusesAValueOfTheWrongType(string option, string value)
		=> await Restoring(async () =>
		{
			var before = await StoredJson();

			var messages = await AsGod($"@config/set {option}={value}");

			await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.ConfigInvalidValueFormat, option, value));
			await Assert.That(await StoredJson()).IsEqualTo(before);
		});

	/// <summary>
	/// <c>config_set</c> skips, for a command, every option in the <c>files</c> and <c>messages</c>
	/// groups and every CP_GODONLY one — "@config/set output_data=../../.bashrc? Ouch."
	/// </summary>
	[Test]
	[Arguments("names_file")]
	[Arguments("connect_file")]
	[Arguments("sql_password")]
	public async ValueTask Set_RefusesOptionsPennKeepsOutOfReach(string option)
		=> await Restoring(async () =>
		{
			var before = await StoredJson();

			var messages = await AsGod($"@config/set {option}=../../x");

			await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.ConfigOptionNotSettableFormat, option));
			await Assert.That(await StoredJson()).IsEqualTo(before);
		});

	[Test]
	public async ValueTask Set_UnknownOptionCannotBeSet()
	{
		var messages = await AsGod("@config/set no_such_option_zz=1");

		await Assert.That(messages).Contains("Couldn't set that option.");
	}

	[Test]
	public async ValueTask Set_WithNoOptionAsksWhatToSet()
	{
		var messages = await AsGod("@config/set");

		await Assert.That(messages).Contains("What did you want to set?");
	}

	/// <summary><c>if (!Wizard(executor)) "You can't remake the world in your image."</c></summary>
	[Test]
	public async ValueTask Set_MortalIsRefusedAndNothingChanges()
		=> await Restoring(async () =>
		{
			var mortal = await Player("CfgMortal");
			var url = $"https://{Guid.NewGuid():N}.test/";

			var messages = await As(mortal, $"@config/set mud_url={url}");

			await Assert.That(messages).Contains("You can't remake the world in your image.");
			await Assert.That(Configuration.CurrentValue.Net.MudUrl).IsNotEqualTo(url);
		});

	[Test]
	public async ValueTask Set_WizardMaySet()
		=> await Restoring(async () =>
		{
			var wizard = await Wizard();
			var url = $"https://{Guid.NewGuid():N}.test/";

			var messages = await As(wizard, $"@config/set mud_url={url}");

			await Assert.That(messages).Contains("Option set.");
			await Assert.That(Configuration.CurrentValue.Net.MudUrl).IsEqualTo(url);
		});

	/// <summary>Only God alters the saved configuration: <c>/save</c> refuses a mere wizard.</summary>
	[Test]
	public async ValueTask Save_WizardIsRefused()
		=> await Restoring(async () =>
		{
			var wizard = await Wizard();
			var url = $"https://{Guid.NewGuid():N}.test/";

			var messages = await As(wizard, $"@config/save mud_url={url}");

			await Assert.That(messages).Contains("You can't remake the world in your image.");
			await Assert.That(Configuration.CurrentValue.Net.MudUrl).IsNotEqualTo(url);
		});

	[Test]
	public async ValueTask Save_GodSetsAndSaves()
		=> await Restoring(async () =>
		{
			var url = $"https://{Guid.NewGuid():N}.test/";

			var messages = await AsGod($"@config/save mud_url={url}");

			await Assert.That(messages).Contains("Option set and saved.");
			await Assert.That(AfterRestart().Net.MudUrl).IsEqualTo(url);
		});

	/// <summary><c>do_enable</c> sets a boolean option through the same path as <c>@config/set</c>.</summary>
	[Test]
	public async ValueTask EnableAndDisable_ToggleABooleanOption()
		=> await Restoring(async () =>
		{
			var disabled = await AsGod("@disable noisy_whisper");
			await Assert.That(disabled).Contains("Disabled.");
			await Assert.That(Configuration.CurrentValue.Command.NoisyWhisper).IsFalse();
			await Assert.That(AfterRestart().Command.NoisyWhisper).IsFalse();

			var enabled = await AsGod("@enable noisy_whisper");
			await Assert.That(enabled).Contains("Enabled.");
			await Assert.That(Configuration.CurrentValue.Command.NoisyWhisper).IsTrue();
		});

	/// <summary><c>do_enable</c>: a CP_GODONLY option "cannot be altered".</summary>
	[Test]
	public async ValueTask Enable_RefusesAnOptionPennKeepsOutOfReach()
	{
		var messages = await AsGod("@enable sql_password");

		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.ConfigOptionNotSettableFormat, "sql_password"));
	}

	[Test]
	public async ValueTask Disable_MortalIsRefused()
		=> await Restoring(async () =>
		{
			await AsGod("@enable noisy_whisper");
			var mortal = await Player("CfgDisable");

			await As(mortal, "@disable noisy_whisper");

			await Assert.That(Configuration.CurrentValue.Command.NoisyWhisper).IsTrue();
		});

	/// <summary>
	/// <c>can_view_config_option</c>: a CP_GODONLY option is hidden from everyone but God — its value
	/// is the SQL password.
	/// </summary>
	[Test]
	public async ValueTask GodOnlyOptions_AreHiddenFromEveryoneButGod()
	{
		var wizard = await Wizard();

		var direct = await As(wizard, "@config sql_password");
		var category = await As(wizard, "@config net");

		await Assert.That(direct.Any(m => m.Contains("sql_password", StringComparison.OrdinalIgnoreCase) && m.Contains(':'))).IsFalse();
		await Assert.That(category.Any(m => m.Contains("sql_password", StringComparison.OrdinalIgnoreCase))).IsFalse();
		await Assert.That(category.Any(m => m.Contains("mud_name", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}
}
