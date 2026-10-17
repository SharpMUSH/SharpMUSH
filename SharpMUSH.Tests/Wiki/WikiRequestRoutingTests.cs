using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Queries.Database.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki;

public class WikiRequestRoutingFactory : ServerWebAppFactory
{
	protected override bool UsesOwnWorld => true;
	public WikiRequestLog Requests { get; } = new();
	public WikiProviderObserver Provider { get; private set; } = null!;

	protected override Action<IServiceCollection> ConfigureTestServices => services =>
	{
		services.AddSingleton(Requests);
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(WikiRequestRecorder<,>));
		var descriptor = services.Last(item => item.ServiceType == typeof(IWikiStore));
		services.Remove(descriptor);
		services.Add(new ServiceDescriptor(typeof(IWikiStore), provider =>
		{
			var inner = (IWikiStore)(descriptor.ImplementationInstance
				?? descriptor.ImplementationFactory?.Invoke(provider)
				?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
			var proxy = DispatchProxy.Create<IWikiStore, WikiProviderObserver>();
			Provider = (WikiProviderObserver)(object)proxy;
			Provider.Inner = inner;
			return proxy;
		}, descriptor.Lifetime));
	};
}

public sealed class WikiRequestLog
{
	public ConcurrentQueue<object> Messages { get; } = new();
	public void Clear() => Messages.Clear();
}

// Registered on the actual generated Mediator. There are no test handlers or alternate dispatcher.
public sealed class WikiRequestRecorder<TMessage, TResponse>(WikiRequestLog log)
	: IPipelineBehavior<TMessage, TResponse> where TMessage : IMessage
{
	public ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next,
		CancellationToken cancellationToken)
	{
		if (typeof(TMessage).Namespace?.EndsWith(".Database.Wiki", StringComparison.Ordinal) == true)
			log.Messages.Enqueue(message);
		return next(message, cancellationToken);
	}
}

public class WikiProviderObserver : DispatchProxy
{
	public IWikiStore Inner { get; set; } = null!;
	public int RequirementReads;
	public ConcurrentDictionary<string, int> Calls { get; } = new();
	public bool FailNextRequirementsRead { get; set; }
	public bool FailNextRequirementsWriteAfterCommit { get; set; }
	private int _holdNextRead;
	public TaskCompletionSource ReadCaptured { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public TaskCompletionSource ReleaseRead { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public void HoldNextRead()
	{
		ReadCaptured = new(TaskCreationOptions.RunContinuationsAsynchronously);
		ReleaseRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
		Interlocked.Exchange(ref _holdNextRead, 1);
	}
	private async Task<IReadOnlyList<WikiRequirementSet>> Hold(Task<IReadOnlyList<WikiRequirementSet>> read)
	{
		var snapshot = await read;
		ReadCaptured.TrySetResult();
		await ReleaseRead.Task.WaitAsync(TimeSpan.FromSeconds(20));
		return snapshot;
	}
	private static async Task<Found<None>> FailAfterCommit(Task<Found<None>> write)
	{
		await write;
		throw new InvalidOperationException("Injected failure after native commit");
	}
	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
	{
		try
		{
			Calls.AddOrUpdate(targetMethod!.Name, 1, (_, count) => count + 1);
			if (targetMethod.Name == nameof(IWikiStore.GetRequirementsAsync))
			{
				Interlocked.Increment(ref RequirementReads);
				if (FailNextRequirementsRead)
				{
					FailNextRequirementsRead = false;
					return Task.FromException<IReadOnlyList<WikiRequirementSet>>(new InvalidOperationException("Injected read failure"));
				}
				var read = (Task<IReadOnlyList<WikiRequirementSet>>)targetMethod.Invoke(Inner, args)!;
				return Interlocked.Exchange(ref _holdNextRead, 0) == 1 ? Hold(read) : read;
			}
			var result = targetMethod.Invoke(Inner, args);
			if (targetMethod.Name == nameof(IWikiStore.SetRequirementsAsync) && FailNextRequirementsWriteAfterCommit)
			{
				FailNextRequirementsWriteAfterCommit = false;
				return FailAfterCommit((Task<Found<None>>)result!);
			}
			return result;
		}
		catch (TargetInvocationException exception) when (exception.InnerException is not null)
		{
			ExceptionDispatchInfo.Throw(exception.InnerException);
			throw;
		}
	}
}

[NotInParallel]
public class WikiRequestRoutingTests
{
	[ClassDataSource<WikiRequestRoutingFactory>(Shared = SharedType.PerTestSession)]
	public required WikiRequestRoutingFactory Factory { get; init; }
	private IWikiService Wiki => Factory.Services.GetRequiredService<IWikiService>();
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task HostFacadeUsesGeneratedRequestsAndPreservesStoredJourney()
	{
		Factory.Requests.Clear();
		var title = $"Routing {Guid.NewGuid():N}";
		var page = (await Wiki.CreateAsync(title, "Hello **world**", "raw author", sourceLocale: "en-US",
			categories: [" Mixed Case "])).Expect<WikiPage>();
		await Assert.That(Factory.Requests.Messages.OfType<CreateWikiPageCommand>().Count()).IsEqualTo(1);
		await Assert.That(page.AuthorDbref).IsEqualTo("raw author");
		await Assert.That(page.RenderedHtml).Contains("<strong>world</strong>");
		await Assert.That(page.PlainText).Contains("Hello world");
		var persisted = (await Factory.Provider.Inner.GetPageByIdAsync(page.Id)).Expect<WikiPage>();
		await Assert.That(persisted.MarkdownSource).IsEqualTo(page.MarkdownSource);
		await Wiki.GetBySlugAsync(title);
		await Wiki.GetByIdAsync(page.Id);
		var titlesBefore = Factory.Provider.Calls.GetValueOrDefault(nameof(IWikiStore.SetPageTitleAsync));
		await Wiki.SetTitleAsync(page.Id, " New title ");
		await Assert.That(Factory.Provider.Calls[nameof(IWikiStore.SetPageTitleAsync)] - titlesBefore).IsEqualTo(1);
		await Wiki.UpdateAsync(page.Id, "Changed *body*", "raw editor", "summary");
		await Wiki.SetMetadataAsync(page.Id, [" Mixed Case "], false);
		var stored = (await Factory.Provider.Inner.GetPageByIdAsync(page.Id)).Expect<WikiPage>();
		await Assert.That(stored.Title).IsEqualTo("New title");
		await Assert.That(stored.Slug).IsEqualTo(page.Slug);
		await Assert.That(stored.LastEditorDbref).IsEqualTo("raw editor");
		await Assert.That(stored.Published).IsFalse();
		await Assert.That(stored.Categories).Contains("mixed_case");
		await Assert.That(stored.RenderedHtml).Contains("<em>body</em>");
		var named = await Wiki.NameCategoriesAsync(["Named Category"], [], "category author", "en-US");
		await Assert.That(named.Count).IsEqualTo(1);
		await Assert.That(named[0].Namespace).IsEqualTo("category");
		await Wiki.SetCategoryPinnedAsync(" Mixed Case ", true);
		await Assert.That(await Wiki.GetPinnedCategoriesAsync()).Contains("mixed_case");
		await Wiki.GetRecentChangesAsync();
		await Wiki.GetByNamespaceAsync(WikiNamespace.Main);
		await Wiki.GetAllPagesAsync();
		var allCount = await Wiki.CountPagesAsync(null, WikiVisibility.All);
		await Assert.That(allCount > 0).IsTrue();
		await Assert.That(await Wiki.CountPagesAsync(null, WikiVisibility.All)).IsEqualTo(allCount);
		await Wiki.CountPagesByStateAsync(WikiVisibility.All);
		await Wiki.CountPagesByCategoryAsync(WikiVisibility.All);
		await Wiki.GetByCategoryAsync("Mixed Case");
		await Assert.That((await Wiki.GetRevisionsAsync(page.Id)).Count).IsEqualTo(2);
		await Wiki.GetRevisionAsync(page.Id, 1);
		var translation = (await Wiki.UpsertTranslationAsync(page.Id, "fr-ca", "Titre", "**Traduit**", "translation author",
			"translated", true, null)).Expect<WikiTranslation>();
		await Assert.That(translation.Locale).IsEqualTo("fr-CA");
		await Assert.That((await Wiki.UpsertTranslationAsync(page.Id, "fr-CA", "Conflict", "x", "other", null, true, null))
			is WikiWriteConflict).IsTrue();
		await Assert.That((await Wiki.UpsertTranslationAsync(page.Id, "en-US", "Source", "x", "other", null, true, null))
			is Error<string>).IsTrue();
		await Wiki.GetTranslationsAsync(page.Id);
		await Wiki.GetAllTranslationsAsync();
		await Wiki.GetTranslationAsync(page.Id, "fr-ca");
		await Wiki.GetRevisionsForLocaleAsync(page.Id, "fr-ca", 0, 20);
		await Wiki.GetRevisionsBeforeForLocaleAsync(page.Id, "fr-ca", 2, 20);
		await Wiki.GetRevisionForLocaleAsync(page.Id, "fr-ca", 1);
		await Wiki.DeleteTranslationAsync(page.Id, "fr-ca", "raw editor");
		await Wiki.SetRequirementsAsync(WikiRuleTarget.ForPage(page.Id), WikiRequirementSet.Protection, "protector");
		await Wiki.GetRequirementsAsync();
		await Wiki.DeleteAsync(page.Id, "raw editor");
		await Assert.That(await Wiki.GetByIdAsync(page.Id) is NotFound).IsTrue();
		await Assert.That(await Factory.Provider.Inner.GetPageByIdAsync(page.Id) is NotFound).IsTrue();
		var names = Factory.Requests.Messages.Select(message => message.GetType().Name).Distinct().ToArray();
		foreach (var expected in new[]
		{
			"GetWikiPageBySlugQuery", "GetWikiPageByIdQuery", "GetRecentWikiPagesQuery", "GetWikiPagesQuery",
			"CountWikiPagesQuery", "CountWikiPagesByStateQuery", "CountWikiPagesByCategoryQuery", "GetWikiPagesByCategoryQuery",
			"GetWikiRequirementsQuery", "GetPinnedWikiCategoriesQuery", "GetWikiRevisionsQuery", "GetWikiRevisionsBeforeQuery",
			"GetWikiRevisionQuery", "GetWikiTranslationSummariesQuery", "GetWikiTranslationsQuery", "GetWikiTranslationQuery",
			"CreateWikiPageCommand", "UpdateWikiPageBodyCommand", "DeleteWikiPageCommand", "SetWikiRequirementsCommand",
			"SetWikiPageTitleCommand", "SetWikiPageMetadataCommand", "SetWikiCategoryPinnedCommand", "WriteWikiTranslationCommand",
			"DeleteWikiTranslationCommand"
		}) await Assert.That(names).Contains(expected);
	}

	[Test]
	public async Task WarmFacadeRequirementsFollowDirectRequestWritesAndDeletion()
	{
		var page = (await Wiki.CreateAsync($"Rules {Guid.NewGuid():N}", "body", "author")).Expect<WikiPage>();
		await Wiki.GetRequirementsAsync();
		foreach (var target in new[] { WikiRuleTarget.ForNamespace("help"), WikiRuleTarget.ForCategory("routing"), WikiRuleTarget.ForPage(page.Id) })
		{
			await Mediator.Send(new SetWikiRequirementsCommand(new WikiRequirementSet(target,
				WikiRequirementSet.Protection, "direct editor", DateTimeOffset.UtcNow)));
			await Assert.That((await Wiki.GetRequirementsAsync()).For(target)?.UpdatedBy).IsEqualTo("direct editor");
			await Assert.That((await Mediator.Send(new GetWikiRequirementsQuery())).Any(set => set.Target == target)).IsTrue();
		}
		var reads = Factory.Provider.RequirementReads;
		var wrapper = await Wiki.GetRequirementsAsync();
		var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
		var rebuilt = await Wiki.GetRequirementsAsync();
		var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
		await Assert.That(ReferenceEquals(wrapper, rebuilt)).IsFalse();
		await Assert.That(rebuilt.Sets.Count).IsEqualTo(wrapper.Sets.Count);
		Console.WriteLine($"Requirements wrapper: {wrapper.Sets.Count} rules, {allocated} process-wide allocation bytes including query infrastructure (observational, not a benchmark); raw provider reads unchanged.");
		await Wiki.GetRequirementsAsync();
		await Mediator.Send(new GetWikiRequirementsQuery());
		await Assert.That(Factory.Provider.RequirementReads).IsEqualTo(reads);
		await Mediator.Send(new DeleteWikiPageCommand(page.Id));
		await Assert.That((await Wiki.GetRequirementsAsync()).HasPageRules(page.Id)).IsFalse();
		await Assert.That((await Mediator.Send(new GetWikiRequirementsQuery())).Any(set => set.Target == WikiRuleTarget.ForPage(page.Id))).IsFalse();
	}

	[Test]
	public async Task OldFactoryCannotPoisonRequirementsAfterDirectCommit()
	{
		var target = WikiRuleTarget.ForCategory($"race-{Guid.NewGuid():N}");
		await Mediator.Send(new SetWikiRequirementsCommand(new WikiRequirementSet(target,
			new Dictionary<WikiAction, IReadOnlyList<string>>(), "clear", DateTimeOffset.UtcNow)));
		Factory.Provider.HoldNextRead();
		var producingRead = Mediator.Send(new GetWikiRequirementsQuery()).AsTask();
		await WithReleasedReadAsync(producingRead, () => Factory.Provider.ReleaseRead.TrySetResult(), async () =>
		{
			await Factory.Provider.ReadCaptured.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Mediator.Send(new SetWikiRequirementsCommand(new WikiRequirementSet(target,
				WikiRequirementSet.Protection, "committed", DateTimeOffset.UtcNow)));
		});
		// The producing caller may receive its pre-write snapshot once. Later callers must refetch.
		await Assert.That((await Mediator.Send(new GetWikiRequirementsQuery())).Single(set => set.Target == target).UpdatedBy)
			.IsEqualTo("committed");
		await Assert.That((await Wiki.GetRequirementsAsync()).For(target)?.UpdatedBy).IsEqualTo("committed");
	}

	private static async Task WithReleasedReadAsync(Task producingRead, Action release, Func<Task> action)
	{
		Exception? primaryFailure = null;
		try { await action(); }
		catch (Exception exception)
		{
			primaryFailure = exception;
			throw;
		}
		finally
		{
			release();
			try { await producingRead; }
			catch when (primaryFailure is not null)
			{
				// The read is observed and drained, but its cleanup failure must not replace the
				// timeout or command failure that caused this test to exit.
			}
		}
	}

	[Test]
	public async Task FailedRaceActionStillDrainsReadAndPreservesPrimaryFailure()
	{
		var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var readDrained = false;
		var primary = new TimeoutException("primary test failure");
		async Task ProducingRead()
		{
			await released.Task;
			readDrained = true;
			throw new InvalidOperationException("secondary read failure");
		}
		var read = ProducingRead();
		Exception? observed = null;
		try
		{
			await WithReleasedReadAsync(read, () => released.TrySetResult(), () => Task.FromException(primary));
		}
		catch (Exception exception) { observed = exception; }
		await Assert.That(observed).IsSameReferenceAs(primary);
		await Assert.That(readDrained).IsTrue();
		await Assert.That(read.IsCompleted).IsTrue();
	}

	[Test]
	public async Task CommittedWriteFailureInvalidatesAndExpiredPolicyNeverFallsBack()
	{
		var target = WikiRuleTarget.ForCategory($"failure-{Guid.NewGuid():N}");
		await Wiki.GetRequirementsAsync();
		Factory.Provider.FailNextRequirementsWriteAfterCommit = true;
		var writeFailed = false;
		try
		{
			await Mediator.Send(new SetWikiRequirementsCommand(new WikiRequirementSet(target,
				WikiRequirementSet.Protection, "committed despite failure", DateTimeOffset.UtcNow)));
		}
		catch (InvalidOperationException exception) when (exception.Message == "Injected failure after native commit")
		{
			writeFailed = true;
		}
		await Assert.That(writeFailed).IsTrue();
		Factory.Provider.FailNextRequirementsRead = true;
		var readFailed = false;
		try { await Mediator.Send(new GetWikiRequirementsQuery()); }
		catch (InvalidOperationException exception) when (exception.Message == "Injected read failure") { readFailed = true; }
		await Assert.That(readFailed).IsTrue();
		await Assert.That((await Mediator.Send(new GetWikiRequirementsQuery())).Single(set => set.Target == target).UpdatedBy)
			.IsEqualTo("committed despite failure");
		await Assert.That((await Wiki.GetRequirementsAsync()).For(target)?.UpdatedBy).IsEqualTo("committed despite failure");
	}

	[Test]
	public async Task ParallelResolutionKeepsSingleFacadeAndProviderIdentity()
	{
		var resolved = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
			Factory.Services.GetRequiredService<IWikiService>())));
		await Assert.That(resolved.All(item => ReferenceEquals(item, Wiki))).IsTrue();
		await Assert.That(Factory.Services.GetRequiredService<IWikiStore>()).IsSameReferenceAs(Factory.Provider);
		await Assert.That(ReferenceEquals(Factory.Provider.Inner, Factory.Services.GetRequiredService<ISharpDatabase>())).IsTrue();
		await Task.WhenAll(resolved.Select(item => item.GetRecentChangesAsync(1)));
	}
}
