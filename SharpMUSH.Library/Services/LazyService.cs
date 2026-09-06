using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Registered as the open generic <c>Lazy&lt;&gt;</c>, so a constructor can take <c>Lazy&lt;T&gt;</c>
/// for a service that is only reachable through a dependency cycle; resolution happens on first use,
/// after the container is built.
/// </summary>
public sealed class LazyService<T>(IServiceProvider provider) : Lazy<T>(provider.GetRequiredService<T>) where T : notnull;
