using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled finger-profile bridge: with finger and it installed, the portal profile's schema gains a section
/// for each +finger view, and the profile's data the fields as +finger shows them to the reader. Not in parallel
/// with <see cref="FingerIntegrationTests"/>: both install and remove finger.
/// </summary>
[NotInParallel]
public class FingerProfileIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	/// <summary>A connected player in a room of its own.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, home);
	}

	private async Task ApplyAsync(string packageId)
	{
		var controller = new PackagesController(
			WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, packageId, null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because($"{packageId} must install from the catalogue");
		await WebAppFactoryArg.QueueBarrierAsync();
	}

	private async Task<JsonElement> GetJsonAsync(string path)
	{
		var response = await WebAppFactoryArg.CreateHttpClient().GetAsync(path);
		var body = await response.Content.ReadAsStringAsync();
		await Assert.That((int)response.StatusCode).IsEqualTo(200).Because(body);
		using var document = JsonDocument.Parse(body);
		return document.RootElement.Clone();
	}

	/// <summary>The keys of the elements in the schema's section <paramref name="key"/>, or none when it has no such section.</summary>
	private static IReadOnlyList<string> SectionKeys(JsonElement schema, string key) =>
		schema.GetProperty("pages")[0].GetProperty("sections").EnumerateArray()
			.Where(section => section.TryGetProperty("key", out var k) && k.GetString() == key)
			.SelectMany(section => section.GetProperty("elements").EnumerateArray())
			.Select(element => element.GetProperty("key").GetString()!)
			.ToList();

	private static string Value(JsonElement profile, string key) =>
		profile.GetProperty("fields").GetProperty(key).GetProperty("value").GetString()!;

	[Test]
	public async Task TheProfileShowsTheFingerFieldsAsAVisitorReadsThem()
	{
		try
		{
			await ApplyAsync("finger");
			await ApplyAsync("finger-profile");
			var ann = await Player("FpAnn");
			var staff = await Player("FpStaff");
			var name = TestIsolationHelpers.GenerateUniqueName("FpName");
			var objid = await God($"think objid(#{ann.DbRef.Number})");

			await As(ann, $"+finger/set full name={name}");
			await As(ann, "+finger/set age=41");
			await As(ann, "+finger/set quote=Maps stay.");
			await As(ann, "+oocfinger/set timezone=US Eastern");
			await As(ann, "+finger/hide age");

			// A section for each view, its elements every field finger has, in finger's order. Long text keeps its
			// colour, so it is an mstring.
			var schema = await GetJsonAsync("http/profile/schema");
			await Assert.That(string.Join(" ", SectionKeys(schema, "finger-ic")))
				.IsEqualTo("finger_fullname finger_alias finger_age finger_species finger_occupation finger_quote");
			await Assert.That(SectionKeys(schema, "finger-ooc")).Contains("finger_timezone");
			var quote = schema.GetProperty("pages")[0].GetProperty("sections").EnumerateArray()
				.SelectMany(section => section.GetProperty("elements").EnumerateArray())
				.First(element => element.GetProperty("key").GetString() == "finger_quote");
			await Assert.That(quote.GetProperty("type").GetString()).IsEqualTo("mstring");

			// A visitor reads what everyone may: not the field Ann hid.
			var profile = await GetJsonAsync($"http/profile?objid={Uri.EscapeDataString(objid)}");
			await Assert.That(Value(profile, "finger_fullname")).IsEqualTo(name);
			await Assert.That(Value(profile, "finger_timezone")).IsEqualTo("US Eastern");
			await Assert.That(Value(profile, "finger_age")).IsEqualTo(string.Empty);
			await Assert.That(Value(profile, "finger_quote")).Contains("Maps stay.");
			await Assert.That(profile.GetProperty("fields").GetProperty("created").GetProperty("value").GetString()).IsNotEmpty();

			// +config finger/portalhide leaves a field off the portal; +finger still has it.
			await God($"@permission/allow #{staff.DbRef.Number}=config.admin");
			await Assert.That(await As(staff, "+config/add finger/portalhide=timezone")).Contains("finger/portalhide is now timezone");
			try
			{
				await Assert.That(SectionKeys(await GetJsonAsync("http/profile/schema"), "finger-ooc")).DoesNotContain("finger_timezone");
				await Assert.That(await As(staff, $"+oocfinger {ann.Name}")).Contains("US Eastern");
			}
			finally
			{
				await As(staff, "+config/reset finger/portalhide");
			}

			// Uninstalling the bridge leaves the profile as profile-handler ships it.
			(await WebAppFactoryArg.Services.UninstallWithBridgesAsync("finger-profile")).Expect<Success>();
			await Assert.That(SectionKeys(await GetJsonAsync("http/profile/schema"), "finger-ic")).IsEmpty();
			var plain = await GetJsonAsync($"http/profile?objid={Uri.EscapeDataString(objid)}");
			await Assert.That(plain.GetProperty("fields").TryGetProperty("finger_fullname", out _)).IsFalse();
		}
		finally
		{
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("finger-profile");
			await WebAppFactoryArg.Services.UninstallWithBridgesAsync("finger");
		}
	}
}
