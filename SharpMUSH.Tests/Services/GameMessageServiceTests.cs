using System.Text.Json;
using Mediator;
using MarkupString.Ansi;
using MarkupString.Layout;
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
/// The connect screen, the MOTDs and the rest: the stored text and its shipped default, and the object
/// <c>messages_object</c> names. Each test builds its own <see cref="GameMessageService"/> over an in-memory record,
/// options of its own and a Messages object of its own, so nothing here changes what the shared world's connections
/// are shown.
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
		var service = Build();

		var html = AnsiEscapeParser.Parse(service.ShippedText(GameMessage.Connect)).Render(MarkupFormat.Html);

		await Assert.That(html).Contains($"<span style=\"{LogoGreen}\">          ==         ==</span>");
		await Assert.That(html).Contains("Welcome to");
	}

	[Test]
	public async Task EveryMessageShipsAText()
	{
		var service = Build();

		foreach (var message in GameMessages.All)
		{
			await Assert.That(service.ShippedText(message)).IsNotEmpty().Because(message.ToString());
		}
	}

	[Test]
	public async Task WithoutThePackageTheStoredTextIsShown()
	{
		var service = Build();

		var shown = (await service.RenderAsync(GameMessage.Quit, 0)).Expect<MString>();

		await Assert.That(shown.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Quit)));
		await Assert.That((await service.MessagesObjectAsync()).Value).IsTypeOf<None>();
	}

	[Test]
	public async Task AnEditedTextIsShownUntilItIsReset()
	{
		var service = Build();
		const string edited = "\u001b[1;31mClosed\u001b[0m for the night.";

		await service.SetTextAsync(GameMessage.Down, edited);
		var shown = (await service.RenderAsync(GameMessage.Down, 0)).Expect<MString>();

		await Assert.That(await service.IsDefaultAsync(GameMessage.Down)).IsFalse();
		await Assert.That(await service.GetTextAsync(GameMessage.Down)).IsEqualTo(edited);
		await Assert.That(shown.ToPlainText()).IsEqualTo("Closed for the night.");
		await Assert.That(shown.Render(MarkupFormat.Ansi)).Contains("\u001b[");

		await service.SetTextAsync(GameMessage.Down, null);

		await Assert.That(await service.IsDefaultAsync(GameMessage.Down)).IsTrue();
		await Assert.That(await service.GetTextAsync(GameMessage.Down)).IsEqualTo(service.ShippedText(GameMessage.Down));
	}

	[Test]
	public async Task AnEmptiedTextShowsNothing()
	{
		var service = Build();

		await service.SetTextAsync(GameMessage.Guest, "");

		await Assert.That((await service.RenderAsync(GameMessage.Guest, 0)).Value).IsTypeOf<None>();
	}

	/// <summary>
	/// The bundled object's connect screen, as an ASCII client 78 columns wide is sent it, is the shipped one line for
	/// line, with the logo's art still green.
	/// </summary>
	[Test]
	public async Task TheMessagesObjectShowsTheSameConnectScreen()
	{
		var (service, _) = await WithMessagesObjectAsync();

		var shown = (await service.RenderAsync(GameMessage.Connect, 0)).Expect<MString>();
		var ascii = BlockLayout.Relayout(shown, 78, new LayoutContext { AsciiOnly = true }).ToPlainText().Replace(MudName, "SharpMUSH");

		await Assert.That(Lines(ascii)).IsEquivalentTo(Lines(Plain(service.ShippedText(GameMessage.Connect))));
		await Assert.That(shown.Render(MarkupFormat.Ansi)).Contains("\u001b[38;2;0;245;183m          ==         ==");
	}

	/// <summary>The object shows the logo as a picture, with the text art as the description's stand-in for a terminal.</summary>
	[Test]
	public async Task TheMessagesObjectShowsTheLogoAsAPicture()
	{
		var (service, _) = await WithMessagesObjectAsync();

		var html = (await service.RenderAsync(GameMessage.Connect, 0)).Expect<MString>().Render(MarkupFormat.Html);

		await Assert.That(html).Contains("src=\"/assets/logo.png\" alt=\"The SharpMUSH logo\"");
	}

	[Test]
	public async Task TheMessagesObjectEvaluatesItsAttributes()
	{
		var (service, _) = await WithMessagesObjectAsync();

		var shown = (await service.RenderAsync(GameMessage.Quit, 0)).Expect<MString>();

		await Assert.That(shown.ToPlainText()).IsEqualTo($"\nGoodbye from {MudName}. Come back soon!");
	}

	[Test]
	public async Task AMessageTheObjectLacksFallsBackToTheStoredText()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		await AttributeService.ClearAttributeAsync(holder, holder, "DOWN", IAttributeService.AttributePatternMode.Exact);

		var shown = (await service.RenderAsync(GameMessage.Down, 0)).Expect<MString>();

		await Assert.That(shown.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Down)));
	}

	[Test]
	public async Task AnAttributeThatEvaluatesToNothingShowsNothing()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		await AttributeService.SetAttributeAsync(holder, holder, "GUEST", MarkupText.Plain("[null(quiet)]"));

		await Assert.That((await service.RenderAsync(GameMessage.Guest, 0)).Value).IsTypeOf<None>();
	}

	/// <summary>An option naming an object that is gone shows the stored text, not nothing.</summary>
	[Test]
	public async Task AMissingObjectFallsBackToTheStoredText()
	{
		var service = Build(messagesObject: int.MaxValue - 7);

		var shown = (await service.RenderAsync(GameMessage.Quit, 0)).Expect<MString>();

		await Assert.That((await service.MessagesObjectAsync()).Value).IsTypeOf<None>();
		await Assert.That(shown.ToPlainText()).IsEqualTo(Plain(service.ShippedText(GameMessage.Quit)));
	}

	[Test]
	public async Task TheViewerIsTheEnactor()
	{
		var (service, holder) = await WithMessagesObjectAsync();
		var viewer = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "MsgViewer");
		var viewerObject = (await Mediator.Send(new GetObjectNodeQuery(viewer))).Expect<AnySharpObject>();
		await AttributeService.SetAttributeAsync(holder, holder, "MOTD", MarkupText.Plain("Hello, [name(%#)]. [lt(%0,0)]"));

		var shown = (await service.RenderAsync(GameMessage.Motd, 7, viewerObject)).Expect<MString>();

		await Assert.That(shown.ToPlainText()).IsEqualTo($"Hello, {viewerObject.Object().Name}. 0");
	}

	/// <summary>A service over an in-memory record, with <c>messages_object</c> naming <paramref name="messagesObject"/>.</summary>
	private GameMessageService Build(int? messagesObject = null)
	{
		var shared = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(shared with { Database = shared.Database with { MessagesObject = (uint?)messagesObject } });

		return new GameMessageService(
			new InMemoryServerData(),
			Mediator,
			AttributeService,
			new Lazy<IMUSHCodeParser>(WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>),
			options,
			NullLogger<GameMessageService>.Instance);
	}

	/// <summary>A thing carrying the bundled package's attributes, as the service's Messages object.</summary>
	private async Task<(GameMessageService Service, AnySharpObject Holder)> WithMessagesObjectAsync()
	{
		var dbref = await TestIsolationHelpers.CreateTestThingAsync(WebAppFactoryArg.CommandParser, ConnectionService, "Messages");
		var holder = (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

		var manifest = new PackageManifestService().ParseManifest(BundledPackages.ManifestYaml(GameMessages.PackageId))
			.Expect<ParsedPackageManifest>().Manifest;
		var spec = manifest.Objects.Single(o => o.Ref == GameMessages.ObjectRef);
		foreach (var (name, attribute) in spec.Attributes)
		{
			await AttributeService.SetAttributeAsync(holder, holder, name, MarkupText.Plain(attribute.Value));
		}
		foreach (var power in spec.Powers)
		{
			await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power #{dbref.Number}={power}"));
		}

		return (Build(holder.Object().DBRef.Number), holder);
	}

	private string MudName => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Net.MudName;

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
