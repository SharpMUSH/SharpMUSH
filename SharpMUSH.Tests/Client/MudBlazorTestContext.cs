using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace SharpMUSH.Tests.Client;

/// <summary>
/// Base class for Blazor component tests that use MudBlazor components.
/// Provides proper setup for MudBlazor services required for testing.
/// </summary>
public abstract class MudBlazorTestContext : Bunit.TestContext
{
	protected MudBlazorTestContext()
	{
		// Add MudBlazor services required for component rendering
		Services.AddMudServices();
	}
}
