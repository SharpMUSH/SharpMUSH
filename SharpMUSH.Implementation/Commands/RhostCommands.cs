using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>Commands RhostMUSH games rely on that PennMUSH has no counterpart for.</summary>
public partial class Commands
{
	/// <summary>RhostMUSH's <c>@eval</c>: evaluates its argument for its side effects and shows nothing.</summary>
	[SharpCommand(Name = "@EVAL", Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1,
		ParameterNames = ["expression"])]
	public ValueTask<Option<CallState>> Eval(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> ValueTask.FromResult<Option<CallState>>(CallState.Empty);

	/// <summary>
	/// RhostMUSH's <c>@pipe</c>: what the executor is told goes into one of its own attributes, added after
	/// what the attribute already holds, until <c>@pipe/off</c> writes it. <c>/tee</c> also shows it.
	/// </summary>
	[SharpCommand(Name = "@PIPE", Switches = ["ON", "OFF", "TEE", "STATUS", "QUIET"],
		Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 1, ParameterNames = ["attribute"])]
	public async ValueTask<Option<CallState>> Pipe(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var quiet = parser.CurrentState.Switches.Contains("QUIET");
		var name = parser.CurrentState.Arguments.TryGetValue("0", out var argument)
			? argument.Message!.ToPlainText().Trim().ToUpperInvariant()
			: string.Empty;

		return parser.CurrentState.Switches.Except(["QUIET"]).ToArray() switch
		{
			["ON"] => await StartPipe(executor, name, tee: false, quiet),
			["TEE"] => await StartPipe(executor, name, tee: true, quiet),
			["OFF"] => await StopPipe(executor, quiet),
			["STATUS"] => await PipeStatus(executor),
			[] => await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeNeedsSwitch)),
			_ => new CallState(ErrorMessages.Returns.TooManySwitches)
		};
	}

	/// <summary>Tells the executor about its pipe on its screen, never into the pipe itself.</summary>
	private async ValueTask<CallState> PipeNotice(AnySharpObject executor, string key, params object[] args)
	{
		var dbref = executor.Object().DBRef.Number;
		var paused = PipeOutputCapture.StopAttributePipe(dbref);
		try
		{
			await NotifyService.NotifyLocalized(executor, key, executor, args);
		}
		finally
		{
			if (paused is not null) PipeOutputCapture.StartAttributePipe(dbref, paused);
		}

		return CallState.Empty;
	}

	private async ValueTask<CallState> StartPipe(AnySharpObject executor, string name, bool tee, bool quiet)
	{
		if (PipeOutputCapture.AttributePipeOf(executor.Object().DBRef.Number) is { } running)
		{
			return await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeAlreadyFormat), running.Attribute);
		}

		if (name.Length == 0)
		{
			return await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeNeedsAttribute));
		}

		if (!await ValidateService.Valid(IValidateService.ValidationType.AttributeName, MString.Plain(name), new None()))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.BadAttrName, executor);
			return new CallState(ErrorMessages.Returns.BadAttrName);
		}

		// What the attribute already holds stays at the front, as RhostMUSH adds each line after it.
		var buffer = new PipeBuffer((int)Configuration.CurrentValue.Limit.MaxAttributeValueLength);
		if (await AttributeService.GetAttributeAsync(executor, executor, name, IAttributeService.AttributeMode.Read,
				parent: false) is SharpAttribute[] chain)
		{
			if (!await PermissionService.CanSet(executor, executor, chain))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			buffer.Append(chain.Last().Value);
		}

		// The notice goes out before the pipe starts, so it is shown rather than piped.
		if (!quiet)
		{
			await NotifyService.NotifyLocalized(executor,
				tee ? nameof(ErrorMessages.Notifications.PipeTeeFormat) : nameof(ErrorMessages.Notifications.PipeOnFormat),
				executor, name);
		}

		PipeOutputCapture.StartAttributePipe(executor.Object().DBRef.Number, new AttributePipe(name, tee, buffer));
		return CallState.Empty;
	}

	private async ValueTask<CallState> StopPipe(AnySharpObject executor, bool quiet)
	{
		if (PipeOutputCapture.StopAttributePipe(executor.Object().DBRef.Number) is not { } pipe)
		{
			return await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeNotRunning));
		}

		var text = pipe.Buffer.Text;
		if (text.Length > 0)
		{
			if (await AttributeService.SetAttributeAsync(executor, executor, pipe.Attribute, text) is Error<string> error)
			{
				await NotifyService.Notify(executor, error.Value, executor);
				return new CallState(error.Value);
			}
		}

		if (pipe.Buffer.Truncated)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PipeFullFormat), executor,
				pipe.Attribute);
		}

		if (!quiet)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PipeOffFormat), executor,
				pipe.Attribute);
		}

		return CallState.Empty;
	}

	private async ValueTask<CallState> PipeStatus(AnySharpObject executor)
		=> PipeOutputCapture.AttributePipeOf(executor.Object().DBRef.Number) switch
		{
			null => await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeNotRunning)),
			{ Tee: true } pipe => await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeStatusTeeFormat), pipe.Attribute),
			var pipe => await PipeNotice(executor, nameof(ErrorMessages.Notifications.PipeStatusOnFormat), pipe.Attribute)
		};
}
