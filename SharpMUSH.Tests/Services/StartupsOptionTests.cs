using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>PennMUSH's <c>startups</c> (<c>src/game.c:706</c>): with it off, the boot @STARTUP pass does not run.</summary>
public class StartupsOptionTests
{
	private sealed record FixedOptions(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;

	[Test]
	public async Task StartupsOff_SkipsTheBootPass()
	{
		var defaults = OptionsService.Default();
		var mediator = Substitute.For<IMediator>();
		var service = new StartupAttributeBootstrapService(mediator, Substitute.For<IAttributeService>(),
			Substitute.For<IMUSHCodeParser>(),
			new FixedOptions(defaults with { Attribute = defaults.Attribute with { Startups = false } }),
			NullLogger<StartupAttributeBootstrapService>.Instance);

		await service.StartAsync(CancellationToken.None);

		await Assert.That(mediator.ReceivedCalls()).IsEmpty();
	}
}
