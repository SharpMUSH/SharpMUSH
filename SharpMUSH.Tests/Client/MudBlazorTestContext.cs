using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client;

/// <summary>
/// Base class for Blazor component tests that use MudBlazor components.
/// Provides proper setup for MudBlazor services required for testing.
/// </summary>
public abstract class MudBlazorTestContext : BunitContext
{
	protected MudBlazorTestContext()
	{
		// Add MudBlazor services required for component rendering
		Services.AddMudServices();
		// Add localization services required for IStringLocalizer<SharedResource>
		Services.AddLocalization();
		// NavMenu (and other chrome) inject ITerminalService to gate character-scoped
		// links on connection state; a disconnected stub is enough for rendering tests.
		Services.AddSingleton(Substitute.For<ITerminalService>());
	}
}
