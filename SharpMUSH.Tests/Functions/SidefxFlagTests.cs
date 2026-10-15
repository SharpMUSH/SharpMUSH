using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The <c>sidefx_flag</c> option, from RhostMUSH: on, a side-effect function works only for an object
/// with the SIDEFX flag or the <c>softcode.sidefx</c> permission, and everyone else gets the same error
/// as side effects turned off. Off, which is the default, the flag changes nothing.
/// </summary>
public class SidefxFlagTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _mortal = null!;

	[Before(Test)]
	public async Task CreateMortal()
		=> _mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator,
			ConnectionService, "SidefxMortal");

	[After(Test)]
	public async Task Cleanup() => await ConnectionService.Disconnect(_mortal.Handle);

	private static IDisposable SidefxFlag(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { SidefxFlag = on }
		});

	[Test]
	public async Task OffSideEffectsNeedNoFlag()
	{
		using var _ = SidefxFlag(false);

		await Assert.That(await EvalAs(_mortal.DbRef, "attrib_set(me/SIDEFX_PROBE,off)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_mortal.DbRef, "get(me/SIDEFX_PROBE)")).IsEqualTo("off");
	}

	[Test]
	public async Task OnSideEffectsWithoutTheFlagAreDisabled()
	{
		using var _ = SidefxFlag(true);

		await Assert.That(await EvalAs(_mortal.DbRef, "attrib_set(me/SIDEFX_PROBE,on)"))
			.IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
		await Assert.That(await EvalAs(_mortal.DbRef, "pemit(me,sidefx probe)"))
			.IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
		await Assert.That(await EvalAs(_mortal.DbRef, "get(me/SIDEFX_PROBE)")).IsEqualTo(string.Empty);
	}

	/// <summary>Only the side-effect forms are gated: name() with one argument reads, and add() is not one.</summary>
	[Test]
	public async Task OnReadingFormsAndOtherFunctionsStillWork()
	{
		using var _ = SidefxFlag(true);

		await Assert.That(await EvalAs(_mortal.DbRef, "name(me)")).StartsWith("SidefxMortal");
		await Assert.That(await EvalAs(_mortal.DbRef, "add(1,2)")).IsEqualTo("3");
		await Assert.That(await EvalAs(_mortal.DbRef, "name(me,Renamed)")).IsEqualTo(ErrorMessages.Returns.FunctionDisabled);
	}

	/// <summary>Anyone may set SIDEFX on what they control, as on RhostMUSH.</summary>
	[Test]
	public async Task OnTheFlagAllowsSideEffects()
	{
		using var _ = SidefxFlag(true);

		await CmdAs(_mortal.DbRef, _mortal.Handle, "@set me=SIDEFX");
		await Assert.That(await EvalAs(_mortal.DbRef, "hasflag(me,SIDEFX)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_mortal.DbRef, "attrib_set(me/SIDEFX_PROBE,flagged)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_mortal.DbRef, "get(me/SIDEFX_PROBE)")).IsEqualTo("flagged");
	}

	/// <summary>The wizard role holds softcode.sidefx, so a wizard needs no SIDEFX flag.</summary>
	[Test]
	public async Task OnThePermissionAllowsSideEffects()
	{
		using var _ = SidefxFlag(true);
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_mortal.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard,
			(await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();

		await Assert.That(await EvalAs(_mortal.DbRef, "permission(me,softcode.sidefx)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_mortal.DbRef, "hasflag(me,SIDEFX)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_mortal.DbRef, "attrib_set(me/SIDEFX_PROBE,granted)")).IsEqualTo(string.Empty);
		await Assert.That(await EvalAs(_mortal.DbRef, "get(me/SIDEFX_PROBE)")).IsEqualTo("granted");
	}

	/// <summary>A configuration file names the option; leaving it out leaves it off.</summary>
	[Test]
	public async Task OptionIsReadFromAConfigurationFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-sidefx-{Guid.NewGuid():N}.cnf");
		try
		{
			await File.WriteAllTextAsync(path, "sidefx_flag yes\n");
			await Assert.That(SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility.SidefxFlag).IsTrue();
			await File.WriteAllTextAsync(path, string.Empty);
			await Assert.That(SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility.SidefxFlag).IsFalse();
		}
		finally
		{
			File.Delete(path);
		}
	}
}
