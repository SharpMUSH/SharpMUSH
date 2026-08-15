using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class FlagAndPowerCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async ValueTask Flag_List_DisplaysAllFlags()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagListCmd");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));
		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@flag/list"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Flags:")), TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Flag_Add_CreatesNewFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.Name).IsEqualTo(flagName);
		await Assert.That(createdFlag.Symbol).IsEqualTo(symbol);
		await Assert.That(createdFlag.System).IsFalse();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagCreatedWithSymbolFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsSystemFlagCreation()
	{
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		var createdFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(createdFlag).IsNotNull();
		await Assert.That(createdFlag!.System).IsFalse();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Add_PreventsDuplicateFlags()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagAlreadyExistsFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Delete_RemovesNonSystemFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		var createdFlag = await Mediator.Send(new CreateObjectFlagCommand(
			flagName, null, symbol, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER", "THING", "ROOM", "EXIT"]
		));
		await Assert.That(createdFlag).IsNotNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		var deletedFlag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(deletedFlag).IsNull();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagDeletedFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Delete_PreventsSystemFlagDeletion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/delete WIZARD"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Delete_HandlesNonExistentFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = "NONEXISTENT_FLAG_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/delete {flagName}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_List_DisplaysAllPowers()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list"));

		// The notify substitute is a session-wide singleton, so assert the call happened, not how many times.
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Object Powers:")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Power_Add_CreatesNewPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.Name).IsEqualTo(powerName);
		await Assert.That(createdPower.Alias).IsEqualTo(alias);
		await Assert.That(createdPower.System).IsFalse();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerCreatedWithAliasFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Add_PreventsSystemPowerCreation()
	{
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		var createdPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(createdPower).IsNotNull();
		await Assert.That(createdPower!.System).IsFalse();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Delete_RemovesNonSystemPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		var createdPower = await Mediator.Send(new CreatePowerCommand(
			powerName, alias, string.Empty, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], ["PLAYER"]
		));
		await Assert.That(createdPower).IsNotNull();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		var deletedPower = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(deletedPower).IsNull();

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerDeletedFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Delete_PreventsSystemPowerDeletion()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var builderPower = await Mediator.Send(new GetPowerQuery("BUILDER"));
		if (builderPower != null && builderPower.System)
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single("@power/delete BUILDER"));

			await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemPowerFormat), executor, executor)).IsTrue();
		}
	}

	[Test]
	public async ValueTask Power_Delete_HandlesNonExistentPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = "NONEXISTENT_POWER_XYZ123";
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/delete {powerName}"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Add_RequiresBothArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/add TESTFLAG"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.FlagAddRequiresNameAndSymbol), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Add_RequiresBothArguments()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/add TESTPOWER"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.PowerAddRequiresNameAndAlias), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Flag_Disable_DisablesNonSystemFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_DISABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/disable {flagName}"));

		var flag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Disabled).IsTrue();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Flag '{flagName}' disabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Enable_EnablesDisabledFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = $"TEST_FLAG_ENABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var symbol = "T";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/add {flagName}={symbol}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/disable {flagName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/enable {flagName}"));

		var flag = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(flag).IsNotNull();
		await Assert.That(flag!.Disabled).IsFalse();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Flag '{flagName}' enabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	[Test]
	public async ValueTask Flag_Disable_PreventsSystemFlagDisable()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		// Use WIZARD (a system flag stored in the ObjectFlags table).
		// PLAYER is a type flag added implicitly per-object and is NOT in the ObjectFlags table,
		// so it cannot be looked up or disabled via @flag/disable.
		await Parser.CommandParse(1, ConnectionService, MModule.single("@flag/disable WIZARD"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, executor)).IsTrue();
	}

	[Test]
	public async ValueTask Power_Disable_DisablesNonSystemPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_DISABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/disable {powerName}"));

		var power = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(power).IsNotNull();
		await Assert.That(power!.Disabled).IsTrue();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Power '{powerName}' disabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Enable_EnablesDisabledPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = $"TEST_POWER_ENABLE_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var alias = "TPOW";

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/add {powerName}={alias}"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/disable {powerName}"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/enable {powerName}"));

		var power = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(power).IsNotNull();
		await Assert.That(power!.Disabled).IsFalse();

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"Power '{powerName}' enabled.")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	[Test]
	public async ValueTask Power_Disable_PreventsSystemPowerDisable()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/disable Builder"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService, nameof(ErrorMessages.Notifications.CannotDisableSystemPowerFormat), executor, executor)).IsTrue();
	}

	[Test]
	[NotInParallel]
	public async ValueTask God_CanSetTrustFlag()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single("@create GodTrustFlagTestObj"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {newDb}=TRUST"));

		var newObject = await Mediator.Send(new GetObjectNodeQuery(newDb));
		await Assert.That(newObject.Object()).IsNotNull();
		var flags = await newObject.Object()!.Flags.Value.ToArrayAsync();

		await Assert.That(flags.Any(f => f.Name.Equals("TRUST", StringComparison.OrdinalIgnoreCase))).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	private async ValueTask<string[]> PowerNamesOf(DBRef dbref)
	{
		var node = await Mediator.Send(new GetObjectNodeQuery(dbref));
		var powers = await node.Object()!.Powers.Value.ToArrayAsync();
		return powers.Select(p => p.Name).ToArray();
	}

	// PennMUSH src/wiz.c do_power: with no switch, @power <object>=<power> grants the power.
	[Test]
	public async ValueTask Power_Grant_SetsPowerOnObject()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerGrant")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));

		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/flags.c set_power: granting emits "<name> - <power> granted."
	[Test]
	public async ValueTask Power_Grant_NotifiesGranted()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("PowerGrantMsg");
		var createResult = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));

		// ManipulateSharpObjectService reports flag and power changes with no explicit sender.
		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s, $"{name} - Builder granted.")),
				null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power: a leading ! on the power name revokes it.
	[Test]
	public async ValueTask Power_Revoke_ClearsPowerOnObject()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerRevoke")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));
		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=!Builder"));
		await Assert.That(await PowerNamesOf(newDb)).DoesNotContain("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power splits the right-hand side on spaces and applies each token.
	[Test]
	public async ValueTask Power_Grant_AppliesEverySpaceSeparatedToken()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerMulti")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder Boot"));
		var granted = await PowerNamesOf(newDb);
		await Assert.That(granted).Contains("Builder");
		await Assert.That(granted).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=!Builder Boot"));
		var after = await PowerNamesOf(newDb);
		await Assert.That(after).DoesNotContain("Builder");
		await Assert.That(after).Contains("Boot");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/flags.c set_power reports the unrecognised power name, not the object's name.
	[Test]
	public async ValueTask Power_Grant_UnknownPowerNamesThePower()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerUnknown")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=NOSUCHPOWERXYZ"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextEquals(s, "NOSUCHPOWERXYZ - I don't recognize that power.")),
				null, INotifyService.NotificationType.Announce);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// PennMUSH src/wiz.c do_power: "Only wizards may grant powers."
	[Test]
	public async ValueTask Power_Grant_RequiresWizard()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PowerNonWiz");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@power {testPlayer.DbRef}=Builder"));

		await Assert.That(await PowerNamesOf(testPlayer.DbRef)).DoesNotContain("Builder");
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextEquals(s,
					ErrorMessages.Notifications.OnlyWizardsMayGrantPowers)),
				null, INotifyService.NotificationType.Announce);
	}

	// PennMUSH src/wiz.c do_power: with no "=", @power describes the named power; it does not list an object's powers.
	[Test]
	public async ValueTask Power_NoEquals_ShowsPowerInformation()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power Builder"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "     Name: Builder")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// PennMUSH src/flags.c do_flag_info: an unknown name reports "No such power."
	[Test]
	public async ValueTask Power_NoEquals_UnknownPowerReportsNoSuchPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power NOSUCHPOWERABC"));

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NoSuchPowerInfo), executor, executor)).IsTrue();
	}

	// PennMUSH src/flags.c: every power definition switch is God-only; a wizard is not enough.
	[Test]
	public async ValueTask Power_Add_RequiresGod()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PowerAddNonGod");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var powerName = $"TEST_POWER_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@power/add {powerName}=TPOW"));

		await Assert.That(await Mediator.Send(new GetPowerQuery(powerName))).IsNull();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NotEnoughMagic), testPlayer.DbRef, testPlayer.DbRef)).IsTrue();
	}

	// PennMUSH src/fundb.c fun_powers routes powers() through do_power, so it honours the same "!" revoke prefix.
	[Test]
	public async ValueTask PowersFunction_SideEffect_HonoursRevokePrefix()
	{
		var createResult = await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@create {TestIsolationHelpers.GenerateUniqueName("PowerFnRevoke")}"));
		var newDb = DBRef.Parse(createResult.Message!.ToPlainText()!);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {newDb}=Builder"));
		await Assert.That(await PowerNamesOf(newDb)).Contains("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"think [powers({newDb},!Builder)]"));
		await Assert.That(await PowerNamesOf(newDb)).DoesNotContain("Builder");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@destroy {newDb}"));
	}

	// Every built-in power carries letter '\0' (PennMUSH hdrs/flag_tab.h power_table) and SharpMUSH
	// refuses to redefine a system power, so each /letter test needs a power of its own. Each also
	// picks a letter no other test uses: the collision check is global and these run in parallel.
	private async ValueTask<string> CreateLetterlessPower(string[]? types = null)
	{
		var powerName = $"TEST_POWER_LTR_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var created = await Mediator.Send(new CreatePowerCommand(
			powerName, string.Empty, string.Empty, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], types ?? ["PLAYER"]));
		await Assert.That(created).IsNotNull();
		await Assert.That(created!.Symbol).IsEqualTo(string.Empty);
		return powerName;
	}

	// PennMUSH src/flags.c:2790 do_flag_letter: "Letter for power <name> set to '<c>'."
	[Test]
	public async ValueTask Power_Letter_SetsTheLetter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = await CreateLetterlessPower();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}=Q"));

		var updated = await Mediator.Send(new GetPowerQuery(powerName));
		await Assert.That(updated).IsNotNull();
		await Assert.That(updated!.Symbol).IsEqualTo("Q");
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.PowerLetterSetFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// PennMUSH src/flags.c:2793 do_flag_letter: an empty or absent letter clears it.
	[Test]
	public async ValueTask Power_Letter_ClearsTheLetterWhenNoneGiven()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = await CreateLetterlessPower();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}=V"));
		await Assert.That((await Mediator.Send(new GetPowerQuery(powerName)))!.Symbol).IsEqualTo("V");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}"));

		await Assert.That((await Mediator.Send(new GetPowerQuery(powerName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.PowerLetterClearedFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// PennMUSH src/flags.c:2778 do_flag_letter: "Power characters must be single characters."
	[Test]
	public async ValueTask Power_Letter_RejectsMultipleCharacters()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = await CreateLetterlessPower();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}=ABC"));

		await Assert.That((await Mediator.Send(new GetPowerQuery(powerName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.PowerCharactersMustBeSingleCharacters), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// PennMUSH src/flags.c:2784 do_flag_letter: "Letter conflicts with the <other> power."
	[Test]
	public async ValueTask Power_Letter_RejectsLetterTakenByAnotherPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var holder = await CreateLetterlessPower();
		var claimant = await CreateLetterlessPower();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {holder}=Z"));
		await Assert.That((await Mediator.Send(new GetPowerQuery(holder)))!.Symbol).IsEqualTo("Z");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {claimant}=Z"));

		await Assert.That((await Mediator.Send(new GetPowerQuery(claimant)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.PowerLetterConflictFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(holder));
		await Mediator.Send(new DeletePowerCommand(claimant));
	}

	// PennMUSH src/flags.c:961 letter_to_flagptr only conflicts when the two definitions share an
	// object type; game/txt/hlp/pennv177.hlp:20 records that as a deliberate fix.
	[Test]
	public async ValueTask Power_Letter_AllowsSameLetterOnADisjointType()
	{
		var playerPower = await CreateLetterlessPower(["PLAYER"]);
		var roomPower = await CreateLetterlessPower(["ROOM"]);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {playerPower}=Y"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {roomPower}=Y"));

		await Assert.That((await Mediator.Send(new GetPowerQuery(playerPower)))!.Symbol).IsEqualTo("Y");
		await Assert.That((await Mediator.Send(new GetPowerQuery(roomPower)))!.Symbol).IsEqualTo("Y");

		await Mediator.Send(new DeletePowerCommand(playerPower));
		await Mediator.Send(new DeletePowerCommand(roomPower));
	}

	// PennMUSH src/flags.c:2764 do_flag_letter refuses anyone but God; a wizard is not enough.
	[Test]
	public async ValueTask Power_Letter_RequiresGod()
	{
		var powerName = await CreateLetterlessPower();
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PowerLetterNonGod");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@power/letter {powerName}=J"));

		await Assert.That((await Mediator.Send(new GetPowerQuery(powerName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NotEnoughMagic), testPlayer.DbRef, testPlayer.DbRef)).IsTrue();

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// A divergence from PennMUSH, which has no notion of a system power and lets God letter anything.
	[Test]
	public async ValueTask Power_Letter_RefusesSystemPower()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var builder = await Mediator.Send(new GetPowerQuery("BUILDER"));
		await Assert.That(builder).IsNotNull();
		await Assert.That(builder!.System).IsTrue();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/letter BUILDER=B"));

		await Assert.That((await Mediator.Send(new GetPowerQuery("BUILDER")))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat), executor, executor)).IsTrue();
	}

	// PennMUSH src/flags.c list_all_flags FLAG_LIST_NAMECHAR renders the letter beside the name.
	[Test]
	public async ValueTask Power_List_ShowsTheLetter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = await CreateLetterlessPower();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}=K"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/list {powerName}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextContains(s, powerName)
					&& TestHelpers.MessagePlainTextContains(s, "Symbol")
					&& TestHelpers.MessagePlainTextContains(s, "K")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// PennMUSH src/flags.c do_flag_info prints a "Character:" line between Name and Aliases.
	[Test]
	public async ValueTask Power_NoEquals_ShowsTheCharacter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var powerName = await CreateLetterlessPower();
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power/letter {powerName}=X"));

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@power {powerName}"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextContains(s, "Character: X")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await Mediator.Send(new DeletePowerCommand(powerName));
	}

	// PennMUSH src/flags.c list_all_flags filters the listing by a glob pattern.
	[Test]
	public async ValueTask Power_List_FiltersByPattern()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;

		await Parser.CommandParse(1, ConnectionService, MModule.single("@power/list Buil*"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor),
				Arg.Is<OneOf.OneOf<MString, string>>(s =>
					TestHelpers.MessagePlainTextContains(s, "Builder")
					&& !TestHelpers.MessagePlainTextContains(s, "Announce")),
				TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	// GetObjectFlagQuery is ICacheable, so a read before the write leaves a FusionCache entry that only
	// an ICacheInvalidating command clears. Reading first is the whole point of the test.
	[Test]
	public async ValueTask UpdateObjectFlag_InvalidatesTheCachedDefinition()
	{
		var flagName = await CreateLetterlessFlag();

		var before = await Mediator.Send(new GetObjectFlagQuery(flagName));
		await Assert.That(before!.Symbol).IsEqualTo(string.Empty);

		await Mediator.Send(new UpdateObjectFlagCommand(
			flagName, before.Aliases, "9", before.SetPermissions, before.UnsetPermissions,
			before.TypeRestrictions));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol)
			.IsEqualTo("9")
			.Because("a stale flag-definition entry survives the write when the command does not invalidate");

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	// The seed already spends most of the alphabet, so each /letter test claims a digit no other flag
	// or test uses: the collision check is global and these run in parallel.
	private async ValueTask<string> CreateLetterlessFlag(string[]? types = null)
	{
		var flagName = $"TEST_FLAG_LTR_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		var created = await Mediator.Send(new CreateObjectFlagCommand(
			flagName, null, string.Empty, false,
			["FLAG^WIZARD"], ["FLAG^WIZARD"], types ?? ["PLAYER"]));
		await Assert.That(created).IsNotNull();
		await Assert.That(created!.Symbol).IsEqualTo(string.Empty);
		return flagName;
	}

	// PennMUSH src/flags.c:2790 do_flag_letter: "Letter for flag <name> set to '<c>'."
	[Test]
	public async ValueTask Flag_Letter_SetsTheLetter()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = await CreateLetterlessFlag();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {flagName}=1"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol).IsEqualTo("1");
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.FlagLetterSetFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	// PennMUSH src/flags.c:2793 do_flag_letter: an empty or absent letter clears it.
	[Test]
	public async ValueTask Flag_Letter_ClearsTheLetterWhenNoneGiven()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = await CreateLetterlessFlag();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {flagName}=2"));
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol).IsEqualTo("2");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {flagName}"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.FlagLetterClearedFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	// PennMUSH src/flags.c:2778 do_flag_letter: "Flag characters must be single characters."
	[Test]
	public async ValueTask Flag_Letter_RejectsMultipleCharacters()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var flagName = await CreateLetterlessFlag();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {flagName}=ABC"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.FlagCharactersMustBeSingleCharacters), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	// PennMUSH src/flags.c:2784 do_flag_letter: "Letter conflicts with the <other> flag." Unlike the
	// POWER flagspace, letter_to_flagptr's `n->tab == &ptab_flag` guard (:961) passes here, so the
	// check is reached in Penn too.
	[Test]
	public async ValueTask Flag_Letter_RejectsLetterTakenByAnotherFlag()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		var holder = await CreateLetterlessFlag();
		var claimant = await CreateLetterlessFlag();

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {holder}=3"));
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(holder)))!.Symbol).IsEqualTo("3");

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {claimant}=3"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(claimant)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.FlagLetterConflictFormat), executor, executor)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(holder));
		await Mediator.Send(new DeleteObjectFlagCommand(claimant));
	}

	// game/txt/hlp/pennv177.hlp:20 records the fix that lets two flags share a letter when they work
	// on different object types; MISTRUST and MYOPIC are seeded on 'm' for exactly that reason.
	[Test]
	public async ValueTask Flag_Letter_AllowsSameLetterOnADisjointType()
	{
		var playerFlag = await CreateLetterlessFlag(["PLAYER"]);
		var roomFlag = await CreateLetterlessFlag(["ROOM"]);

		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {playerFlag}=4"));
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@flag/letter {roomFlag}=4"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(playerFlag)))!.Symbol).IsEqualTo("4");
		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(roomFlag)))!.Symbol).IsEqualTo("4");

		await Mediator.Send(new DeleteObjectFlagCommand(playerFlag));
		await Mediator.Send(new DeleteObjectFlagCommand(roomFlag));
	}

	// PennMUSH src/flags.c:2764 do_flag_letter refuses anyone but God; a wizard is not enough.
	[Test]
	public async ValueTask Flag_Letter_RequiresGod()
	{
		var flagName = await CreateLetterlessFlag();
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagLetterNonGod");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@flag/letter {flagName}=5"));

		await Assert.That((await Mediator.Send(new GetObjectFlagQuery(flagName)))!.Symbol).IsEqualTo(string.Empty);
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NotEnoughMagic), testPlayer.DbRef, testPlayer.DbRef)).IsTrue();

		await Mediator.Send(new DeleteObjectFlagCommand(flagName));
	}

	// PennMUSH src/flags.c:2516 do_flag_add is God-only, like every other flag definition switch.
	[Test]
	public async ValueTask Flag_Add_RequiresGod()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagAddNonGod");
		await Parser.CommandParse(1, ConnectionService, MModule.single($"@set {testPlayer.DbRef}=WIZARD"));

		var flagName = $"TEST_FLAG_{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
		await Parser.CommandParse(testPlayer.Handle, ConnectionService,
			MModule.single($"@flag/add {flagName}=6"));

		await Assert.That(await Mediator.Send(new GetObjectFlagQuery(flagName))).IsNull();
		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(NotifyService,
			nameof(ErrorMessages.Notifications.NotEnoughMagic), testPlayer.DbRef, testPlayer.DbRef)).IsTrue();
	}

	// PennMUSH src/cmds.c:545 cmd_flag routes /decompile to do_list_flags, which has no God check.
	[Test]
	public async ValueTask Flag_Decompile_StaysOpenToNonGod()
	{
		var testPlayer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "FlagDecompileNonGod");

		await Parser.CommandParse(testPlayer.Handle, ConnectionService, MModule.single("@flag/decompile WIZARD"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(testPlayer.DbRef),
				Arg.Is<OneOf.OneOf<MString, string>>(s => TestHelpers.MessagePlainTextStartsWith(s, "Flag: WIZARD")),
				TestHelpers.MatchingObject(testPlayer.DbRef), INotifyService.NotificationType.Announce);
	}
}
