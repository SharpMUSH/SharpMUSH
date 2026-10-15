using System.Collections.Concurrent;
using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IAttributeIndexBuilder"/>: one loop per building index, each batch a
/// <see cref="BuildAttributeIndexBatchCommand"/> through the Mediator. Batches re-index idempotently and every value
/// write keeps a building index in step, so a loop stopped at shutdown loses nothing; the next start resumes it.
/// </summary>
public sealed class AttributeIndexBuilder(IMediator mediator, ILogger<AttributeIndexBuilder> logger)
	: IAttributeIndexBuilder, IDisposable
{
	private readonly ConcurrentDictionary<string, AttributeIndexBuild> _running = new(StringComparer.OrdinalIgnoreCase);
	private readonly CancellationTokenSource _stopping = new();

	public AttributeIndexBuild Build(string name)
	{
		var key = name.ToUpperInvariant();
		lock (_running)
		{
			if (_running.TryGetValue(key, out var running))
			{
				// The index may have been set again since the loop's last batch: one more batch finds out.
				running.Again = true;
				return running;
			}

			var completion = new TaskCompletionSource<Found<AttributeIndexBuildStep>>(TaskCreationOptions.RunContinuationsAsynchronously);
			var build = new AttributeIndexBuild(key) { Completion = completion.Task };
			_running[key] = build;
			_ = Task.Run(() => RunAsync(build, completion));
			return build;
		}
	}

	public async ValueTask ResumeAllAsync(CancellationToken cancellationToken = default)
	{
		await foreach (var entry in mediator.CreateStream(new GetAllAttributeEntriesQuery(), cancellationToken))
		{
			if (entry.Index is { Ready: false })
			{
				logger.LogInformation("Resuming the index on {Attribute} from #{ResumeAt}.", entry.Name, entry.Index.ResumeAt);
				Build(entry.Name);
			}
		}
	}

	private async Task RunAsync(AttributeIndexBuild build, TaskCompletionSource<Found<AttributeIndexBuildStep>> completion)
	{
		try
		{
			while (true)
			{
				_stopping.Token.ThrowIfCancellationRequested();
				var step = await mediator.Send(new BuildAttributeIndexBatchCommand(build.Name, IAttributeIndexBuilder.BatchSize),
					_stopping.Token);
				if (step is not AttributeIndexBuildStep batch)
				{
					Finish(build, completion, new NotFound());
					return;
				}

				build.Add(batch);
				if (batch.Ready && !TakeAgain(build))
				{
					logger.LogInformation("The index on {Attribute} is ready: {Indexed} copies on {Scanned} objects.",
						build.Name, build.Indexed, build.Scanned);
					Finish(build, completion, batch);
					return;
				}

				// Let other writes in between batches.
				await Task.Yield();
			}
		}
		catch (OperationCanceledException)
		{
			Remove(build);
			completion.TrySetCanceled();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Building the index on {Attribute} failed; it resumes at the next start.", build.Name);
			Remove(build);
			completion.TrySetException(ex);
		}
	}

	/// <summary>Whether a <see cref="Build"/> joined the loop since its last batch, clearing the request.</summary>
	private bool TakeAgain(AttributeIndexBuild build)
	{
		lock (_running)
		{
			var again = build.Again;
			build.Again = false;
			return again;
		}
	}

	private void Finish(AttributeIndexBuild build, TaskCompletionSource<Found<AttributeIndexBuildStep>> completion,
		Found<AttributeIndexBuildStep> outcome)
	{
		lock (_running)
		{
			if (build.Again)
			{
				// Joined between the last batch and here: hand the joiner a fresh loop under the same build.
				build.Again = false;
				_ = Task.Run(() => RunAsync(build, completion));
				return;
			}

			_running.TryRemove(new KeyValuePair<string, AttributeIndexBuild>(build.Name, build));
		}

		completion.TrySetResult(outcome);
	}

	private void Remove(AttributeIndexBuild build)
	{
		lock (_running)
		{
			_running.TryRemove(new KeyValuePair<string, AttributeIndexBuild>(build.Name, build));
		}
	}

	public void Dispose()
	{
		_stopping.Cancel();
		_stopping.Dispose();
	}
}
