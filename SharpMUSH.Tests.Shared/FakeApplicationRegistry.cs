using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Shared;

/// <summary>An in-memory <see cref="IApplicationRegistryService"/>, listed in order then slug.</summary>
public sealed class FakeApplicationRegistry : IApplicationRegistryService
{
	private readonly Dictionary<string, RegisteredApplication> _store = new(StringComparer.OrdinalIgnoreCase);

	public Task UpsertApplicationAsync(RegisteredApplication application)
	{
		_store[application.Slug] = application;
		return Task.CompletedTask;
	}

	public Task<Found<RegisteredApplication>> GetApplicationAsync(string slug) =>
		Task.FromResult(_store.TryGetValue(slug, out var app)
			? (Found<RegisteredApplication>)app
			: new NotFound());

	public Task<IReadOnlyList<RegisteredApplication>> GetApplicationsAsync() =>
		Task.FromResult<IReadOnlyList<RegisteredApplication>>(_store.Values
			.OrderBy(a => a.Order)
			.ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
			.ToList());

	public Task RemoveApplicationAsync(string slug)
	{
		_store.Remove(slug);
		return Task.CompletedTask;
	}
}

/// <summary>A plugin whose only contribution is a fixed set of applications.</summary>
public sealed class StubApplicationPlugin(params RegisteredApplication[] apps) : IPlugin, IApplicationSource
{
	public string Id => "stub-app";
	public string Version => "1.0.0";
	public IReadOnlyList<string> Dependencies => [];
	public int Priority => 0;
	public void Initialize(IServiceProvider services) { }
	public IEnumerable<RegisteredApplication> GetApplications() => apps;
}
