﻿using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Commands;

public class GeneralCommandTests : BaseUnitTest
{
		private static IMUSHCodeParser? _parser;

		[Before(Class)]
		public static async Task OneTimeSetup()
		{
				_parser = await TestParser(ns: Substitute.For<INotifyService>());
		}

		[Test]
		[Arguments("@pemit #1=1 This is a test", "1 This is a test")]
		[Arguments("@pemit #1=2 This is a test;", "2 This is a test;")]
		public async Task Test(string str, string expected)
		{
				Console.WriteLine("Testing: {0}", str);
				await _parser!.CommandParse("1", MModule.single(str));

				await _parser.NotifyService
					.Received(Quantity.Exactly(1))
					.Notify(Arg.Any<AnySharpObject>(), expected);
		}

		[Test]
		public async Task DoListSimple()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1=3 This is a test"));

				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "3 This is a test");
		}

		[Test]
		public async Task DoListSimple2()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1 2 3=@pemit #1={4 This is, a test};"));

				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "4 This is, a test");
		}

		[Test]
		public async Task DoListComplex()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1 2 3={@pemit #1=5 This is a test; @pemit #1=6 This is also a test}"));

				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "5 This is a test");
				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "6 This is also a test");
		}

		[Test]
		public async Task DoListComplex2()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1 2 3={@pemit #1=7 This is a test; @pemit #1=8 This is also a test}; @pemit #1=9 Repeat 3 times in this mode."));

				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "7 This is a test");
				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "8 This is also a test");
				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "9 Repeat 3 times in this mode.");
		}

		[Test]
		public async Task DoListComplex3()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1={@dolist 1 2 3=@pemit #1=10 This is a test}; @pemit #1=11 Repeat 1 times in this mode."));

				await _parser.NotifyService
					.Received(Quantity.Exactly(3))
					.Notify(Arg.Any<AnySharpObject>(), "10 This is a test");
				await _parser.NotifyService
					.Received(Quantity.Exactly(1))
					.Notify(Arg.Any<AnySharpObject>(), "11 Repeat 1 times in this mode.");
		}

		[Test]
		public async Task DoListComplex4()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist 1 2={@dolist 1 2 3=@pemit #1=12 This is a test}; @pemit #1=13 Repeat 2 times in this mode."));

				await _parser.NotifyService
					.Received(Quantity.Exactly(6))
					.Notify(Arg.Any<AnySharpObject>(), "12 This is a test");
				await _parser.NotifyService
					.Received(Quantity.Exactly(2))
					.Notify(Arg.Any<AnySharpObject>(), "13 Repeat 2 times in this mode.");
		}

		[Test]
		public async Task DoListComplex5()
		{
				await _parser!.CommandParse("1", MModule.single("@dolist a b={@dolist 1 2 3=@pemit #1=14 This is a test %i0}; @pemit #1=15 Repeat 1 times in this mode %i0"));

				await _parser.NotifyService
					.Received(Quantity.Exactly(2))
					.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 1");
				await _parser.NotifyService
					.Received(Quantity.Exactly(2))
					.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 2");
				await _parser.NotifyService
					.Received(Quantity.Exactly(2))
					.Notify(Arg.Any<AnySharpObject>(), "14 This is a test 3");
				await _parser.NotifyService
					.Received(Quantity.Exactly(1))
					.Notify(Arg.Any<AnySharpObject>(), "15 Repeat 1 times in this mode a");
				await _parser.NotifyService
					.Received(Quantity.Exactly(1))
					.Notify(Arg.Any<AnySharpObject>(), "15 Repeat 1 times in this mode b");
		}

		[Test, Skip("Not Implemented")]
		public async Task DoFlagSet()
		{
				await _parser!.CommandParse("1", MModule.single("@set #1=DEBUG"));

				var one = await _parser.Mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
				var onePlayer = one.AsPlayer;
				var flags = onePlayer.Object.Flags.Value;

				await Assert.That(flags.Count(x => x.Name == "DEBUG")).IsEqualTo(1);
		}
}