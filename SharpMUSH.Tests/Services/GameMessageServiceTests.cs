using System.Text.Json;
using Mediator;
using MarkupString.Ansi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The connect screen, the MOTDs and the rest: the stored text and its shipped default, and the bundled Messages
/// object when the game reads from it. Each test builds its own <see cref="GameMessageService"/> over an in-memory
/// record and a Messages object of its own, so nothing here changes what the shared world's connections are shown.
/// </summary>
public class GameMessageServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private const string LogoGreen = "color: #00f5b7";

	[Test]
	public async Task TheShippedConnectScreenDrawsTheLogoInGreen()
	{
		var (service, _) = Build(installed: false);

		var html = AnsiEscapeParser.Parse(service.ShippedText(GameMessage.Connect)).Render(MarkupFormat.Html);

		await Assert.That(html).Contains($"<span style=\"{LogoGreen}\">==</span>");
		await Assert.That(html).Contains("Welcome to");
	}

	[Test]
	public async Task EveryMessageShipsAText()
	{
		var (service, _) = Build(installed: false);

		foreach (var message in GameMessages.All)
		{
			await Assert.That(service.ShippedText(message)).IsNotEmpty().Because(message.ToString());
		}
	}

	[Test]
	public async Task WithoutThePackageTheStoredTextIsShown()
	{
		var (service, _) = Build(installed: false);

		var shown = await service.RenderAsync(GameMessage.Quit, 0);

		await Assert.That(shown!.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Quit)));
		await Assert.That(await service.GetSourceAsync()).IsEqualTo((GameMessageSource.Stored, false));
	}

	[Test]
	public async Task AnEditedTextIsShownUntilItIsReset()
	{
		var (service, _) = Build(installed: false);
		const string edited = "\u001b[1;31mClosed\u001b[0m for the night.";

		await service.SetTextAsync(GameMessage.Down, edited);
		var shown = await service.RenderAsync(GameMessage.Down, 0);

		await Assert.That(await service.IsDefaultAsync(GameMessage.Down)).IsFalse();
		await Assert.That(await service.GetTextAsync(GameMessage.Down)).IsEqualTo(edited);
		await Assert.That(shown!.ToPlainText()).IsEqualTo("Closed for the night.");
		await Assert.That(shown.Render(MarkupFormat.Ansi)).Contains("\u001b[");

		await service.SetTextAsync(GameMessage.Down, null);

		await Assert.That(await service.IsDefaultAsync(GameMessage.Down)).IsTrue();
		await Assert.That(await service.GetTextAsync(GameMessage.Down)).IsEqualTo(service.ShippedText(GameMessage.Down));
	}

	[Test]
	public async Task AnEmptiedTextShowsNothing()
	{
		var (service, _) = Build(installed: false);

		await service.SetTextAsync(GameMessage.Guest, "");

		await Assert.That(await service.RenderAsync(GameMessage.Guest, 0)).IsNull();
	}

	/// <summary>The bundled object's connect screen is the shipped one, line for line, with the logo still green.</summary>
	[Test]
	public async Task TheMessagesObjectShowsTheSameConnectScreen()
	{
		var (service, _) = await WithMessagesObjectAsync();

		var shown = await service.RenderAsync(GameMessage.Connect, 0);

		await Assert.That(await service.GetSourceAsync()).IsEqualTo((GameMessageSource.Object, false));
		await Assert.That(Lines(shown!.ToPlainText())).IsEquivalentTo(Lines(Plain(service.ShippedText(GameMessage.Connect))));
		await Assert.That(shown.Render(MarkupFormat.Html)).Contains(LogoGreen);
	}

	[Test]
	public async Task TheMessagesObjectEvaluatesItsAttributes()
	{
		var (service, _) = await WithMessagesObjectAsync();
		var mudName = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Net.MudName;

		var shown = await service.RenderAsync(GameMessage.Quit, 0);

		await Assert.That(shown!.ToPlainText()).IsEqualTo($"\nGoodbye from {mudName}!");
	}

	[Test]
	public async Task AMessageTheObjectLacksFallsBackToTheStoredText()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		await AttributeService.ClearAttributeAsync(holder, holder, "DOWN", IAttributeService.AttributePatternMode.Exact);

		var shown = await service.RenderAsync(GameMessage.Down, 0);

		await Assert.That(shown!.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Down)));
	}

	[Test]
	public async Task AnAttributeThatEvaluatesToNothingShowsNothing()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		await AttributeService.SetAttributeAsync(holder, holder, "GUEST", MarkupText.Plain("[null(quiet)]"));

		await Assert.That(await service.RenderAsync(GameMessage.Guest, 0)).IsNull();
	}

	[Test]
	public async Task ChoosingTheStoredTextOverridesThePackage()
	{
		var (service, _) = await WithMessagesObjectAsync();

		await service.SetSourceAsync(GameMessageSource.Stored);
		var shown = await service.RenderAsync(GameMessage.Quit, 0);

		await Assert.That(await service.GetSourceAsync()).IsEqualTo((GameMessageSource.Stored, true));
		await Assert.That(shown!.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Quit)));

		await service.SetSourceAsync(null);

		await Assert.That(await service.GetSourceAsync()).IsEqualTo((GameMessageSource.Object, false));
	}

	[Test]
	public async Task TheViewerIsTheEnactor()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		var viewer = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "MsgViewer");
		var viewerObject = (await Mediator.Send(new GetObjectNodeQuery(viewer))).Expect<AnySharpObject>();
		await AttributeService.SetAttributeAsync(holder, holder, "MOTD", MarkupText.Plain("Hello, [name(%#)]. [lt(%0,0)]"));

		var shown = await service.RenderAsync(GameMessage.Motd, 7, viewerObject);

		await Assert.That(shown!.ToPlainText()).IsEqualTo($"Hello, {viewerObject.Object().Name}. 0");
	}

	private (GameMessageService Service, IPackageRegistryService Packages) Build(bool installed, string? objid = null)
	{
		var packages = Substitute.For<IPackageRegistryService>();
		Found<InstalledPackageRecord> record = installed
			? new InstalledPackageRecord(GameMessages.PackageId, "1.0.0", BundledPackages.SourceRepo, null,
				BundledPackages.SourceCommit, null, DateTimeOffset.UtcNow, 1)
			: new NotFound();
		IReadOnlyList<PackageObjectRecord> objects = objid is null
			? []
			: [new PackageObjectRecord(GameMessages.PackageId, GameMessages.ObjectRef, objid, "thing")];
		packages.GetInstalledPackageAsync(GameMessages.PackageId).Returns(Task.FromResult(record));
		packages.GetPackageObjectsAsync(GameMessages.PackageId).Returns(Task.FromResult(objects));

		var service = new GameMessageService(
			new InMemoryServerData(),
			packages,
			Mediator,
			AttributeService,
			new Lazy<IMUSHCodeParser>(WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>),
			WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>(),
			NullLogger<GameMessageService>.Instance);
		return (service, packages);
	}

	/// <summary>A thing carrying the bundled package's attributes, as the service's Messages object.</summary>
	private async Task<(GameMessageService Service, AnySharpObject Holder)> WithMessagesObjectAsync()
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "Messages");
		var holder = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

		var manifest = new PackageManifestService().ParseManifest(BundledPackages.ManifestYaml(GameMessages.PackageId))
			.Expect<ParsedPackageManifest>().Manifest;
		foreach (var (name, attribute) in manifest.Objects.Single(o => o.Ref == GameMessages.ObjectRef).Attributes)
		{
			await AttributeService.SetAttributeAsync(holder, holder, name, MarkupText.Plain(attribute.Value));
		}

		var (service, _) = Build(installed: true, objid: holder.Object().DBRef.ToString());
		return (service, holder);
	}

	private static string Plain(string ansi) => AnsiEscapeParser.Parse(ansi).ToPlainText();

	private static string[] Lines(string text) => [.. text.Split('\n').Select(line => line.TrimEnd())];

	private sealed class InMemoryServerData : IExpandedObjectDataService
	{
		private readonly Dictionary<string, string> _server = [];

		public ValueTask<T?> GetExpandedDataAsync<T>(SharpObject obj) where T : class => throw new NotSupportedException();

		public ValueTask SetExpandedDataAsync<T>(T data, SharpObject obj, bool ignoreNull = false) where T : class
			=> throw new NotSupportedException();

		public ValueTask<T?> GetExpandedServerDataAsync<T>() where T : class
			=> ValueTask.FromResult(_server.TryGetValue(typeof(T).Name, out var json) ? JsonSerializer.Deserialize<T>(json) : null);

		public ValueTask SetExpandedServerDataAsync<T>(T data, bool ignoreNull = false) where T : class
		{
			_server[typeof(T).Name] = JsonSerializer.Serialize(data);
			return ValueTask.CompletedTask;
		}
	}
}
