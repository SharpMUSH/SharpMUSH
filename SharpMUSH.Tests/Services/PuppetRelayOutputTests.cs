using MarkupString;
using Mediator;
using NSubstitute;
using OneOf.Types;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A puppet relay is the one notification path that did not go through <see cref="NotifyService"/>:
/// it rendered ANSI itself and pushed the bytes out as a <c>TelnetOutputMessage</c>, which skips
/// <see cref="MarkupOutputRenderer"/> entirely. On a Pueblo or MXP connection that arrived neither
/// entity-encoded nor line-mode prefixed, so a <c>&lt;</c> or <c>&amp;</c> in whatever the puppet
/// heard reached the client's parser raw — and an unprefixed line sits in MXP's default open mode,
/// where <c>&lt;b&gt;</c>, <c>&lt;color&gt;</c> and <c>&lt;font&gt;</c> are honoured.
/// </summary>
public class PuppetRelayOutputTests
{
	/// <summary>What a player would have to say to exploit the old path.</summary>
	private const string Heard = "Tom & <color red>Sue</color> said \"hi\"";

	private const long OwnerHandle = 42;

	[Test]
	public async Task PuppetRelay_PublishesMarkup_RatherThanPreRenderedBytes()
	{
		var (service, bus) = BuildRelay();

		await service.ProcessNotificationAsync(
			new NotificationContext(Target: PuppetRef, Location: RoomRef, ExcludedObjects: []),
			MarkupText.Plain(Heard),
			Speaker,
			INotifyService.NotificationType.Say);

		await bus.Received(1).HandlePublish(Arg.Any<MarkupOutputMessage>());
		await bus.DidNotReceive().Publish(Arg.Any<TelnetOutputMessage>(), Arg.Any<CancellationToken>());
	}

	/// <summary>
	/// The point of publishing markup rather than bytes: the ConnectionServer gets to apply the
	/// connection's negotiated format. Rendering the relay's own published markup through
	/// <see cref="MarkupOutputRenderer"/> is what the connection server does with it.
	/// </summary>
	[Test]
	public async Task PuppetRelay_ReachesAnMxpClientEncodedAndLinePrefixed()
	{
		var (service, bus) = BuildRelay();

		await service.ProcessNotificationAsync(
			new NotificationContext(Target: PuppetRef, Location: RoomRef, ExcludedObjects: []),
			MarkupText.Plain(Heard),
			Speaker,
			INotifyService.NotificationType.Say);

		var published = bus.ReceivedCalls()
			.Select(call => call.GetArguments().FirstOrDefault())
			.OfType<MarkupOutputMessage>()
			.Single();

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(published.Markup, MxpConnection()).Data);

		await Assert.That(published.Handle).IsEqualTo(OwnerHandle);
		await Assert.That(rendered).StartsWith(ProtocolConstants.MxpLineSecure);
		await Assert.That(rendered).Contains("&lt;color red&gt;");
		await Assert.That(rendered).DoesNotContain("<color red>");
		// The puppet's default @prefix, which the relay is also responsible for carrying.
		await Assert.That(rendered).Contains("Fido&gt; ");
	}

	// ── Fixture ──────────────────────────────────────────────────────────────────

	private static readonly DBRef RoomRef = new(0, 0);
	private static readonly DBRef PuppetRef = new(3, 0);
	private static readonly DBRef OwnerRef = new(2, 0);

	private static readonly TestObjectFactory Factory = new();
	private static readonly AnySharpObject Speaker = Factory.CreatePlayer(9, "Speaker");

	private static ConnectionServerService.ConnectionData MxpConnection() =>
		new(
			Handle: OwnerHandle,
			PlayerDbRef: null,
			State: ConnectionServerService.ConnectionState.Connected,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			EncodingFunction: () => Encoding.UTF8,
			DisconnectFunction: () => { },
			GMCPFunction: null,
			Capabilities: new ProtocolCapabilities(Format: OutputFormat.Mxp),
			Preferences: null,
			ConnectionType: "telnet");

	private static (IListenerRoutingService Service, IMessageBus Bus) BuildRelay(string? change = null)
	{
		var owner = OwnerPlayer();
		var puppet = Puppet(owner);

		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>())
			.Returns(new AnyOptionalSharpObject(puppet.AsThing));

		var permissions = Substitute.For<IPermissionService>();
		permissions.CanInteract(
			Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<IPermissionService.InteractType>())
			.Returns(true);

		// No PREFIX and no LISTEN: the relay falls back to "<name>> " and the LISTEN pass returns early.
		var attributes = Substitute.For<IAttributeService>();
		var current = Connection();
		attributes.GetAttributeAsync(
			Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), Arg.Any<string>(),
			Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns(call =>
			{
				if (call.Arg<string>() == "PREFIX" && change is not null)
					current = change switch
					{
						"character" => current with { Ref = Speaker.Object().DBRef },
						"stamp" => current with { Ref = new DBRef(OwnerRef.Number, 999) },
						"session" => current with { Metadata = new(new[] { KeyValuePair.Create("SessionId", "new-session") }) },
						"logout" => current with { State = IConnectionService.ConnectionState.Connected, Ref = null },
						_ => current
					};
				return new OptionalSharpAttributeOrError(new None());
			});

		var services = Substitute.For<IServiceProvider>();
		services.GetService(typeof(IAttributeService)).Returns(attributes);

		var connections = Substitute.For<IConnectionService>();
		connections.Get(OwnerRef).Returns(_ => Connected());
		connections.Get(OwnerHandle).Returns(_ => current);

		var bus = Substitute.For<IMessageBus>();

		var service = new ListenerRoutingService(
			mediator,
			Substitute.For<IListenPatternMatcher>(),
			permissions,
			Substitute.For<ILockService>(),
			connections,
			services,
			bus, DisabledRealityPolicy.Instance);

		return (service, bus);
	}

	private static IConnectionService.ConnectionData Connection() => new(
		OwnerHandle, OwnerRef, IConnectionService.ConnectionState.LoggedIn,
		_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
		new ConcurrentDictionary<string, string>(new[] { KeyValuePair.Create("SessionId", "original") }));

	private static async IAsyncEnumerable<IConnectionService.ConnectionData> Connected()
	{
		yield return Connection();
		await Task.CompletedTask;
	}

	[Test]
	[Arguments("character")]
	[Arguments("stamp")]
	[Arguments("session")]
	[Arguments("logout")]
	[Arguments("unchanged")]
	public async Task PuppetRelayRechecksBindingAfterPrefixRead(string change)
	{
		var (service, bus) = BuildRelay(change);
		await service.ProcessNotificationAsync(
			new NotificationContext(PuppetRef, RoomRef, []), MarkupText.Plain(Heard), Speaker,
			INotifyService.NotificationType.Say);
		var published = bus.ReceivedCalls().Count(call => call.GetArguments().FirstOrDefault() is MarkupOutputMessage);
		await Assert.That(published).IsEqualTo(change == "unchanged" ? 1 : 0);
	}

	private static SharpPlayer OwnerPlayer()
	{
		var room = Room();
		var obj = BareObject(OwnerRef.Number, "Owner", "Player");
		var player = new SharpPlayer
		{
			Object = obj,
			Aliases = [],
			Location = new(_ => Task.FromResult(new AnySharpContainer(room))),
			Home = new(_ => Task.FromResult(new AnySharpContainer(room))),
			PasswordHash = string.Empty,
			PasswordSalt = null,
			Quota = 20
		};
		obj.Owner = new(_ => Task.FromResult(player));
		return player;
	}

	/// <summary>
	/// A PUPPET thing owned by <paramref name="owner"/>, and deliberately not in the owner's room —
	/// a puppet in the same room as its owner relays nothing unless it is VERBOSE.
	/// </summary>
	private static AnySharpObject Puppet(SharpPlayer owner)
	{
		var obj = BareObject(PuppetRef.Number, "Fido", "Thing");
		obj.Owner = new(_ => Task.FromResult(owner));
		obj.Flags = new(() => new[] { new SharpObjectFlag { Name = "PUPPET", Symbol = "p", System = false, SetPermissions = [], UnsetPermissions = [], TypeRestrictions = [] } }.ToAsyncEnumerable());

		var elsewhere = Room(99, "Elsewhere");
		return new AnySharpObject(new SharpThing
		{
			Object = obj,
			Location = new(_ => Task.FromResult(new AnySharpContainer(elsewhere))),
			Home = new(_ => Task.FromResult(new AnySharpContainer(elsewhere)))
		});
	}

	private static SharpRoom Room(int key = 0, string name = "Room Zero") =>
		new()
		{
			Id = $"test-room-{key}",
			Object = BareObject(key, name, "Room"),
			Location = new(_ => Task.FromResult<AnyOptionalSharpContainer>(new None()))
		};

	private static SharpObject BareObject(int key, string name, string type) =>
		new()
		{
			Key = key,
			CreationTime = 0L,
			Name = name,
			Type = type,
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(_ => Task.FromResult<SharpPlayer>(null!)),
			Powers = new(AsyncEnumerable.Empty<SharpPower>),
			Attributes = new(AsyncEnumerable.Empty<SharpAttribute>),
			LazyAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
			AllAttributes = new(AsyncEnumerable.Empty<SharpAttribute>),
			LazyAllAttributes = new(AsyncEnumerable.Empty<LazySharpAttribute>),
			Flags = new(AsyncEnumerable.Empty<SharpObjectFlag>),
			Parent = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
			Zone = new(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
			Children = new(AsyncEnumerable.Empty<SharpObject>)
		};
}
