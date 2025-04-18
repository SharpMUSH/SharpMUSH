﻿using Antlr4.Runtime.Tree;
using DotNext.Collections.Generic;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Definitions;
using static SharpMUSHParser;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	private static readonly
		Dictionary<string, (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>
		CommandLibrary = [];

	private static readonly Dictionary<string, (MethodInfo Method, SharpCommandAttribute Attribute)>
		KnownBuiltInCommands =
			typeof(Commands)
				.GetMethods()
				.Select(m => (Method: m,
					Attribute: m.GetCustomAttribute<SharpCommandAttribute>(false)))
				.Where(x => x.Attribute is not null)
				.SelectMany(y =>
					(Configurable.CommandAliases.TryGetValue(y.Attribute!.Name, out var aliases)
						? aliases.Select(alias =>
							new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(alias,
								(y.Method, y.Attribute!)))
						: [])
					.Append(new KeyValuePair<string, (MethodInfo Method, SharpCommandAttribute Attribute)>(y.Attribute.Name,
						(y.Method, y.Attribute!))))
				.ToDictionary();

	static Commands()
		=> CommandLibrary.AddAll(KnownBuiltInCommands.Select(knownCommand =>
			new KeyValuePair<string, (SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>>
				Function)>(
				key: knownCommand.Key,
				value: (knownCommand.Value.Attribute,
					async p => await (ValueTask<Option<CallState>>)knownCommand.Value.Method.Invoke(null,
						[p, knownCommand.Value.Attribute])!))));

	/// <summary>
	/// Evaluates the command, with the parser info given.
	/// </summary>
	/// <remarks>
	/// Call State is expected to be empty on return. 
	/// But if one wanted to implement an @pipe command that can pass a result from say, an @dig command, 
	/// there would be a need for some way of passing on secondary data.
	/// </remarks>
	/// <param name="logger">Logger</param>
	/// <param name="parser">Parser with state.</param>
	/// <param name="source">Original string</param>
	/// <param name="context">Command Context</param>
	/// <param name="visitChildren">Parser function to visit children.</param>
	/// <returns>An empty Call State</returns>
	public static async ValueTask<Option<CallState>> EvaluateCommands(ILogger logger, IMUSHCodeParser parser,
		MString source,
		CommandContext context,
		Func<IRuleNode, ValueTask<CallState?>> visitChildren)
	{
		try
		{
			var firstCommandMatch = context.evaluationString();

			if (firstCommandMatch?.SourceInterval.Length is null or 0)
				return new None();

			var command = firstCommandMatch.GetText();

			var spaceIndex = command.AsSpan().IndexOf(' ');
			if (spaceIndex != -1)
			{
				command = command[..spaceIndex];
			}

			if (parser.CurrentState.Handle is not null && command == "IDLE")
			{
				parser.ConnectionService.Update(parser.CurrentState.Handle.Value, "LastConnectionSignal",
					DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
			}

			// Step 1: Check if it's a SOCKET command
			// TODO: Optimize
			var socketCommandPattern = CommandLibrary.Where(x
				=> parser.CurrentState.Handle is not null
				   && x.Key.Equals(command, StringComparison.CurrentCultureIgnoreCase)
				   && x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SOCKET)).ToList();

			if (socketCommandPattern.Any() &&
			    CommandLibrary.TryGetValue(command.ToUpper(), out var librarySocketCommandDefinition))
			{
				return await HandleSocketCommandPattern(parser, source, context, command, socketCommandPattern,
					librarySocketCommandDefinition);
			}

			if (parser.CurrentState.Executor is null && parser.CurrentState.Handle is not null)
			{
				await parser.NotifyService.Notify(parser.CurrentState.Handle.Value, "No such command available at login.");
				return new None();
			}

			// Step2a: Check for the channel single-token command.

			// TODO: Better channel name matching within the channel helper.
			if (command[..1] == parser.Configuration.CurrentValue.Chat.ChatTokenAlias.ToString())
			{
				var channels = await parser.Mediator.Send(new GetChannelListQuery());
				var check = command[1..];

				var channel = channels.FirstOrDefault(x =>
					x.Name.ToPlainText().StartsWith(check, StringComparison.CurrentCultureIgnoreCase));

				if (channel is not null && !context.evaluationString().IsEmpty)
				{
					return await HandleChannelCommand(parser, channel, context, source);
				}
			}

			// Step 2b: Check for a single-token command
			// TODO: Optimize
			var singleTokenCommandPattern = CommandLibrary.Where(x
				=> x.Key.Equals(command[..1], StringComparison.CurrentCultureIgnoreCase) &&
				   x.Value.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.SingleToken)).ToList();

			if (singleTokenCommandPattern.Count != 0)
			{
				return await HandleSingleTokenCommandPattern(parser, source, context, command, singleTokenCommandPattern);
			}

			var executorObject = (await parser.CurrentState.ExecutorObject(parser.Mediator)).WithoutNone();
			// Step 3: Check exit Aliases
			if (executorObject.IsContent)
			{
				var locate = await parser.LocateService.Locate(
					parser,
					executorObject,
					executorObject,
					command,
					LocateFlags.ExitsInTheRoomOfLooker
					| LocateFlags.EnglishStyleMatching
					| LocateFlags.ExitsPreference
					| LocateFlags.OnlyMatchTypePreference
					| LocateFlags.FailIfNotPreferred);

				if (locate.IsExit)
				{
					var exit = locate.AsExit;
					return await HandleGoCommandPattern(parser, exit);
				}
			}

			// Step 4: Check if we are setting an attribute: &... -- we're just treating this as a Single Token Command for now.
			// Who would rely on a room alias being & anyway?
			// Step 5: Check @COMMAND in command library

			// TODO: Optimize
			// TODO: Get the Switches and send them along as a list of items!
			var slashIndex = command.AsSpan().IndexOf('/');
			var rootCommand =
				command[..(slashIndex > -1 ? slashIndex : command.Length)];
			var swtch = command[(slashIndex > -1 ? slashIndex : command.Length)..];
			var switches = swtch.Split('/').Where(s => !string.IsNullOrWhiteSpace(s));

			if (CommandLibrary.TryGetValue(rootCommand.ToUpper(), out var libraryCommandDefinition)
			    && !rootCommand.Equals("HUH_COMMAND", StringComparison.CurrentCultureIgnoreCase))
			{
				return await HandleInternalCommandPattern(parser, source, context, rootCommand, switches,
					libraryCommandDefinition);
			}

			// Step 6: Check @attribute setting
			// Step 7: Enter Aliases
			// Step 8: Leave Aliases

			// Step 9: User Defined Commands nearby
			// -- This is going to be a very important place to Cache the commands.
			// A caching strategy is going to be reliant on the Attribute Service.
			// Optimistic that the command still exists, until we try and it no longer does?
			// What's the best way to retrieve the Regex or Wildcard pattern and transform it? 
			// It needs to take an area to search in. So this is definitely its own service.
			var nearbyObjects = await parser.Mediator.Send(new GetNearbyObjectsQuery(executorObject.Object().DBRef));

			var userDefinedCommandMatches = await parser.CommandDiscoveryService.MatchUserDefinedCommand(
				parser,
				nearbyObjects,
				source);

			if (userDefinedCommandMatches.IsSome())
			{
				return await HandleUserDefinedCommand(parser, userDefinedCommandMatches.AsValue());
			}

			// Step 10: Zone Exit Name and Aliases
			// Step 11: Zone Master User Defined Commands
			// Step 12: User Defined commands on the location itself.
			if (executorObject.IsContent)
			{
				var userDefinedCommandMatchesOnLocation = await parser.CommandDiscoveryService.MatchUserDefinedCommand(
					parser,
					[(await executorObject.AsContent.Location()).WithExitOption()],
					source);

				if (userDefinedCommandMatchesOnLocation.IsSome())
				{
					return await HandleUserDefinedCommand(parser, userDefinedCommandMatchesOnLocation.AsValue());
				}
			}

			// Step 13: User defined commands on the player's personal zone.
			// Step 14: Global Exits
			// Step 15: Global User-defined commands
			var goConfig = parser.Configuration.CurrentValue.Database.MasterRoom;
			var maybeGlobalObject = await parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(Convert.ToInt32(goConfig))));
			var globalObject = maybeGlobalObject.Known();
			var globalObjectContent = (await globalObject.AsContainer.Content(parser))
				.Select(x => x.WithRoomOption());

			var userDefinedCommandMatchesOnGlobal = await parser.CommandDiscoveryService.MatchUserDefinedCommand(
				parser,
				[globalObject, .. globalObjectContent],
				source);

			if (userDefinedCommandMatchesOnGlobal.IsSome())
			{
				return await HandleUserDefinedCommand(parser, userDefinedCommandMatchesOnGlobal.AsValue());
			}

			// Step 16: HUH_COMMAND is run
			var newParser = parser.Push(parser.CurrentState with
			{
				Command = "HUH_COMMAND",
				Arguments = [],
				Function = null
			});

			var huhCommand = await CommandLibrary["HUH_COMMAND"].Function.Invoke(newParser);

			return huhCommand;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, nameof(EvaluateCommands));
			return CallState.Empty;
		}
	}

	private static async Task<Option<CallState>> HandleChannelCommand(IMUSHCodeParser parser, SharpChannel channel,
		CommandContext context, MString source)
	{
		var rest = MModule.substring(
			context.evaluationString().Start.StartIndex,
			context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1,
			source);

		var chatParser = parser.Push(parser.CurrentState with
		{
			Command = "@CHAT",
			Arguments = new(new Dictionary<string, CallState>
			{
				{ "0", new CallState(channel.Name) },
				{ "1", new CallState(rest) }
			})
		});

		return await CommandLibrary["@CHAT"].Function.Invoke(chatParser);
	}

	private static async Task<Option<CallState>> HandleUserDefinedCommand(
		IMUSHCodeParser parser,
		IEnumerable<(AnySharpObject Obj, SharpAttribute Attr, Dictionary<string, CallState> Arguments)> matches)
	{
		// Step 1: Validate if the command can be evaluated (locks)
		foreach (var (obj, attr, arguments) in matches)
		{
			var newParser = parser.Push(parser.CurrentState with
			{
				CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.Name),
				Arguments = new(arguments),
				Function = null,
				Executor = obj.Object().DBRef
			});

			await newParser.CommandListParse(MModule.substring(
				attr.CommandListIndex!.Value,
				MModule.getLength(attr.Value) - attr.CommandListIndex!.Value,
				attr.Value));
		}

		return CallState.Empty;
	}

	private static async ValueTask<Option<CallState>> HandleGoCommandPattern(IMUSHCodeParser parser, SharpExit exit)
	{
		var newParser = parser.Push(parser.CurrentState with
		{
			Command = "GOTO",
			Arguments = new(new Dictionary<string, CallState>
			{
				{ "0", new CallState(exit.Object.DBRef.ToString(), 0) }
			}),
			Function = null
		});

		return await CommandLibrary.Single(x => x.Key == "GOTO").Value.Function.Invoke(newParser);
	}

	private static async ValueTask<Option<CallState>> HandleInternalCommandPattern(IMUSHCodeParser parser, MString source,
		CommandContext context, string rootCommand, IEnumerable<string> switches,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var arguments = await ArgumentSplit(parser, source, context, libraryCommandDefinition);

		var newParser = parser.Push(parser.CurrentState with
		{
			Command = rootCommand,
			Switches = switches.Select(x => x.ToUpper()),
			Arguments = new(arguments.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value))
				.ToDictionary()),
			Function = null
		});

		return await libraryCommandDefinition.Function.Invoke(newParser);
	}

	private static async ValueTask<Option<CallState>> HandleSocketCommandPattern(IMUSHCodeParser parser, MString source,
		CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (SharpCommandAttribute Attribute,
			Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>> socketCommandPattern,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			librarySocketCommandDefinition)
	{
		var arguments = await ArgumentSplit(parser, source, context, librarySocketCommandDefinition);

		var newParser = parser.Push(parser.CurrentState with
		{
			Command = command,
			Arguments = new(arguments.Select((value, i) => new KeyValuePair<string, CallState>(i.ToString(), value))
				.ToDictionary()),
			Function = null
		});

		// Run as Socket Command. 
		return await socketCommandPattern.First().Value.Function.Invoke(newParser);
	}

	private static async ValueTask<Option<CallState>> HandleSingleTokenCommandPattern(IMUSHCodeParser parser,
		MString source, CommandContext context, string command,
		IEnumerable<KeyValuePair<string, (SharpCommandAttribute Attribute,
			Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)>> singleTokenCommandPattern)
	{
		var singleRootCommand = command[..1];
		var rest = command[1..];
		var singleLibraryCommandDefinition = singleTokenCommandPattern.Single().Value;
		// TODO: Should Single Commands split? - Getting errors out of this.
		var arguments = await ArgumentSplit(parser, source, context, singleLibraryCommandDefinition);

		var newParser = parser.Push(
			parser.CurrentState with
			{
				Command = singleRootCommand,
				Arguments = new(ImmutableDictionary<string, CallState>.Empty
					.Add("0", new CallState(rest))
					.AddRange(arguments.Select((value, i) => new KeyValuePair<string, CallState>((i + 1).ToString(), value)))
					.ToDictionary()),
				Function = null
			}
		);

		return await singleLibraryCommandDefinition.Function.Invoke(newParser);
	}

	private static async ValueTask<List<CallState>> ArgumentSplit(IMUSHCodeParser parser, MString source,
		CommandContext context,
		(SharpCommandAttribute Attribute, Func<IMUSHCodeParser, ValueTask<Option<CallState>>> Function)
			libraryCommandDefinition)
	{
		var argCallState = CallState.EmptyArgument;
		var behavior = libraryCommandDefinition.Attribute.Behavior;

		// Do not parse the argument splitting.
		var newNoParseParser = parser.Push(parser.CurrentState with { ParseMode = ParseMode.NoParse });
		var realSubtext = MModule.substring(
			context.evaluationString().Start.StartIndex,
			context.evaluationString().Stop.StopIndex - context.evaluationString().Start.StartIndex + 1,
			source);
		var spaceInContext = MModule.indexOf(realSubtext, MModule.single(" "));

		// command (space) argument(s)
		if (spaceInContext != -1)
		{
			var remainder =
				MModule.substring(spaceInContext + 1, MModule.getLength(realSubtext) - spaceInContext, realSubtext);

			// command arg0 = arg1,still arg 1 
			if (behavior.HasFlag(Definitions.CommandBehavior.EqSplit) && behavior.HasFlag(Definitions.CommandBehavior.RSArgs))
			{
				argCallState = await newNoParseParser.CommandEqSplitArgsParse(remainder);
			}
			// command arg0 = arg1,arg2
			else if (behavior.HasFlag(Definitions.CommandBehavior.EqSplit))
			{
				argCallState = await newNoParseParser.CommandEqSplitParse(remainder);
			}
			// Command arg0,arg1,arg2,arg
			else if (behavior.HasFlag(Definitions.CommandBehavior.RSArgs))
			{
				argCallState = await newNoParseParser.CommandCommaArgsParse(remainder);
			}
			else
			{
				argCallState = await newNoParseParser.CommandSingleArgParse(remainder);
			}
		}

		List<CallState> arguments = [];

		var eqSplit = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.EqSplit);
		var noParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.NoParse);
		var noRsParse = libraryCommandDefinition.Attribute.Behavior.HasFlag(Definitions.CommandBehavior.RSNoParse);
		var nArgs = argCallState?.Arguments?.Length;

		// TODO: Implement lsargs - but there are no immediate commands that need it.

		if (argCallState is null)
		{
			return arguments;
		}

		if (eqSplit)
		{
			arguments.Add(noParse
				? new CallState(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty(), argCallState.Depth)
				: (await parser.FunctionParse(argCallState.Arguments!.FirstOrDefault() ?? MModule.empty()))!);

			if (nArgs < 2) return arguments;

			if (noRsParse || noParse)
			{
				arguments.AddRange(argCallState.Arguments!
					.Skip(1)
					// TODO: Implement Parsed Message alt
					.Select(x =>
						new CallState(x,
							argCallState.Depth,
							null,
							async () =>
								(await parser.FunctionParse(x))!.Message!)));
			}
			else
			{
				foreach (var argument in argCallState.Arguments!.Skip(1))
				{
					// This is done to avoid allocation with ValueTask.
					arguments.Add((await parser.FunctionParse(argument))!);
				}
			}
		}
		else
		{
			if (noParse)
			{
				arguments.AddRange(argCallState.Arguments!
					.Select(x => new CallState(x, argCallState.Depth)));
			}
			else
			{
				foreach (var argument in argCallState.Arguments!)
				{
					// This is done to avoid allocation with ValueTask.
					arguments.Add((await parser.FunctionParse(argument))!);
				}
			}
		}

		return arguments;
	}
}