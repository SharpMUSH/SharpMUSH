using Mediator;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// <see cref="IWikiFactIndexBuilder"/>: one loop, each batch a <see cref="BuildWikiFactIndexBatchCommand"/> through the
/// Mediator. Every page write keeps a building index in step, so a loop stopped at shutdown loses nothing; the next
/// start resumes it.
/// </summary>
public sealed class WikiFactIndexBuilder(IMediator mediator, ILogger<WikiFactIndexBuilder> logger)
	: IWikiFactIndexBuilder, IDisposable
{
	private readonly Lock _gate = new();
	private readonly CancellationTokenSource _stopping = new();
	private Task _running = Task.CompletedTask;
	private bool _looping;
	private bool _again;

	public Task Idle
	{
		get { lock (_gate) return _running; }
	}

	public void Start()
	{
		lock (_gate)
		{
			// A rebuild asked for while a loop runs: the loop takes one more batch, which sees it.
			if (_looping)
			{
				_again = true;
				return;
			}

			_looping = true;
			_running = Task.Run(RunAsync);
		}
	}

	public async Task<Models.Wiki.WikiFactIndexStatus> RebuildAsync(CancellationToken cancellationToken = default)
	{
		var status = await mediator.Send(new RebuildWikiFactIndexCommand(), cancellationToken);
		Start();
		return status;
	}

	public async Task StopAsync()
	{
		await _stopping.CancelAsync();
		await Idle;
	}

	private async Task RunAsync()
	{
		try
		{
			while (true)
			{
				_stopping.Token.ThrowIfCancellationRequested();
				var status = await mediator.Send(new BuildWikiFactIndexBatchCommand(IWikiFactIndexBuilder.BatchSize), _stopping.Token);
				if (status.Ready && Finished())
				{
					logger.LogInformation("The wiki fact index is ready: {Pages} pages.", status.Pages);
					return;
				}

				// Let other writes in between batches.
				await Task.Yield();
			}
		}
		catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
		{
			lock (_gate) _looping = false;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Building the wiki fact index failed; it resumes at the next start.");
			lock (_gate) _looping = false;
		}
	}

	/// <summary>Ends the loop unless a <see cref="Start"/> came since its last batch, which then takes one more.</summary>
	private bool Finished()
	{
		lock (_gate)
		{
			if (_again)
			{
				_again = false;
				return false;
			}

			_looping = false;
			return true;
		}
	}

	public void Dispose()
	{
		_stopping.Cancel();
		_stopping.Dispose();
	}
}
