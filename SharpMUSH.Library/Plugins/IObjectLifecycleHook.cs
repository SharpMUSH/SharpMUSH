using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Phase 2b engine-extension hook: react to object lifecycle. A plugin entry type may implement this to
/// observe objects being created (<c>@create</c>) and destroyed (<c>@destroy</c>/<c>@recycle</c>). The
/// engine invokes these at the building-command seams, alongside the softcode <c>OBJECT`CREATE</c> event.
///
/// <para>A plugin implements any subset (the defaults are no-ops). <see cref="OnDestroyingAsync"/> fires
/// <i>before</i> the object is removed from the database, so a plugin can still read it. Resolve engine
/// services at call time via <c>parser.ServiceProvider.GetRequiredService&lt;T&gt;()</c> where a parser is
/// available, or from the captured root provider.</para>
/// </summary>
public interface IObjectLifecycleHook
{
	/// <summary>An object was created. <paramref name="creator"/> is the executor that ran <c>@create</c>. Default is a no-op.</summary>
	ValueTask OnCreatedAsync(DBRef obj, DBRef creator) => ValueTask.CompletedTask;

	/// <summary>An object is about to be destroyed (still present in the DB). Default is a no-op.</summary>
	ValueTask OnDestroyingAsync(DBRef obj) => ValueTask.CompletedTask;
}
