using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Email;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>TinyMUX's <c>@email</c>: a plain-text email through the game's SMTP server.</summary>
public partial class Commands
{
	/// <summary>
	/// <c>@email &lt;address&gt;[/&lt;subject&gt;]=&lt;body&gt;</c>. The email goes out off the queue, and the executor hears
	/// whether it was sent. Wizards only; off until <c>email_server</c> and <c>email_from</c> are set.
	/// </summary>
	[SharpCommand(Name = "@DIALECT_EMAIL", Switches = [], Behavior = CB.Default | CB.EqSplit,
		CommandLock = "FLAG^WIZARD", MinArgs = 0, MaxArgs = 2, ParameterNames = ["address/subject", "body"])]
	public async ValueTask<Option<CallState>> DialectEmail(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var left = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		var body = args.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty;
		var net = Configuration.CurrentValue.Net;

		if (net.EmailServer is not { Length: > 0 } host || net.EmailFrom is not { Length: > 0 } from)
			return await EmailNotice(executor, "@email is not set up: see email_server and email_from in @config net.");
		var slash = left.IndexOf('/');
		var address = (slash < 0 ? left : left[..slash]).Trim();
		if (address.Length == 0)
			return await EmailNotice(executor, "Name the address: @email <address>[/<subject>]=<body>.");
		if (body.Trim().Length == 0)
			return await EmailNotice(executor, "Give the email a body.");

		var subject = slash < 0 ? string.Empty : left[(slash + 1)..].Trim();
		if (subject.Length == 0) subject = net.EmailSubject is { Length: > 0 } fallback ? fallback : net.MudName;
		var server = new EmailServer(host, (int)net.EmailPort, net.EmailUsername is { Length: > 0 } user ? user : null, from,
			net.EmailFromName is { Length: > 0 } name ? name : net.MudName, (int)net.EmailPerHour);
		var message = new EmailMessage(address, subject, body);

		// Sent off the queue, as the server may take seconds to answer; the executor hears the outcome as a queued entry.
		var admission = await Mediator.Send(new AdmitWorkAfterRequest(async () =>
		{
			var sent = await Email.SendAsync(server, message, ExecutionBudget.CurrentToken);
			var notice = sent switch
			{
				Success => $"@email: sent to {address}.",
				Error<string> error => $"@email: not sent. {error.Value}"
			};
			return async () =>
			{
				await NotifyService.Notify(executor, notice, executor);
				return null;
			};
		}, $"email:{address}", executor.Object().DBRef), ExecutionBudget.CurrentToken);

		if (admission.Accepted) return CallState.Empty;
		// Capacity and shutdown refusals are already reported by the scheduler.
		if (admission.Reason is QueueRejectionReason.InvalidTarget or QueueRejectionReason.AlreadyReleased)
			await NotifyService.Notify(executor, admission.Error, executor);
		return new CallState(admission.Error);
	}

	private async ValueTask<Option<CallState>> EmailNotice(AnySharpObject executor, string message)
	{
		await NotifyService.Notify(executor, message, executor);
		return new CallState($"#-1 {message}");
	}
}
