using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Seeds the default <c>HTTP`PROFILE`*</c> softcode onto the configured <c>http_handler</c>
/// object (#4 by default). Backend-agnostic: it sets attributes through <see cref="IAttributeService"/>,
/// so it works identically on every database provider. Idempotent — each attribute is written only
/// when absent, so admin customizations are never overwritten.
///
/// The stock handler stores profile values as <c>PROFILE`&lt;key&gt;</c> attributes on the character and
/// enforces visibility/editability itself (the engine stays opinionless). Request data arrives as
/// stack args: <c>%0</c>=method, <c>%1</c>=path, <c>%2</c>=query, <c>%3</c>=body, <c>%4</c>=viewer dbref.
/// </summary>
public class DefaultHttpHandlerBootstrapService(
	IMediator mediator,
	IAttributeService attributeService,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<DefaultHttpHandlerBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var handlerDbRef = options.CurrentValue.Database.HttpHandler;
		if (handlerDbRef is null or 0)
		{
			logger.LogDebug("No http_handler configured; skipping default profile handler seeding.");
			return;
		}

		var handlerResult = await mediator.Send(new GetObjectNodeQuery(new DBRef((int)handlerDbRef.Value, null)), cancellationToken);
		if (handlerResult.IsNone)
		{
			logger.LogWarning("Configured http_handler #{HandlerDbRef} not found; cannot seed default profile softcode.", handlerDbRef.Value);
			return;
		}

		var godResult = await mediator.Send(new GetObjectNodeQuery(new DBRef(1, null)), cancellationToken);
		if (godResult.IsNone)
		{
			logger.LogWarning("God (#1) not found; cannot seed default profile softcode.");
			return;
		}

		var handler = handlerResult.Known;
		var god = godResult.Known;
		var seeded = 0;

		foreach (var (attribute, code) in DefaultProfileHandlerSoftcode.Attributes)
		{
			var existing = await attributeService.GetAttributeAsync(
				god, handler, attribute, IAttributeService.AttributeMode.Execute, parent: false);
			if (existing.IsAttribute)
			{
				continue; // Respect admin customizations — never overwrite.
			}

			var setResult = await attributeService.SetAttributeAsync(god, handler, attribute, MModule.single(code));
			if (setResult.IsT1)
			{
				logger.LogWarning("Failed to seed {Attribute} on #{HandlerDbRef}: {Error}",
					attribute, handlerDbRef.Value, setResult.AsT1.Value);
			}
			else
			{
				seeded++;
			}
		}

		if (seeded > 0)
		{
			logger.LogInformation("Seeded {Count} default HTTP`PROFILE`* attributes on #{HandlerDbRef}.", seeded, handlerDbRef.Value);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
