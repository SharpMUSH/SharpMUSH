﻿using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class BooleanExpressionUnitTests : TestsBase
{
	private IBooleanExpressionParser BooleanParser => Services.GetRequiredService<IBooleanExpressionParser>();	
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();

	[Arguments("!#FALSE", true)]
	[Arguments("#TRUE", true)]
	[Arguments("(#TRUE)", true)]
	[Arguments("!#TRUE", false)]
	[Arguments("#FALSE", false)]
	[Arguments("(#FALSE)", false)]
	[Arguments("(#FALSE | #TRUE) & #TRUE", true)]
	[Arguments("(#FALSE | #TRUE) | #FALSE", true)]
	[Arguments("(#FALSE | #TRUE) & #FALSE", false)]
	[Arguments("#TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE", true)]
	[Arguments("#TRUE & #FALSE", false)]
	[Arguments("#FALSE & #TRUE", false)]
	[Arguments("#TRUE | #FALSE", true)]
	[Arguments("#FALSE | #TRUE", true)]
	[Arguments("#TRUE & #TRUE & #TRUE", true)]
	[Arguments("#TRUE | #TRUE | #TRUE", true)]
	[Arguments("#TRUE & !#FALSE | #TRUE", true)]
	[Test]
	public async Task SimpleExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player & #TRUE", true)]
	[Arguments("type^Player & #FALSE", false)]
	[Arguments("type^Player & !type^Player", false)]
	[Arguments("type^Thing", false)]
	[Arguments("type^Player", true)]
	[Test]
	public async Task TypeExpressions(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsTrue();
		await Assert.That(bep.Compile(input)(dbn, dbn)).IsEqualTo(expected);
	}

	[Arguments("type^Player", true)]
	[Arguments("type^Thing", true)]
	[Arguments("type^Room", true)]
	[Arguments("type^Exit", true)]
	[Arguments("type^Nonsense", false)]
	[Test]
	public async Task TypeValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("name^Player*", true)]  // Should validate (name locks always valid syntactically)
	[Arguments("name^test", true)]
	[Arguments("name^*", true)]
	[Test]
	public async Task NameValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("=me", true)]  // Exact object locks always valid syntactically
	[Arguments("=#1", true)]
	[Arguments("=TestObject", true)]
	[Test]
	public async Task ExactObjectValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("name^God", true)]  // DBRef #1 is named "God"
	[Arguments("name^NonExistent", false)]  // This player name shouldn't exist
	[Test]
	public async Task NameExpressionMatching(string input, bool expected)
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, player)).IsTrue();
		await Assert.That(bep.Compile(input)(player, player)).IsEqualTo(expected);
	}

	[Arguments("=#1", true)]  // Player #1 matches itself
	[Arguments("=#2", false)]  // Player #1 doesn't match #2
	[Arguments("=me", true)]  // Player #1 owned by itself, "me" should match
	[Test]
	public async Task ExactObjectMatching(string input, bool expected)
	{
		var bep = BooleanParser;
		var player = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, player)).IsTrue();
		await Assert.That(bep.Compile(input)(player, player)).IsEqualTo(expected);
	}

	[Arguments("dbreflist^testattr", true)]  // DBRef list locks are always valid syntactically
	[Test]
	public async Task DbRefListValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("ip^127.0.0.1", true)]  // IP locks are always valid syntactically
	[Arguments("ip^192.168.*", true)]
	[Arguments("hostname^localhost", true)]  // Hostname locks are always valid syntactically
	[Test]
	public async Task HostLockValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}

	[Arguments("channel^Public", true)]  // Channel locks are always valid syntactically
	[Test]
	public async Task ChannelValidation(string input, bool expected)
	{
		var bep = BooleanParser;
		var dbn = (await Database.GetObjectNodeAsync(new DBRef(1))).Known();

		await Assert.That(bep.Validate(input, dbn)).IsEqualTo(expected);
	}
}