using System.Buffers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace SharpMUSH.Server.Mcp;

/// <summary>Bounds bodies before SDK JSON decoding; authorization middleware runs before this endpoint delegate.</summary>
public sealed class McpHttpAdmission : IDisposable
{
	private const int BufferSize = 8192;
	private readonly McpOptions _options;
	private readonly object _gate = new();
	private readonly CancellationTokenSource _stopping = new();
	private readonly CancellationTokenRegistration _hostStopping;
	private int _active;
	private bool _disposed;

	public McpHttpAdmission(IOptions<McpOptions> options, IHostApplicationLifetime? lifetime = null)
	{
		_options = options.Value;
		if (_options.MaxRequestBodyBytes <= 0 || _options.MaxRequestBodyBytes > long.MaxValue - BufferSize ||
			_options.MaxConcurrentRequests <= 0 || _options.BodyReadTimeout <= TimeSpan.Zero ||
			_options.BodyReadTimeout.TotalMilliseconds > uint.MaxValue - 1)
			throw new ArgumentException("MCP request budgets and body timeout must be positive and finite.", nameof(options));
		_hostStopping = lifetime?.ApplicationStopping.Register(() => _stopping.Cancel()) ?? default;
	}

	public async Task InvokeAsync(HttpContext context, RequestDelegate next)
	{
		// Kestrel's default body cap is smaller than the worst-case escaped 5 MiB envelope.
		// Set this endpoint's configured cap before any read; other endpoints retain their limits.
		if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyFeature)
			bodyFeature.MaxRequestBodySize = _options.MaxRequestBodyBytes;
		if (context.Request.ContentLength > _options.MaxRequestBodyBytes)
		{
			context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
			return;
		}
		context.RequestAborted.ThrowIfCancellationRequested();
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_active >= _options.MaxConcurrentRequests)
			{
				context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
				context.Response.Headers.RetryAfter = "1";
				return;
			}
			_active++;
		}
		try
		{
			if (HttpMethods.IsPost(context.Request.Method))
			{
				// Memory is limited to a small prefix; larger envelopes spool to request-owned temporary
				// storage, which ASP.NET disposes with the response. Only admitted bytes are replayed to JSON.
				context.Request.EnableBuffering(64 * 1024, _options.MaxRequestBodyBytes + BufferSize);
				var start = context.Request.Body.Position;
				using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
				deadline.CancelAfter(_options.BodyReadTimeout);
				var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
				try
				{
					long total = 0;
					int read;
					while ((read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, BufferSize), deadline.Token)) != 0)
					{
						total += read;
						if (total > _options.MaxRequestBodyBytes)
						{
							context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
							return;
						}
					}
					context.Request.Body.Position = start;
				}
				catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested && !_stopping.IsCancellationRequested)
				{
					context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
					return;
				}
				finally { ArrayPool<byte>.Shared.Return(buffer); }
			}
			await next(context);
		}
		finally
		{
			lock (_gate)
			{
				_active--;
				if (_disposed && _active == 0) DisposeResources();
			}
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed) return;
			_disposed = true;
			_stopping.Cancel();
			if (_active == 0) DisposeResources();
		}
	}

	private void DisposeResources()
	{
		_hostStopping.Dispose();
		_stopping.Dispose();
	}
}

public static class McpEndpointAdmissionExtensions
{
	public static TBuilder WithMcpAdmission<TBuilder>(this TBuilder endpoints) where TBuilder : IEndpointConventionBuilder
	{
		endpoints.Add(endpoint =>
		{
			var next = endpoint.RequestDelegate ?? throw new InvalidOperationException("MCP endpoint has no request delegate.");
			endpoint.RequestDelegate = context => context.RequestServices.GetRequiredService<McpHttpAdmission>().InvokeAsync(context, next);
		});
		return endpoints;
	}
}
