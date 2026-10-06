using System.Collections.Concurrent;
using Mediator;
using MarkupString.Ansi;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// The stored messages and the source switch (expanded server data). A message with no entry in
/// <see cref="Texts"/> has never been edited and shows the text SharpMUSH ships.
/// </summary>
public sealed class GameMessagesData
{
	/// <summary>Edited texts, keyed by <see cref="GameMessage"/> name, ANSI escapes and all.</summary>
	public Dictionary<string, string> Texts { get; set; } = [];

	/// <summary>The administrator's choice of source, or null to follow the Messages package.</summary>
	public GameMessageSource? Source { get; set; }
}

/// <inheritdoc cref="IGameMessageService"/>
public class GameMessageService(
	IExpandedObjectDataService serverData,
	IPackageRegistryService packages,
	IMediator mediator,
	IAttributeService attributeService,
	Lazy<IMUSHCodeParser> parser,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<GameMessageService> logger) : IGameMessageService
{
	private static readonly ConcurrentDictionary<GameMessage, string> Shipped = new();

	// One record holds every message: two saves at once must not each drop the other's change.
	private readonly SemaphoreSlim _writeLock = new(1, 1);

	/// <inheritdoc />
	public async ValueTask<MString?> RenderAsync(GameMessage message, long handle, AnySharpObject? viewer = null)
	{
		var (source, _) = await GetSourceAsync();
		if (source == GameMessageSource.Object
				&& await MessagesObjectAsync() is AnySharpObject holder
				&& await HasAttributeAsync(holder, message))
		{
			var evaluated = await EvaluateAsync(holder, message, handle, viewer);
			return evaluated is null || string.IsNullOrWhiteSpace(evaluated.Text) ? null : evaluated;
		}

		var text = await GetTextAsync(message);
		return string.IsNullOrWhiteSpace(text) ? null : AnsiEscapeParser.Parse(text);
	}

	/// <inheritdoc />
	public async ValueTask<string> GetTextAsync(GameMessage message)
		=> (await LoadAsync()).Texts.TryGetValue(message.ToString(), out var text) ? text : ShippedText(message);

	/// <inheritdoc />
	public async ValueTask<bool> IsDefaultAsync(GameMessage message)
		=> !(await LoadAsync()).Texts.ContainsKey(message.ToString());

	/// <inheritdoc />
	public async ValueTask SetTextAsync(GameMessage message, string? text)
		=> await ChangeAsync(data =>
		{
			if (text is null) data.Texts.Remove(message.ToString());
			else data.Texts[message.ToString()] = text.Replace("\r\n", "\n");
		});

	/// <inheritdoc />
	public async ValueTask<(GameMessageSource Source, bool Chosen)> GetSourceAsync()
	{
		if ((await LoadAsync()).Source is { } chosen) return (chosen, true);
		return (await packages.GetInstalledPackageAsync(GameMessages.PackageId) is InstalledPackageRecord
			? GameMessageSource.Object
			: GameMessageSource.Stored, false);
	}

	/// <inheritdoc />
	public async ValueTask SetSourceAsync(GameMessageSource? source)
		=> await ChangeAsync(data => data.Source = source);

	/// <inheritdoc />
	public async ValueTask<AnyOptionalSharpObject> MessagesObjectAsync()
	{
		var record = (await packages.GetPackageObjectsAsync(GameMessages.PackageId))
			.FirstOrDefault(o => o.Ref == GameMessages.ObjectRef);
		if (record is null || HelperFunctions.ParseDbRef(record.Objid) is not DBRef dbref) return new None();
		return await mediator.Send(new GetObjectNodeQuery(dbref));
	}

	/// <inheritdoc />
	public string ShippedText(GameMessage message) => Shipped.GetOrAdd(message, static m =>
	{
		var resource = $"SharpMUSH.Library.Messages.{m.ToString().ToLowerInvariant()}.txt";
		using var stream = typeof(IGameMessageService).Assembly.GetManifestResourceStream(resource)
			?? throw new InvalidOperationException($"Shipped message '{resource}' not found.");
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n');
	});

	private async ValueTask<GameMessagesData> LoadAsync()
		=> await serverData.GetExpandedServerDataAsync<GameMessagesData>() ?? new GameMessagesData();

	private async ValueTask ChangeAsync(Action<GameMessagesData> change)
	{
		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			change(data);
			await serverData.SetExpandedServerDataAsync(data);
		}
		finally
		{
			_writeLock.Release();
		}
	}

	private async ValueTask<bool> HasAttributeAsync(AnySharpObject holder, GameMessage message)
	{
		var name = GameMessages.AttributeName(message);
		var found = await mediator.CreateStream(new GetAttributeQuery(holder.Object().DBRef, [name])).LastOrDefaultAsync();
		return found is not null && string.Equals(found.LongName, name, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// The attribute run as the Messages object, under a <c>queue_entry_cpu_time</c> limit of its own: a message is
	/// shown outside any queue entry, and a slow one must not hold up the connection it is shown to.
	/// </summary>
	private async ValueTask<MString?> EvaluateAsync(AnySharpObject holder, GameMessage message, long handle, AnySharpObject? viewer)
	{
		var holderRef = holder.Object().DBRef;
		var attribute = GameMessages.AttributeName(message);
		using var budget = ExecutionBudget.FromMilliseconds(options.CurrentValue.Limit.QueueEntryCpuTime,
			ExecutionBudget.Current?.CancelledBy ?? CancellationToken.None);
		using var scope = budget.Enter();
		try
		{
			var context = parser.Value.FromState(ParserState.RootFor(holderRef) with
			{
				Enactor = viewer?.Object().DBRef ?? holderRef,
				Caller = holderRef,
				ExecutionBudget = budget
			});
			var text = await attributeService.EvaluateAttributeFunctionAsync(context, holder, holder, attribute,
				new Dictionary<string, CallState> { ["0"] = new(handle.ToString()) }, evalParent: false, ignorePermissions: true);
			budget.ThrowIfExceeded();
			return text;
		}
		catch (OperationCanceledException) when (budget.IsExpired)
		{
			logger.LogWarning("{Attribute} on the Messages object {Holder} ran out of time", attribute, holderRef);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogWarning(ex, "{Attribute} on the Messages object {Holder} failed", attribute, holderRef);
		}

		return null;
	}
}
