using System.Text.Json;
using SharpMUSH.Implementation.Dice;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Tests.Dice;

/// <summary>
/// The dice engine behind roll(), rolled with scripted faces so every result is known.
/// </summary>
public class DiceRollTests
{
	/// <summary>Hands out <paramref name="faces"/> in order, then 1s; refuses a face larger than the die.</summary>
	private static Func<int, int> Faces(params int[] faces)
	{
		var queue = new Queue<int>(faces);
		return sides =>
		{
			var face = queue.Count > 0 ? queue.Dequeue() : 1;
			return face <= sides ? face : throw new InvalidOperationException($"face {face} on a d{sides}");
		};
	}

	private static Result<DiceRollResult> Rolled(string formula, string? variables, DiceRollMode mode, int[] faces)
	{
		using var document = variables is null ? null : JsonDocument.Parse(variables);
		var root = document?.RootElement;
		return DiceRoll.RollAsync(formula,
				name => ValueTask.FromResult(root is JsonElement json ? DiceRoll.FromJson(json, name) : new NotFound()),
				Faces(faces), mode)
			.AsTask().GetAwaiter().GetResult();
	}

	private static DiceRollResult Roll(string formula, string? variables, params int[] faces)
		=> Rolled(formula, variables, DiceRollMode.Random, faces).Expect<DiceRollResult>();

	private static string Error(string formula, string? variables = null, params int[] faces)
		=> Rolled(formula, variables, DiceRollMode.Random, faces).Expect<Error<string>>().Value;

	[Test]
	[Arguments("2d6+5", new[] { 3, 4 }, "12", "2d6[3, 4] + 5 = 12")]
	[Arguments("d20", new[] { 17 }, "17", "d20[17] = 17")]
	[Arguments("d%", new[] { 64 }, "64", "d%[64] = 64")]
	[Arguments("4d6kh3", new[] { 6, 1, 4, 3 }, "13", "4d6kh3[6, (1), 4, 3] = 13")]
	[Arguments("4d6k3", new[] { 6, 1, 4, 3 }, "13", "4d6k3[6, (1), 4, 3] = 13")]
	[Arguments("2d20kl", new[] { 15, 7 }, "7", "2d20kl[(15), 7] = 7")]
	[Arguments("4d6dl1", new[] { 6, 1, 4, 3 }, "13", "4d6dl1[6, (1), 4, 3] = 13")]
	[Arguments("3d6dh", new[] { 6, 1, 4 }, "5", "3d6dh[(6), 1, 4] = 5")]
	[Arguments("2 * (1d4 + 1) - 3", new[] { 2 }, "3", "2 * (1d4[2] + 1) - 3 = 3")]
	[Arguments("-1d6", new[] { 5 }, "-5", "-1d6[5] = -5")]
	[Arguments("7 / 2", new int[0], "3.5", "7 / 2 = 3.5")]
	[Arguments("7 % 4", new int[0], "3", "7 % 4 = 3")]
	public async Task RollsAndWritesTheFormula(string formula, int[] faces, string total, string text)
	{
		var result = Roll(formula, null, faces);

		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(result.Total)).IsEqualTo(total);
		await Assert.That(result.Text).IsEqualTo(text);
	}

	[Test]
	public async Task KeepingMoreDiceThanRolledKeepsThemAll()
	{
		var result = Roll("2d6kh3+2", null, 5, 3);

		await Assert.That(result.Total).IsEqualTo(10m);
		await Assert.That(result.Text).IsEqualTo("2d6kh3[5, 3] + 2 = 10");
	}

	[Test]
	public async Task DroppingMoreDiceThanRolledDropsThemAll()
	{
		var result = Roll("2d6dl5", null, 5, 3);

		await Assert.That(result.Total).IsEqualTo(0m);
	}

	[Test]
	public async Task FateDiceAreMinusOneZeroOrOne()
	{
		// A fate die is rolled as a d3: 1, 2, 3 read as -1, 0, 1.
		var result = Roll("4dF", null, 1, 2, 3, 3);

		await Assert.That(result.Total).IsEqualTo(1m);
		await Assert.That(result.Text).IsEqualTo("4dF[-1, 0, 1, 1] = 1");
		await Assert.That(result.Terms[0].Faces).IsEqualTo("F");
	}

	[Test]
	public async Task ExplodingDiceAddADieAfterEachMaximum()
	{
		var result = Roll("3d6x", null, 6, 2, 4, 6, 3);

		await Assert.That(result.Text).IsEqualTo("3d6x[6!, 6!, 3, 2, 4] = 21");
	}

	[Test]
	public async Task ExplodeOnceLetsOnlyTheFirstDiceExplode()
	{
		var result = Roll("2d6xo", null, 6, 6, 6, 1);

		await Assert.That(result.Text).IsEqualTo("2d6xo[6!, 6, 6!, 1] = 19");
	}

	[Test]
	public async Task ExplodeTakesAComparison()
	{
		var result = Roll("2d10x>=9", null, 9, 3, 10, 2);

		await Assert.That(result.Text).IsEqualTo("2d10x>=9[9!, 10!, 2, 3] = 24");
	}

	[Test]
	public async Task ExplodingStopsAfterOneHundredInARow()
	{
		var faces = Enumerable.Repeat(1, 200).ToArray();
		var result = Roll("1d1x", null, faces);

		await Assert.That(result.Total).IsEqualTo(101m);
	}

	[Test]
	public async Task RerollOnceReplacesOnesOnce()
	{
		var result = Roll("3d6r", null, 1, 5, 1, 1, 4);

		await Assert.That(result.Text).IsEqualTo("3d6r[(1), 1, 5, (1), 4] = 10");
	}

	[Test]
	public async Task RerollRecursiveRerollsUntilTheFaceStopsMatching()
	{
		var result = Roll("1d6rr<3", null, 1, 2, 5);

		await Assert.That(result.Text).IsEqualTo("1d6rr<3[(1), (2), 5] = 5");
	}

	[Test]
	public async Task ModifiersApplyInTheOrderWritten()
	{
		var result = Roll("4d6r1kh3", null, 1, 6, 2, 3, 5);

		await Assert.That(result.Text).IsEqualTo("4d6r1kh3[(1), 5, 6, (2), 3] = 14");
	}

	[Test]
	public async Task CountSuccessesDefaultsToTheHighestFace()
	{
		var result = Roll("4d6cs", null, 6, 5, 6, 1);

		await Assert.That(result.Text).IsEqualTo("4d6cs[6*, 5, 6*, 1] = 2");
	}

	[Test]
	public async Task DeductFailuresTakesOneFromTheSuccessesPerFailure()
	{
		var result = Roll("5d10cs>=8df1", null, 9, 1, 8, 4, 10);

		await Assert.That(result.Text).IsEqualTo("5d10cs>=8df1[9*, 1_, 8*, 4, 10*] = 2");
	}

	[Test]
	public async Task CountFailuresCountsTheFailures()
	{
		var result = Roll("5d10cf<3", null, 1, 2, 5, 6, 2);

		await Assert.That(result.Text).IsEqualTo("5d10cf<3[1_, 2_, 5, 6, 2_] = 3");
	}

	[Test]
	public async Task SubtractFailuresTakesTheirFaces()
	{
		var result = Roll("3d6sf<3", null, 2, 5, 6);

		await Assert.That(result.Text).IsEqualTo("3d6sf<3[2_, 5, 6] = 9");
	}

	[Test]
	[Arguments("3d6ms10", 13, "3")]
	[Arguments("3d6ms>10", 13, "3")]
	[Arguments("3d6ms<10", 13, "-3")]
	public async Task MarginOfSuccessComparesTheTotalWithATarget(string formula, int sum, string total)
	{
		var result = Roll(formula, null, 6, 4, sum - 10);

		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(result.Total)).IsEqualTo(total);
	}

	[Test]
	[Arguments("4d6even", "4d6even[2*, 3, 6*, 5] = 2")]
	[Arguments("4d6odd", "4d6odd[2, 3*, 6, 5*] = 2")]
	public async Task EvenAndOddCountAsSuccesses(string formula, string text)
	{
		await Assert.That(Roll(formula, null, 2, 3, 6, 5).Text).IsEqualTo(text);
	}

	[Test]
	public async Task ATargetCanBeAFormula()
	{
		var result = Roll("4d10cs>=(DIFFICULTY)", """{"DIFFICULTY":"5+2"}""", 7, 6, 9, 10);

		await Assert.That(result.Total).IsEqualTo(3m);
	}

	[Test]
	public async Task DAloneDropsTheLowest()
	{
		var result = Roll("4d10d2", null, 3, 9, 1, 7);

		await Assert.That(result.Text).IsEqualTo("4d10d2[(3), 9, (1), 7] = 16");
	}

	[Test]
	public async Task ExplodingCanBeCapped()
	{
		var result = Roll("3d10x1=10", null, 10, 10, 4, 10);

		await Assert.That(result.Text).IsEqualTo("3d10x1=10[10!, 10, 10, 4] = 34");
	}

	[Test]
	public async Task MinimumOrMaximumWithoutAValueChangesNothing()
	{
		await Assert.That(Roll("2d6min", null, 1, 6).Total).IsEqualTo(7m);
		await Assert.That(Roll("2d6max", null, 1, 6).Total).IsEqualTo(7m);
	}

	[Test]
	public async Task CoinsCountHeads()
	{
		// A coin is rolled as a d2: 1 is tails (0), 2 heads (1).
		var result = Roll("4dc", null, 2, 1, 2, 2);

		await Assert.That(result.Text).IsEqualTo("4dc[1, 0, 1, 1] = 3");
		await Assert.That(result.Terms[0].Faces).IsEqualTo("C");
	}

	[Test]
	public async Task ACoinCallCountsTheCalledFace()
	{
		var result = Roll("3dcc0", null, 1, 2, 1);

		await Assert.That(result.Text).IsEqualTo("3dcc0[0*, 1, 0*] = 2");
	}

	[Test]
	public async Task PoolsKeepWholeTotals()
	{
		var result = Roll("{1d20, 10}kh + 5", null, 7);

		await Assert.That(result.Text).IsEqualTo("{1d20[7], 10}kh[(7), 10] + 5 = 15");
		await Assert.That(result.Terms[1].Faces).IsEqualTo("pool");
	}

	[Test]
	public async Task PoolsCountSuccesses()
	{
		var result = Roll("{2d6, 1d8, 3}cs>5", null, 4, 3, 2);

		await Assert.That(result.Total).IsEqualTo(1m);
	}

	[Test]
	public async Task PoolsCannotExplode()
	{
		await Assert.That(Error("{1d6, 2}x")).IsEqualTo(string.Format(ErrorMessages.Returns.RollSyntaxErrorFormat, 9));
	}

	[Test]
	public async Task FlavorLabelsATerm()
	{
		var result = Roll("2d6[fire] + 1d4[ cold ] + 2[bonus]", null, 3, 4, 2);

		await Assert.That(result.Text).IsEqualTo("2d6[3, 4] fire + 1d4[2] cold + 2 bonus = 11");
		await Assert.That(result.Terms[0].Flavor).IsEqualTo("fire");
		await Assert.That(result.ToJson()).Contains("\"flavor\":\"cold\"");
	}

	[Test]
	[Arguments(DiceRollMode.Minimize, "3")]
	[Arguments(DiceRollMode.Maximize, "15")]
	public async Task MinimizeAndMaximizeGiveTheRange(DiceRollMode mode, string total)
	{
		var result = Rolled("2d6x + 1dF + 2", null, mode, []).Expect<DiceRollResult>();

		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(result.Total)).IsEqualTo(total);
	}

	[Test]
	[Arguments("1d6 - 1d6", "1d6[1] - 1d6[6] = -5", "1d6[6] - 1d6[1] = 5")]
	[Arguments("10 - 2d6kh1", "10 - 2d6kh1[(6), 6] = 4", "10 - 2d6kh1[(1), 1] = 9")]
	[Arguments("-1d4 * 3", "-1d4[4] * 3 = -12", "-1d4[1] * 3 = -3")]
	[Arguments("2 * (1d6 - 4)", "2 * (1d6[1] - 4) = -6", "2 * (1d6[6] - 4) = 4")]
	[Arguments("1d6 * (1d3 - 2)", "1d6[6] * (1d3[1] - 2) = -6", "1d6[6] * (1d3[3] - 2) = 6")]
	[Arguments("4d6sf<3", "4d6sf<3[2_, 2_, 2_, 2_] = -8", "4d6sf<3[6, 6, 6, 6] = 24")]
	[Arguments("3d6cf<2", "3d6cf<2[2, 2, 2] = 0", "3d6cf<2[1_, 1_, 1_] = 3")]
	[Arguments("2d5even", "2d5even[1, 1] = 0", "2d5even[2*, 2*] = 2")]
	[Arguments("1d20 - (1d4)d4", "1d20[1] - (1d4)d4[4, 4, 4, 4] = -15", "1d20[20] - (1d4)d4[1] = 19")]
	[Arguments("{1d20, 15 - 1d6}kh", "{1d20[1], 15 - 1d6[6]}kh[(1), 9] = 9", "{1d20[20], 15 - 1d6[6]}kh[20, (9)] = 20")]
	[Arguments("abs(1d6 - 1d6)", "0 = 0", "abs(1d6[1] - 1d6[6]) = 5")]
	[Arguments("1d6 % 4", "0 = 0", "3 = 3")]
	public async Task MinimizeAndMaximizeFollowTheOperators(string formula, string lowest, string highest)
	{
		var low = Rolled(formula, null, DiceRollMode.Minimize, []).Expect<DiceRollResult>();
		var high = Rolled(formula, null, DiceRollMode.Maximize, []).Expect<DiceRollResult>();

		await Assert.That(low.Text).IsEqualTo(lowest);
		await Assert.That(high.Text).IsEqualTo(highest);
	}

	[Test]
	public async Task ARangeThatCouldDivideByZeroIsRefused()
	{
		await Assert.That(Rolled("10 / (1d3 - 2)", null, DiceRollMode.Maximize, []).Expect<Error<string>>().Value)
			.IsEqualTo(ErrorMessages.Returns.DivisionByZero);
	}

	[Test]
	[Arguments("{79228162514264337593543950335, 1}")]
	[Arguments("round(79228162514264337593543950335)")]
	public async Task OverflowInAPoolOrFunctionIsOutOfRange(string formula)
	{
		await Assert.That(Error(formula)).IsEqualTo(ErrorMessages.Returns.NumberOutOfRange);
	}

	/// <summary>The examples in the roll help topics, with the faces they show.</summary>
	[Test]
	[Arguments("1d20+3", new[] { 14 }, "1d20[14] + 3 = 17")]
	[Arguments("4dF+2", new[] { 3, 2, 1, 3 }, "4dF[1, 0, -1, 1] + 2 = 3")]
	[Arguments("(1d4)d6", new[] { 3, 5, 2, 6 }, "(1d4)d6[5, 2, 6] = 13")]
	[Arguments("floor(1d6/2)", new[] { 5 }, "floor(1d6[5] / 2) = 2")]
	[Arguments("max(1d6,1d6)", new[] { 2, 5 }, "max(1d6[2], 1d6[5]) = 5")]
	[Arguments("4d6kh3", new[] { 5, 2, 4, 6 }, "4d6kh3[5, (2), 4, 6] = 15")]
	[Arguments("2d20kl", new[] { 17, 8 }, "2d20kl[(17), 8] = 8")]
	[Arguments("4d6r1kh3", new[] { 1, 5, 3, 6, 4 }, "4d6r1kh3[(1), 4, 5, (3), 6] = 15")]
	[Arguments("3d6x", new[] { 6, 2, 4, 3 }, "3d6x[6!, 3, 2, 4] = 15")]
	[Arguments("10d10cs>=8", new[] { 3, 9, 8, 1, 10, 4, 2, 7, 8, 5 }, "10d10cs>=8[3, 9*, 8*, 1, 10*, 4, 2, 7, 8*, 5] = 4")]
	[Arguments("5d10cs>=8df1", new[] { 9, 1, 8, 4, 10 }, "5d10cs>=8df1[9*, 1_, 8*, 4, 10*] = 2")]
	[Arguments("1d20ms>=15", new[] { 18 }, "1d20ms>=15[18] = 3")]
	[Arguments("4d6min2", new[] { 1, 5, 1, 3 }, "4d6min2[2, 5, 2, 3] = 12")]
	[Arguments("{1d20, 10}kh + 5", new[] { 7 }, "{1d20[7], 10}kh[(7), 10] + 5 = 15")]
	[Arguments("{2d6, 1d12}kh", new[] { 3, 5, 11 }, "{2d6[3, 5], 1d12[11]}kh[(8), 11] = 11")]
	[Arguments("2d6[fire] + 1d4[cold]", new[] { 3, 4, 2 }, "2d6[3, 4] fire + 1d4[2] cold = 9")]
	[Arguments("2d6kh1+1", new[] { 5, 2 }, "2d6kh1[5, (2)] + 1 = 6")]
	public async Task HelpExamplesRollAsShown(string formula, int[] faces, string text)
	{
		await Assert.That(Roll(formula, null, faces).Text).IsEqualTo(text);
	}

	[Test]
	[Arguments("2d6kh3+STR", true)]
	[Arguments("{1d20, 10}kh[luck] + @skills.stealth", true)]
	[Arguments("2d6+", false)]
	[Arguments("sqrt(2)", false)]
	public async Task ValidityIsAboutSyntaxNotData(string formula, bool valid)
	{
		await Assert.That(DiceParser.IsValid(formula)).IsEqualTo(valid);
	}

	[Test]
	public async Task NamesAreLookedUpOnceEach()
	{
		var asked = new List<string>();
		var result = await DiceRoll.RollAsync("A + A + B", name =>
		{
			asked.Add(name);
			return ValueTask.FromResult<DiceValue>(name == "A" ? new DiceFormula("B * 2") : 3m);
		}, Faces());

		await Assert.That(result.Expect<DiceRollResult>().Total).IsEqualTo(15m);
		await Assert.That(asked).IsEquivalentTo(["A", "B"]);
	}

	[Test]
	public async Task TooManyNamesAreRefused()
	{
		var formula = string.Join(" + ", Enumerable.Range(0, DiceRoll.MaxNames + 1).Select(i => $"N{i}"));
		var result = await DiceRoll.RollAsync(formula, _ => ValueTask.FromResult<DiceValue>(1m), Faces());

		await Assert.That(result.Expect<Error<string>>().Value).IsEqualTo(ErrorMessages.Returns.RollTooManyNames);
	}

	[Test]
	public async Task MinimumAndMaximumClampEachDie()
	{
		var result = Roll("3d6min2max5", null, 1, 6, 3);

		await Assert.That(result.Text).IsEqualTo("3d6min2max5[2, 5, 3] = 10");
	}

	[Test]
	public async Task ModifiersIgnoreCase()
	{
		var result = Roll("4D6KH3", null, 6, 1, 4, 3);

		await Assert.That(result.Total).IsEqualTo(13m);
	}

	[Test]
	public async Task CountAndSidesMayBeWorkedOut()
	{
		var result = Roll("(1d4)d(SIDES)", """{"SIDES":"2+2"}""", 2, 3, 4);

		await Assert.That(result.Text).IsEqualTo("(1d4)d(SIDES)[3, 4] = 7");
		await Assert.That(result.Terms.Length).IsEqualTo(2);
	}

	[Test]
	public async Task VariablesHoldNumbersOrFormulas()
	{
		var result = Roll("1dF+DEXTERITY", """{"DEXTERITY":"1d6 + SPEED","SPEED":3}""", 3, 4);

		await Assert.That(result.Total).IsEqualTo(8m);
		await Assert.That(result.Text).IsEqualTo("1dF[1] + DEXTERITY(1d6[4] + SPEED(3)) = 8");
	}

	[Test]
	[Arguments("skills.stealth")]
	[Arguments("@skills.stealth")]
	[Arguments("SKILLS.STEALTH")]
	public async Task VariablesReachIntoNestedObjects(string name)
	{
		var result = Roll($"{name}+1", """{"skills":{"stealth":3}}""");

		await Assert.That(result.Total).IsEqualTo(4m);
	}

	[Test]
	public async Task AnExactNameWinsOverOneThatDiffersInCase()
	{
		var result = Roll("dex", """{"DEX":1,"dex":2}""");

		await Assert.That(result.Total).IsEqualTo(2m);
	}

	[Test]
	public async Task BooleansCountAsOneOrZero()
	{
		var result = Roll("BLESSED*4 + CURSED", """{"BLESSED":true,"CURSED":false}""");

		await Assert.That(result.Total).IsEqualTo(4m);
	}

	[Test]
	public async Task NamesStartingWithDAreVariablesUnlessTheyAreDice()
	{
		var result = Roll("DEX + damage + d4", """{"DEX":2,"damage":5}""", 3);

		await Assert.That(result.Text).IsEqualTo("DEX(2) + damage(5) + d4[3] = 10");
	}

	[Test]
	[Arguments("floor(7/2)", "3")]
	[Arguments("ceil(7/2)", "4")]
	[Arguments("round(2.5)", "3")]
	[Arguments("round(-2.5)", "-2")]
	[Arguments("abs(-4)", "4")]
	[Arguments("min(4, 2, 9)", "2")]
	[Arguments("max(4, 2, 9)", "9")]
	[Arguments("clamp(12, 0, 10)", "10")]
	[Arguments("clamp(-3, 0, 10)", "0")]
	[Arguments("ternary(gte(5, 3), 3, 0)", "3")]
	[Arguments("ternary(lt(5, 3), 3, 0)", "0")]
	[Arguments("btwn(9, 8, 10)", "1")]
	[Arguments("btwn(11, 8, 10)", "0")]
	[Arguments("eq(2, 2)", "1")]
	[Arguments("ne(2, 2)", "0")]
	[Arguments("gt(3, 2)", "1")]
	[Arguments("lte(3, 2)", "0")]
	[Arguments("when(gt(2, 1), 7)", "7")]
	[Arguments("1 + when(gt(1, 2), 7)", "1")]
	[Arguments("match(when(gte(12, 15), 8), when(gte(12, 7), 3), 1)", "3")]
	[Arguments("match(when(gte(2, 15), 8), when(gte(2, 7), 3), 1)", "1")]
	[Arguments("match(when(gte(2, 15), 8))", "0")]
	[Arguments("match((when(0, 7)), 9)", "9")]
	[Arguments("match(((when(1, 7))), 9)", "7")]
	public async Task Functions(string formula, string total)
	{
		var result = Roll(formula, null);

		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(result.Total)).IsEqualTo(total);
	}

	[Test]
	public async Task FoundryFormulasReadTheirValues()
	{
		var result = Roll("match(when(btwn(@actor.level, 8, 10), 4), when(btwn(@actor.level, 11, 14), 6), when(gte(@actor.level, 15), 8))",
			"""{"actor":{"level":12}}""");

		await Assert.That(result.Total).IsEqualTo(6m);
		await Assert.That(result.Text).IsEqualTo(
			"match(when(btwn(actor.level(12), 8, 10), 4), when(btwn(actor.level(12), 11, 14), 6), when(gte(actor.level(12), 15), 8)) = 6");
	}

	[Test]
	public async Task EveryArgumentOfAConditionalIsRolled()
	{
		var result = Roll("ternary(gt(1d6, 3), 1d4, 1d8)", null, 5, 2, 7);

		await Assert.That(result.Total).IsEqualTo(2m);
		await Assert.That(result.Text).IsEqualTo("ternary(gt(1d6[5], 3), 1d4[2], 1d8[7]) = 2");
	}

	[Test]
	[Arguments("ternary(gt(1d6, 3), 10, 1d4)", "1", "10")]
	[Arguments("ternary(gt(5, 3), 1d4, 100)", "1", "4")]
	[Arguments("gte(1d20, 10)", "0", "1")]
	[Arguments("gte(5, 3)", "1", "1")]
	[Arguments("5 + when(gt(1d6, 3), 2)", "5", "7")]
	[Arguments("match(when(gte(1d6, 4), 10), when(gt(2, 1), 3), 100)", "3", "10")]
	[Arguments("match(when(gte(1d6, 4), 10))", "0", "10")]
	[Arguments("match((when(0, 7)), 9)", "9", "9")]
	[Arguments("gt(1d6, 0)", "1", "1")]
	[Arguments("lte(1d6, 0)", "0", "0")]
	[Arguments("eq(1d6, 7)", "0", "0")]
	[Arguments("ne(1d6, 7)", "1", "1")]
	[Arguments("btwn(1d6, 1, 6)", "1", "1")]
	[Arguments("btwn(1d6, 7, 9)", "0", "0")]
	[Arguments("gte(1d6, 4)", "0", "1")]
	[Arguments("ternary(1d6, 5, 100)", "5", "5")]
	[Arguments("when(1d6, 5)", "5", "5")]
	[Arguments("match(when(1d6, 5), 100)", "5", "5")]
	public async Task MinimizeAndMaximizeTakeInEveryBranchAConditionalCouldGive(string formula, string lowest, string highest)
	{
		var low = Rolled(formula, null, DiceRollMode.Minimize, []).Expect<DiceRollResult>();
		var high = Rolled(formula, null, DiceRollMode.Maximize, []).Expect<DiceRollResult>();

		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(low.Total)).IsEqualTo(lowest);
		await Assert.That(SharpMUSH.Library.Utilities.MushNumber.Unparse(high.Total)).IsEqualTo(highest);
	}

	[Test]
	public async Task AGroupedWhenKeepsItsParenthesesInTheText()
	{
		var result = Roll("match((when(0, 7)), 9)", null);

		await Assert.That(result.Text).IsEqualTo("match((when(0, 7)), 9) = 9");
	}

	[Test]
	[Arguments("match(when(0, 1/0), 7)", DiceRollMode.Minimize)]
	[Arguments("match(when(0, 1/0), 7)", DiceRollMode.Maximize)]
	[Arguments("match(7, 1/0)", DiceRollMode.Minimize)]
	[Arguments("match(when(1, 7), 1/0)", DiceRollMode.Maximize)]
	public async Task MinimizeAndMaximizeFailWhereRollingWould(string formula, DiceRollMode mode)
	{
		await Assert.That(Rolled(formula, null, mode, []).Expect<Error<string>>().Value)
			.IsEqualTo(ErrorMessages.Returns.DivisionByZero);
		await Assert.That(Error(formula)).IsEqualTo(ErrorMessages.Returns.DivisionByZero);
	}

	[Test]
	[Arguments("ternary(1, 2)")]
	[Arguments("when(1)")]
	[Arguments("btwn(1, 2)")]
	public async Task ConditionalsNeedTheirArguments(string formula)
	{
		await Assert.That(Error(formula)).StartsWith("#-1");
	}

	[Test]
	public async Task EachUseOfAVariableRollsAgain()
	{
		var result = Roll("HIT + HIT", """{"HIT":"1d6"}""", 2, 5);

		await Assert.That(result.Total).IsEqualTo(7m);
	}

	[Test]
	public async Task JsonListsEveryDie()
	{
		var result = Roll("2d6kh1+1", null, 5, 2);

		await Assert.That(result.ToJson()).IsEqualTo(
			"""{"formula":"2d6kh1+1","total":6,"text":"2d6kh1[5, (2)] + 1 = 6","dice":[{"formula":"2d6kh1","faces":6,"total":5,"results":[{"result":5,"active":true},{"result":2,"active":false,"dropped":true}]}]}""");
	}

	[Test]
	public async Task JsonNamesFateFacesF()
	{
		var json = Roll("1dF", null, 2).ToJson();

		await Assert.That(json).Contains("\"faces\":\"F\"");
	}

	[Test]
	[Arguments("2d6+", 5)]
	[Arguments("2d6 3", 5)]
	[Arguments("(1d6", 5)]
	[Arguments("2d6kz", 5)]
	[Arguments("2d6x>", 6)]
	[Arguments("", 1)]
	public async Task SyntaxErrorsNameWhereReadingStopped(string formula, int character)
	{
		await Assert.That(Error(formula, null, 1, 1))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollSyntaxErrorFormat, character));
	}

	[Test]
	public async Task AnUnknownVariableIsNamed()
	{
		await Assert.That(Error("1d6+STRENGTH", """{"DEX":1}"""))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollNoSuchVariableFormat, "STRENGTH"));
		await Assert.That(Error("STRENGTH"))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollNoSuchVariableFormat, "STRENGTH"));
	}

	[Test]
	public async Task AVariableThatLeadsBackToItselfIsRefused()
	{
		await Assert.That(Error("A", """{"A":"1+B","B":"C","C":"a"}"""))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollCircularVariableFormat, "A"));
	}

	[Test]
	public async Task AVariableMustBeANumberOrAFormula()
	{
		await Assert.That(Error("STATS", """{"STATS":{"DEX":1}}"""))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollVariableNotANumberFormat, "STATS"));
	}

	[Test]
	public async Task AnErrorInsideAVariablesFormulaIsReported()
	{
		await Assert.That(Error("DEX", """{"DEX":"1d6+"}"""))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollSyntaxErrorFormat, 5));
	}

	[Test]
	public async Task UnknownFunctionsAreNamed()
	{
		await Assert.That(Error("sqrt(4)"))
			.IsEqualTo(string.Format(ErrorMessages.Returns.RollUnknownFunctionFormat, "SQRT"));
	}

	[Test]
	[Arguments("1001d6", ErrorMessages.Returns.RollTooManyDice)]
	[Arguments("1d0", ErrorMessages.Returns.RollSidesOutOfRange)]
	[Arguments("1d1000001", ErrorMessages.Returns.RollSidesOutOfRange)]
	[Arguments("(-1)d6", ErrorMessages.Returns.NumberOutOfRange)]
	[Arguments("1/0", ErrorMessages.Returns.DivisionByZero)]
	[Arguments("1d6 % (2-2)", ErrorMessages.Returns.DivisionByZero)]
	public async Task OutOfRangeRollsAreRefused(string formula, string error)
	{
		await Assert.That(Error(formula, null, 1)).IsEqualTo(error);
	}

	[Test]
	public async Task RerollsAndExplosionsCountTowardsTheDiceLimit()
	{
		var faces = Enumerable.Repeat(6, 1100).ToArray();

		await Assert.That(Error("20d6x", null, faces)).IsEqualTo(ErrorMessages.Returns.RollTooManyDice);
	}

	[Test]
	public async Task AValueUsedManyTimesCountsEachUseTowardsTheTermLimit()
	{
		// A1 is A2+A2, A2 is A3+A3, ...: 2^29 terms from 30 short values.
		var values = string.Join(",", Enumerable.Range(1, 29).Select(i => $"\"A{i}\":\"A{i + 1}+A{i + 1}\""));
		var data = $"{{{values},\"A30\":\"1\"}}";

		await Assert.That(Error("A1", data)).IsEqualTo(ErrorMessages.Returns.RollFormulaTooLarge);
	}

	[Test]
	public async Task AFormulaOfMoreThanAThousandTermsIsRefused()
	{
		await Assert.That(Error(string.Join("+", Enumerable.Repeat("1", DiceRoll.MaxTerms + 1))))
			.IsEqualTo(ErrorMessages.Returns.RollFormulaTooLarge);
	}

	[Test]
	[Arguments("(", ")")]
	[Arguments("-", "")]
	[Arguments("{", "}")]
	[Arguments("abs(", ")")]
	public async Task NestingPastTheLimitIsRefused(string open, string close)
	{
		var depth = DiceRoll.MaxNesting + 1;
		var formula = string.Concat(Enumerable.Repeat(open, depth)) + "1" + string.Concat(Enumerable.Repeat(close, depth));

		await Assert.That(Error(formula)).IsEqualTo(ErrorMessages.Returns.RollFormulaTooDeep);
	}

	[Test]
	public async Task NestedValuesCountTowardsTheNestingLimit()
	{
		// Each value opens 3 brackets around the next: 22 values nest past 64.
		var values = string.Join(",", Enumerable.Range(1, 21).Select(i => $"\"V{i}\":\"(((V{i + 1})))\""));
		var data = $"{{{values},\"V22\":\"1\"}}";

		await Assert.That(Error("V1", data)).IsEqualTo(ErrorMessages.Returns.RollFormulaTooDeep);
	}

	[Test]
	public async Task TheLargestAllowedFormulaRollsOnASmallStack()
	{
		var formula = string.Join("+", Enumerable.Repeat("1d6", DiceRoll.MaxTerms - DiceRoll.MaxNesting))
			+ "+" + string.Concat(Enumerable.Repeat("(", DiceRoll.MaxNesting - 1)) + "1d6"
			+ string.Concat(Enumerable.Repeat(")", DiceRoll.MaxNesting - 1));
		var totals = new decimal[3];
		var thread = new Thread(() =>
		{
			totals[0] = Roll(formula, null).Total;
			totals[1] = Rolled(formula, null, DiceRollMode.Minimize, []).Expect<DiceRollResult>().Total;
			totals[2] = Rolled(formula, null, DiceRollMode.Maximize, []).Expect<DiceRollResult>().Total;
		}, 256 * 1024);
		thread.Start();
		thread.Join();

		await Assert.That(totals).IsEquivalentTo([937m, 937m, 5622m]);
	}
}
