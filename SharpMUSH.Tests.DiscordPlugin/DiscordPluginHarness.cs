using System.Collections.Concurrent;
using System.Collections.Immutable;
using MarkupString.Layout;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>
/// The Discord plugin as the server registers it (<see cref="SharpMUSH.Plugins.Discord.DiscordPlugin.RegisterServices"/>),
/// over a world of its own and substitutes for the host's services. Nothing is started, so the bot never connects:
/// what a test sees is the plugin with no Discord to talk to.
/// </summary>
public sealed class DiscordPluginHarness : IAsyncDisposable
{
	public static readonly DateTimeOffset Now = new(2026, 10, 15, 3, 0, 0, TimeSpan.Zero);

	private sealed class FixedClock : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => Now;
	}

	private sealed class DefaultOptions : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = SharpMUSHOptions.Default();
	}

	/// <summary>Secrets in memory; no bot token is stored.</summary>
	public sealed class MemorySecrets : ISecretStore
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public IReadOnlyList<SecretDefinition> Definitions => [];

		public ValueTask<FoundResult<string>> GetAsync(string name, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<FoundResult<string>>(Values.TryGetValue(name, out var value) ? value : new NotFound());

		public ValueTask<Result<SecretStatus>> SetAsync(string name, string value, SecretAuthor author,
			CancellationToken cancellationToken = default)
		{
			Values[name] = value;
			return ValueTask.FromResult<Result<SecretStatus>>(Status(name));
		}

		public ValueTask<Found<SecretStatus>> ClearAsync(string name, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult<Found<SecretStatus>>(Values.TryRemove(name, out _) ? Status(name) : new NotFound());

		public ValueTask<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<IReadOnlyList<string>> HostsAsync(string name, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<Result<SecretStatus>> SetHostsAsync(string name, IReadOnlyList<string> hosts,
			CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask<SecretSnapshot> SnapshotAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		public ValueTask RestoreAsync(SecretSnapshot snapshot, CancellationToken cancellationToken = default)
			=> throw new NotSupportedException();

		private static SecretStatus Status(string name) => new(name, null, default, null, true, Now, new SecretAuthor("#1:1", "God"), null, false);
	}

	private readonly string _path;
	private readonly LightningDatabase _database;
	private readonly IHouseLookService _look = Substitute.For<IHouseLookService>();
	private readonly Action<IServiceCollection>? _configure;
	private readonly FixedClock _clock = new();
	private ServiceProvider? _owned;
	private IServiceProvider? _host;
	private readonly ConcurrentQueue<(DBRef To, string Text)> _told = new();
	private readonly SemaphoreSlim _toldSignal = new(0);

	private DiscordPluginHarness(string path, LightningDatabase database, Action<IServiceCollection>? configure)
	{
		_path = path;
		_database = database;
		_configure = configure;

		var notify = Substitute.For<INotifyService>();
		notify.Notify(Arg.Any<AnySharpObject>(), Arg.Any<SharpMessage>(), Arg.Any<AnySharpObject?>(), Arg.Any<INotifyService.NotificationType>())
			.Returns(call =>
			{
				Tell(call.ArgAt<AnySharpObject>(0).Object().DBRef, call.ArgAt<SharpMessage>(1));
				return ValueTask.CompletedTask;
			});
		Notify = notify;

		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult(Objects.TryGetValue(call.Arg<GetObjectNodeQuery>().DBRef.Number, out var found)
				? new AnyOptionalSharpObject(found)
				: new AnyOptionalSharpObject(new None())));
		Mediator = mediator;

		_look.Look.Returns(LayoutTheme.Default);
		Objects[God.Object().Key] = God;
	}

	public static async Task<DiscordPluginHarness> StartAsync(Action<IServiceCollection>? configure = null)
	{
		var path = Path.Join(Path.GetTempPath(), "sharpmush-discord-" + Guid.NewGuid().ToString("N"));
		var database = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 64L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await database.Migrate();
		return new DiscordPluginHarness(path, database, configure);
	}

	/// <summary>
	/// The plugin's services: a provider of the harness's own, or the host's once <see cref="UseHost"/> names one built
	/// from <see cref="Register"/>.
	/// </summary>
	public IServiceProvider Services => _host ?? (_owned ??= Build());

	/// <summary>Registers the host's substitutes and the plugin, as the server registers it.</summary>
	public void Register(IServiceCollection services)
	{
		services.AddLogging();
		services.AddSingleton<ILightningStorageAccessor>(_database);
		services.AddSingleton<ISecretStore>(Secrets);
		services.AddSingleton<TimeProvider>(_clock);
		services.AddSingleton(Notify);
		services.AddSingleton(Mediator);
		services.AddSingleton(_look);
		services.AddSingleton(Accounts);
		services.AddSingleton(Feeds);
		services.AddSingleton(Capabilities);
		services.AddSingleton(Roles);
		services.AddSingleton<IOptionsWrapper<SharpMUSHOptions>>(new DefaultOptions());
		_configure?.Invoke(services);
		new SharpMUSH.Plugins.Discord.DiscordPlugin().RegisterServices(services);
		// The plugin mints /link tickets against the system clock; these expire against the harness's.
		services.AddSingleton(new SharpMUSH.Plugins.Discord.Members.DiscordLinkTickets(_clock));
	}

	/// <summary>Reads the plugin's services from a host that called <see cref="Register"/>.</summary>
	public void UseHost(IServiceProvider services) => _host = services;

	private ServiceProvider Build()
	{
		var services = new ServiceCollection();
		Register(services);
		return services.BuildServiceProvider();
	}

	public INotifyService Notify { get; }

	public IMediator Mediator { get; }

	public MemorySecrets Secrets { get; } = new();

	public IAccountService Accounts { get; } = Substitute.For<IAccountService>();

	public IFeedService Feeds { get; } = Substitute.For<IFeedService>();

	public IAdministrativeCapabilityService Capabilities { get; } = Substitute.For<IAdministrativeCapabilityService>();

	public IRoleRegistryService Roles { get; } = Substitute.For<IRoleRegistryService>();

	/// <summary>Objects the substitute mediator finds by number.</summary>
	public ConcurrentDictionary<int, AnySharpObject> Objects { get; } = new();

	/// <summary>Player #1, who holds every permission.</summary>
	public AnySharpObject God { get; } = Player(1, "God");

	public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

	/// <summary>A player who holds no role, known to the mediator.</summary>
	public AnySharpObject Mortal(int number, string name)
	{
		var player = Player(number, name);
		Objects[number] = player;
		return player;
	}

	/// <summary>A parser running <c>@discord</c> as <paramref name="executor"/> with these switches and arguments.</summary>
	public IMUSHCodeParser Parser(AnySharpObject executor, IEnumerable<string> switches, string? left = null, string? right = null)
	{
		var arguments = new Dictionary<string, CallState>();
		if (left is not null) arguments["0"] = new CallState(left);
		if (right is not null) arguments["1"] = new CallState(right);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(Services);
		parser.CurrentState.Returns(ParserState.RootFor(executor.Object().DBRef) with
		{
			Switches = [.. switches],
			Arguments = arguments,
		});
		return parser;
	}

	/// <summary>Runs <c>@discord</c> and returns what it gave back.</summary>
	public async Task<string> RunAsync(AnySharpObject executor, string switches, string? left = null, string? right = null)
	{
		var parsed = switches.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToUpperInvariant());
		var result = await SharpMUSH.Plugins.Discord.Commands.DiscordCommandModule.Discord(Parser(executor, parsed, left, right),
			new SharpCommandAttribute { Name = "@DISCORD" });
		return result switch
		{
			CallState state => state.Message?.ToPlainText() ?? "",
			_ => "",
		};
	}

	/// <summary>Everything told to <paramref name="who"/>, as plain text.</summary>
	public IReadOnlyList<string> ToldTo(AnySharpObject who)
		=> [.. _told.Where(told => told.To == who.Object().DBRef).Select(told => told.Text)];

	/// <summary>Waits until something told to <paramref name="who"/> contains <paramref name="text"/>.</summary>
	public async Task<string> WaitForAsync(AnySharpObject who, string text, TimeSpan? timeout = null)
	{
		using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
		while (true)
		{
			if (ToldTo(who).FirstOrDefault(told => told.Contains(text, StringComparison.Ordinal)) is { } found)
			{
				return found;
			}

			await _toldSignal.WaitAsync(cancellation.Token);
		}
	}

	private void Tell(DBRef to, SharpMessage message)
	{
		_told.Enqueue((to, message switch
		{
			MString text => text.ToPlainText(),
			string text => text,
		}));
		_toldSignal.Release();
	}

	private static AnySharpObject Player(int number, string name) => new SharpPlayer
	{
		Object = new SharpObject
		{
			Key = number,
			CreationTime = 1000 + number,
			Name = name,
			Type = "Player",
			Locks = ImmutableDictionary<string, SharpLockData>.Empty,
			Owner = new(async ct => { await ValueTask.CompletedTask; return null!; }),
			Grants = new(_ => Task.FromResult(ObjectGrants.None)),
			Powers = new(() => AsyncEnumerable.Empty<SharpPower>()),
			Attributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			AllAttributes = new(() => AsyncEnumerable.Empty<SharpAttribute>()),
			LazyAllAttributes = new(() => AsyncEnumerable.Empty<LazySharpAttribute>()),
			Flags = new(() => AsyncEnumerable.Empty<SharpObjectFlag>()),
			Parent = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Zone = new(async ct => { await ValueTask.CompletedTask; return new None(); }),
			Children = new(() => AsyncEnumerable.Empty<SharpObject>()),
		},
		Aliases = [],
		Location = new(async ct => { await ValueTask.CompletedTask; return null!; }),
		Home = new(async ct => { await ValueTask.CompletedTask; return null!; }),
		PasswordHash = string.Empty,
		PasswordSalt = null,
		Quota = 20,
	};

	public async ValueTask DisposeAsync()
	{
		if (_owned is not null) await _owned.DisposeAsync();
		await _database.DisposeAsync();
		_toldSignal.Dispose();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}
}
