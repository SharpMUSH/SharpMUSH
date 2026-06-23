using System.Collections.Concurrent;
using System.Reflection;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Loads a plugin-shipped compiled Blazor component into the running WASM client at runtime: fetches the UI
/// assembly bytes from the server (gate-guarded + hash-verified server-side), <c>Assembly.Load(bytes)</c>s
/// them, and resolves the requested component <see cref="Type"/> to hand to <c>&lt;DynamicComponent&gt;</c>.
///
/// <para><b>Phase-10 boundary:</b> this service references ZERO plugin types — it renders purely by reflection
/// on a Type name. <b>No unload:</b> a loaded assembly lingers in the runtime until the next hard page refresh
/// (the forced refresh on plugin unload is what reclaims it); so loaded assemblies are cached by URL. <b>Gate
/// aware:</b> a non-success fetch (e.g. the server 404s the endpoint because <c>allow_browser_code</c> is off,
/// or the bytes fail hash verification) is a no-op that returns null — the caller falls back to a notice.</para>
/// </summary>
public sealed class PluginComponentLoader(IHttpClientFactory httpClientFactory, ILogger<PluginComponentLoader> logger)
{
	// Cache by assembly URL: the bytes are immutable for a given URL and an assembly cannot be unloaded, so a
	// second request for the same URL reuses the already-loaded Assembly.
	private readonly ConcurrentDictionary<string, Assembly> _assemblies = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Resolve the component <see cref="Type"/> named <paramref name="componentTypeName"/> from the assembly at
	/// <paramref name="assemblyUrl"/>, fetching+loading the assembly once and caching it. Returns null when the
	/// assembly cannot be fetched/loaded (gate off, network/verification failure) or the type is not found.
	/// </summary>
	public async Task<Type?> ResolveComponentAsync(string assemblyUrl, string componentTypeName)
	{
		if (string.IsNullOrWhiteSpace(assemblyUrl) || string.IsNullOrWhiteSpace(componentTypeName))
		{
			return null;
		}

		var assembly = await GetOrLoadAssemblyAsync(assemblyUrl);
		if (assembly is null)
		{
			return null;
		}

		var type = assembly.GetType(componentTypeName, throwOnError: false, ignoreCase: false);
		if (type is null)
		{
			logger.LogWarning("Plugin component type '{Type}' not found in assembly from {Url}.",
				componentTypeName, assemblyUrl);
		}

		return type;
	}

	private async Task<Assembly?> GetOrLoadAssemblyAsync(string assemblyUrl)
	{
		if (_assemblies.TryGetValue(assemblyUrl, out var cached))
		{
			return cached;
		}

		byte[] bytes;
		try
		{
			var http = httpClientFactory.CreateClient("api");
			var response = await http.GetAsync(assemblyUrl);
			if (!response.IsSuccessStatusCode)
			{
				// Gate off (404) or unverified/missing assembly: refuse to load, defense in depth.
				logger.LogWarning("Plugin UI assembly fetch from {Url} returned {Status}; not loading.",
					assemblyUrl, (int)response.StatusCode);
				return null;
			}

			bytes = await response.Content.ReadAsByteArrayAsync();
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
		{
			logger.LogWarning(ex, "Failed to fetch plugin UI assembly from {Url}.", assemblyUrl);
			return null;
		}

		try
		{
			// In-browser Mono-WASM Assembly.Load(byte[]). This path is runtime-only (no unload possible); the
			// resolve+render seam above is what bUnit exercises with an already-loaded test assembly.
			var assembly = Assembly.Load(bytes);
			return _assemblies.GetOrAdd(assemblyUrl, assembly);
		}
		catch (Exception ex) when (ex is BadImageFormatException or FileLoadException)
		{
			logger.LogWarning(ex, "Failed to load plugin UI assembly bytes from {Url}.", assemblyUrl);
			return null;
		}
	}
}
