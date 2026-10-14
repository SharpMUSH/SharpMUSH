using System.Globalization;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@secret</c>: the server's secrets (<see cref="ISecretStore"/>). With no argument, or <c>/list</c>, it
	/// lists them and where each value comes from; <c>&lt;name&gt;=&lt;value&gt;</c> stores one, replacing what was
	/// stored; <c>/clear &lt;name&gt;</c> removes the stored value. Nothing shows a value again. The command is
	/// not evaluated, so a value is stored exactly as typed. Guarded in the command, by <c>config.admin</c> or the
	/// permission a secret's declaration names, so a <c>@command/restrict</c> never opens it wider.
	/// </summary>
	[SharpCommand(Name = "@SECRET", Switches = ["LIST", "CLEAR"],
		Behavior = CB.Player | CB.EqSplit | CB.NoParse | CB.RSNoParse | CB.NoGuest | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["name", "value"])]
	public async ValueTask<Option<CallState>> Secret(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var name = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? "").Trim().ToLowerInvariant();
		var hasValue = args.ContainsKey("1");

		if (switches.Contains("LIST") || (name.Length == 0 && !hasValue && switches.Length == 0))
		{
			var listed = await SecretsManagedBy(executor);
			if (listed.Count == 0 && !await executor.Can(PortalPermission.ConfigAdmin))
			{
				return await SecretPermissionDenied(executor);
			}

			await NotifyService.Notify(executor, SecretListing(listed), executor);
			return CallState.Empty;
		}

		if (name.Length == 0 || switches.Contains("CLEAR") == hasValue)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretUsage), executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		if (!await MayManageSecret(executor, name))
		{
			return await SecretPermissionDenied(executor);
		}

		return switches.Contains("CLEAR")
			? await ClearSecret(executor, name)
			: await SetSecret(executor, name, args["1"].Message.ToPlainText());
	}

	private async ValueTask<Option<CallState>> SetSecret(AnySharpObject executor, string name, string value)
	{
		var author = new SecretAuthor(executor.Object().DBRef.ToString(), executor.Object().Name);
		switch (await Secrets.SetAsync(name, value, author))
		{
			case Error<string> error:
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretRefusedFormat), executor, error.Value);
				return new CallState(ErrorMessages.Returns.InvalidArguments);
			case SecretStatus status:
				await Audit.RecordAsync(executor, AuditActions.SecretSet, AuditTargets.Of(AuditTargetKinds.Secret, name));
				if (status.Override is { } overriddenBy)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretSetOverriddenFormat), executor,
						name, overriddenBy);
				}
				else
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretSetFormat), executor, name);
				}

				return CallState.Empty;
		}

		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> ClearSecret(AnySharpObject executor, string name)
	{
		if (await Secrets.ClearAsync(name) is not SecretStatus status)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretNotStoredFormat), executor, name);
			return new CallState(ErrorMessages.Returns.NoMatch);
		}

		await Audit.RecordAsync(executor, AuditActions.SecretClear, AuditTargets.Of(AuditTargetKinds.Secret, name));
		if (status.Override is { } overriddenBy)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretClearedOverriddenFormat), executor,
				name, overriddenBy);
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SecretClearedFormat), executor, name);
		}

		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> SecretPermissionDenied(AnySharpObject executor)
	{
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
		return new CallState(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary><c>config.admin</c> manages every secret; a declaration's <c>ManagedBy</c> permission manages that one.</summary>
	private async ValueTask<bool> MayManageSecret(AnySharpObject executor, string name)
		=> await executor.Can(PortalPermission.ConfigAdmin)
			|| (Secrets.Definitions.FirstOrDefault(definition => definition.Name == name)?.ManagedBy is { } scope
				&& await executor.Can(scope));

	private async ValueTask<List<SecretStatus>> SecretsManagedBy(AnySharpObject executor)
	{
		List<SecretStatus> managed = [];
		foreach (var status in await Secrets.ListAsync())
		{
			if (await MayManageSecret(executor, status.Name))
			{
				managed.Add(status);
			}
		}

		return managed;
	}

	/// <summary>Each secret, where its value comes from, when and by whom it was stored, and what it is for.</summary>
	private static MString SecretListing(IReadOnlyList<SecretStatus> secrets)
	{
		Block body = secrets.Count == 0
			? new TextBlock(MarkupText.Plain("No secrets."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Secret")) { Wrap = false },
					new TableColumn(MarkupText.Plain("Value")) { Min = 10 },
					new TableColumn(MarkupText.Plain("Stored")) { Min = 10 },
					new TableColumn(MarkupText.Plain("For")) { Min = 10 },
				],
				secrets.Select(secret => new[]
				{
					secret.Name,
					SecretSourceText(secret),
					secret is { SetAt: { } at, SetBy: { } by }
						? $"{at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC by {by.Name}({by.Id})"
						: "-",
					secret.Description ?? "-",
				}));

		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Secrets ({secrets.Count})"), body), 78);
	}

	private static string SecretSourceText(SecretStatus secret) => secret.Source switch
	{
		SecretSource.Stored => "stored",
		SecretSource.Environment or SecretSource.File when secret.Stored => $"{secret.Override}, over a stored value",
		SecretSource.Environment or SecretSource.File => secret.Override ?? "override",
		SecretSource.Unreadable => "unreadable: set it again",
		_ => "not set"
	};
}
