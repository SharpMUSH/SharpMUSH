using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Integration.Auth;

/// <summary>
/// Cross-surface consistency fix: <see cref="SitelockController.AddSitelockRule"/> and
/// <see cref="SitelockController.DeleteSitelockRule"/> must base their read-modify-write on
/// persisted state (<c>database.GetExpandedServerData&lt;SharpMUSHOptions&gt;</c>), exactly like the
/// in-game <c>@sitelock</c> command's <c>WizardCommands.CurrentPersistedOptionsAsync</c> does — not
/// on the shared <see cref="IOptionsWrapper{SharpMUSHOptions}"/> substitute's <c>CurrentValue</c>,
/// which this fixture (see <c>SitelockCommandTests</c>' remarks) pins to a fixed snapshot captured
/// once at session start and never updates from DB reloads.
/// <para>
/// Because that substitute is permanently stale here, seeding rules directly via
/// <c>database.SetExpandedServerData</c> (bypassing the substitute entirely) is exactly the scenario
/// the finding describes: a rule that exists in the persisted store but not in the cached
/// <c>CurrentValue</c>. Before the fix, <c>DeleteSitelockRule</c> merging off the stale
/// <c>CurrentValue</c> would not even see the seeded "different rule" key to remove it (producing a
/// 404, and — had it produced a hit — would have persisted a rule set with the seeded rule silently
/// dropped). After the fix, the controller reads the real persisted dictionary, finds and removes
/// only the targeted key, and the unrelated seeded rule survives untouched.
/// </para>
/// </summary>
[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
[NotInParallel("ConfigMutation")]
public class SitelockControllerReadBasisTests(ServerWebAppFactory factory)
{
	private ISharpDatabase Database => factory.Services.GetRequiredService<ISharpDatabase>();

	private SitelockController CreateController()
	{
		var options = factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>();
		var configReloadService = factory.Services.GetRequiredService<ConfigurationReloadService>();
		var banEnforcer = factory.Services.GetRequiredService<IBanEnforcer>();
		var logger = factory.Services.GetRequiredService<ILogger<SitelockController>>();
		return new SitelockController(options, Database, configReloadService, banEnforcer, logger);
	}

	/// <summary>
	/// Merges <paramref name="rules"/> into whatever is currently persisted (falling back to an empty
	/// rule set if nothing has been persisted yet), mirroring the production read-modify-write basis,
	/// so this seed never clobbers rules other tests/commands may have already persisted.
	/// </summary>
	private async Task SeedRulesAsync(params (string Pattern, string[] Flags)[] rules)
	{
		var current = await Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		var baseline = current ?? factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var newRules = new Dictionary<string, string[]>(baseline.SitelockRules.Rules);
		foreach (var (pattern, flags) in rules)
		{
			newRules[pattern] = flags;
		}

		var updated = baseline with { SitelockRules = new SitelockRulesOptions(newRules) };
		await Database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);
	}

	private async Task<Dictionary<string, string[]>> GetPersistedRulesAsync()
	{
		var current = await Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		await Assert.That(current).IsNotNull();
		return current!.SitelockRules.Rules;
	}

	[Test]
	public async Task DeleteSitelockRule_ReadsPersistedState_UnrelatedSeededRuleSurvives()
	{
		var survivingPattern = $"*.{TestIsolationHelpers.GenerateUniqueName("survivor")}.test";
		var targetPattern = $"*.{TestIsolationHelpers.GenerateUniqueName("doomed")}.test";

		// Seeded directly against the DB, bypassing the controller entirely — this is the "externally
		// seeded rule" the finding is about: present in the persisted store, absent from the stale
		// IOptionsWrapper.CurrentValue substitute.
		await SeedRulesAsync(
			(survivingPattern, ["!connect"]),
			(targetPattern, ["!connect"]));

		var controller = CreateController();
		var result = await controller.DeleteSitelockRule(targetPattern);

		await Assert.That(result).IsTypeOf<OkResult>();

		var persistedRules = await GetPersistedRulesAsync();
		await Assert.That(persistedRules.ContainsKey(targetPattern)).IsFalse();
		await Assert.That(persistedRules.ContainsKey(survivingPattern)).IsTrue();
		await Assert.That(persistedRules[survivingPattern]).IsEquivalentTo(["!connect"]);
	}

	[Test]
	public async Task AddSitelockRule_PersistsAndRoundTripsViaGetExpandedServerData()
	{
		var pattern = $"*.{TestIsolationHelpers.GenerateUniqueName("added")}.test";

		var controller = CreateController();
		var result = await controller.AddSitelockRule(pattern, ["!connect", "register"]);

		await Assert.That(result).IsTypeOf<OkResult>();

		var persistedRules = await GetPersistedRulesAsync();
		await Assert.That(persistedRules.ContainsKey(pattern)).IsTrue();
		await Assert.That(persistedRules[pattern]).IsEquivalentTo(["!connect", "register"]);
	}
}
