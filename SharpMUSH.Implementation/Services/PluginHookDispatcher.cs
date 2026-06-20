using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Implementation.Services;

/// <summary>
/// Phase 2b hook dispatcher. The engine calls this at its command and object-lifecycle seams; it reads the
/// hook buckets the <see cref="PluginCatalog"/> collected and fans each seam out to the registered plugin
/// hooks in load order. Every plugin call is isolated in try/catch and logged, so a misbehaving hook can
/// never abort dispatch. When no hooks are registered the methods are cheap no-ops, so the seams do not
/// change normal command/object flow.
///
/// <para>Connection hooks are deliberately not routed here — they are registered as
/// <c>IConnectionService.ListenState</c> listeners at boot (see the host's plugin bootstrap), riding the
/// engine's existing connection-state mechanism.</para>
/// </summary>
public sealed class PluginHookDispatcher(PluginCatalog catalog, ILogger<PluginHookDispatcher> logger)
	: IPluginHookDispatcher
{
	public bool HasCommandInterceptors => catalog.CommandInterceptors.Count > 0;

	public async ValueTask<bool> CommandBeforeAsync(IMUSHCodeParser parser, string command)
	{
		foreach (var interceptor in catalog.CommandInterceptors)
		{
			try
			{
				if (!await interceptor.BeforeAsync(parser, command))
				{
					// A veto short-circuits the rest of the before-chain (and the body/override).
					return false;
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Command interceptor {Interceptor} threw in BeforeAsync for '{Command}'; treating as no veto.",
					interceptor.GetType().FullName, command);
			}
		}

		return true;
	}

	public async ValueTask<Option<CallState>?> CommandTryOverrideAsync(IMUSHCodeParser parser, string command)
	{
		foreach (var interceptor in catalog.CommandInterceptors)
		{
			try
			{
				var result = await interceptor.TryOverrideAsync(parser, command);
				if (result is not null)
				{
					// First non-null override wins and short-circuits the built-in.
					return result;
				}
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Command interceptor {Interceptor} threw in TryOverrideAsync for '{Command}'; treating as no override.",
					interceptor.GetType().FullName, command);
			}
		}

		return null;
	}

	public async ValueTask CommandAfterAsync(IMUSHCodeParser parser, string command)
	{
		foreach (var interceptor in catalog.CommandInterceptors)
		{
			try
			{
				await interceptor.AfterAsync(parser, command);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Command interceptor {Interceptor} threw in AfterAsync for '{Command}'; ignoring.",
					interceptor.GetType().FullName, command);
			}
		}
	}

	public async ValueTask ObjectCreatedAsync(DBRef obj, DBRef creator)
	{
		foreach (var hook in catalog.ObjectLifecycleHooks)
		{
			try
			{
				await hook.OnCreatedAsync(obj, creator);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Object lifecycle hook {Hook} threw in OnCreatedAsync for {Obj}; ignoring.",
					hook.GetType().FullName, obj);
			}
		}
	}

	public async ValueTask ObjectDestroyingAsync(DBRef obj)
	{
		foreach (var hook in catalog.ObjectLifecycleHooks)
		{
			try
			{
				await hook.OnDestroyingAsync(obj);
			}
			catch (Exception ex)
			{
				logger.LogError(ex,
					"Object lifecycle hook {Hook} threw in OnDestroyingAsync for {Obj}; ignoring.",
					hook.GetType().FullName, obj);
			}
		}
	}
}
