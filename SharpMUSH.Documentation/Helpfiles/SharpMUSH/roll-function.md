<!-- help-article
{
  "corpus": "help",
  "id": "roll-function",
  "lookup": "roll()",
  "aliases": [
    "roll",
    "dice"
  ],
  "sections": [
    {
      "id": "roll-dice",
      "heading": "Dice",
      "lookup": "roll dice"
    },
    {
      "id": "roll-modifiers",
      "heading": "Modifiers",
      "lookup": "roll modifiers"
    },
    {
      "id": "roll-pools",
      "heading": "Pools and labels",
      "lookup": "roll pools"
    },
    {
      "id": "roll-data",
      "heading": "Data",
      "lookup": "roll data"
    },
    {
      "id": "roll-options",
      "heading": "Options",
      "lookup": "roll options"
    }
  ]
}
-->
# roll()

`roll(<formula>[, <data>[, <options>]])`

Rolls the dice in `<formula>` and returns the total. Formulas are written the way Foundry VTT writes them: dice terms such as `2d6`, modifiers such as `kh3` that keep, reroll or count dice, numbers, `+ - * / %`, brackets, and names whose values come from `<data>`.

```sharp
> think roll(2d6+5)
12
> think roll(4d6kh3)
14
> &stat`dex me=1d6+@stat.speed
> &stat`speed me=2
> think roll(1dF+@stat.dex,me)
5
```

Output: a number. Each call rolls again, so the numbers above are one possible result.

`<data>` is an object whose attributes the formula names, or a JSON object (see roll data). `<options>` asks for a line showing every die, the dice as JSON, or the lowest or highest the formula can come to (see roll options).

Softcode reads `[ ]`, `{ }`, `%` and commas before roll() sees them. A formula typed into the call that uses them goes inside lit(), and `d%` is typed `d%%`. A formula kept in an attribute and passed with v() or get() needs neither.

```sharp
> think roll(lit({1d20, 10}kh + 5))
17
> think roll(d%%)
42
```

roll() returns an error starting with #-1 when the formula cannot be read (naming the character where reading stopped), names something `<data>` does not hold, or rolls more than 1000 dice. valid(roll, <formula>) checks a formula without rolling it.

::: seealso
- [die()]
- [rand()]
- [valid()]
- [json()]
:::

## Dice

`<count>d<sides>` rolls `<count>` dice with `<sides>` sides each and adds them up. The count may be left out for one die.

2d6         two six-sided dice<br>
d20         one twenty-sided die<br>
d%          one hundred-sided die (`d%%` typed into a call)<br>
4dF         four fate dice, each -1, 0 or 1<br>
3dc         three coins, each 0 (tails) or 1 (heads)<br>
(1d4)d6     a count worked out first: 1d4 six-sided dice<br>
1d(SIDES)   sides worked out first, here from data

A count or number of sides that comes out as a fraction is rounded down. A die has at most 1000000 sides.

Brackets group as usual, and these functions may be used inside a formula: `floor()`, `ceil()`, `round()`, `abs()`, `min()` and `max()`. round() rounds a half up, so 2.5 is 3 and -2.5 is -2.

## Modifiers

Modifiers follow a dice term with no spaces and apply in the order written: `4d6r1kh3` rerolls ones and then keeps the highest three. A number after a modifier may be a bracketed formula instead, such as `cs>=(@difficulty)`.

kh<n>, k<n>     keep the highest n dice; n is 1 if left out<br>
kl<n>           keep the lowest n<br>
dl<n>, d<n>     drop the lowest n<br>
dh<n>           drop the highest n<br>
x               explode: a die showing its highest face adds another die, which can explode too<br>
xo              explode once: only the dice first rolled explode<br>
x<n>=<face>     explode on that face, adding at most n dice<br>
r               reroll once each die showing its lowest face<br>
rr              reroll until the die no longer shows it<br>
cs              count successes: the total is how many dice show their highest face<br>
cf              count failures: the total is how many dice show their lowest face<br>
df              deduct failures: each die showing its lowest face takes 1 away<br>
sf              subtract failures: each failing die takes its own face away<br>
even, odd       count even or odd dice as successes<br>
ms<n>           margin of success: the total minus n (`ms<n` is n minus the total)<br>
min<n>          a die below n counts as n<br>
max<n>          a die above n counts as n<br>
c<n>            a coin's call: 3dcc1 counts heads, 3dcc0 tails

Keeping more dice than were rolled keeps them all: `2d6kh3` is two dice, not an error.

x, xo, r, rr, cs, cf, df, sf and ms can name the faces they act on: a number (`r2`), or a comparison with `=`, `>`, `>=`, `<` or `<=` (`x>=9`, `cs>=8`, `sf<3`). One die explodes or is rerolled at most 100 times in a row.

```sharp
> think roll(10d10cs>=8,,text)
10d10cs>=8[3, 9*, 8*, 1, 10*, 4, 2, 7, 8*, 5] = 4
> think roll(3d6x,,text)
3d6x[6!, 2, 4, 3] = 15
> think roll(5d10cs>=8df1,,text)
5d10cs>=8df1[9*, 1_, 8*, 4, 10*] = 2
```

Output: the dice and the total; the faces will differ each time.

## Pools and labels

A pool is formulas in braces, separated by commas. Each one's total is a result, and the pool's modifiers keep, drop or count those results as they would dice. A pool takes keep, drop, count, min, max and ms, but not explode or reroll.

```sharp
> think roll(lit({1d20, 10}kh + 5),,text)
{1d20[7], 10}kh[(7), 10] + 5 = 15
```

Output: the d20 rolled 7, so the 10 was kept.

A label in square brackets after a term names it, as Foundry's flavor text does. Labels show in the text and JSON and do not change the total:

```sharp
> think roll(lit(2d6[fire] + 1d4[cold]),,text)
2d6[3, 4] fire + 1d4[2] cold = 9
```

## Data

A name in the formula is looked up in `<data>`. A number is used as it is. Anything else is itself a formula, so a value can roll dice and name other values. A name may start with `@`, as in Foundry, and a dot steps down a level.

When `<data>` is an object, a name is one of its attributes, and a dot is a backtick: `@stat.dex` reads `STAT`DEX`. Attributes are found and checked the way get() finds and checks them, and are never evaluated: what is stored is the number or formula.

```sharp
> &stat`dex me=1d6+@stat.speed
> &stat`speed me=2
> think roll(1dF+@stat.dex,me,text)
1dF[1] + stat.dex(1d6[4] + stat.speed(2)) = 7
```

Output: each name is followed by what it came to, in brackets.

When `<data>` starts with `{`, it is a JSON object, matched exactly first and then ignoring case. true counts as 1 and false as 0.

```sharp
> &stats me={"DEXTERITY":"1d6+SPEED","SPEED":2,"skills":{"stealth":3}}
> think roll(1dF+DEXTERITY+skills.stealth,v(stats),text)
1dF[-1] + DEXTERITY(1d6[5] + SPEED(2)) + skills.stealth(3) = 9
```

Every name, however deep, is looked up from the top of `<data>`. A value that leads back to itself is an error rather than a roll that never ends, and each use of a name rolls it again. Names that look like dice, such as `d6`, are read as dice.

## Options

`<options>` is one word, or a JSON object with `output` and `mode`:

total    the total (the default)<br>
text     one line: each dice term followed by its dice in square brackets, then `=` and the total<br>
json     every term and die, for a program to read<br>
min      the lowest total the formula can come to<br>
max      the highest total the formula can come to

`{"output":"text","mode":"max"}` gives the text of the highest roll. min and max follow the arithmetic, so the lowest of `1d20-1d4` takes the d20's 1 and the d4's 4. They leave out explosions and rerolls, so `2d6x` reaches at most 12. A formula that could divide by zero has no range. Through abs() and %, min and max are limits the roll may not reach, and the text shows only the number.

In text, each die is marked: `!` exploded, `*` a success, `_` a failure, and a die in round brackets was dropped or rerolled and does not count.

```sharp
> think roll(2d6kh1+1,,json)
{"formula":"2d6kh1+1","total":6,"text":"2d6kh1[5, (2)] + 1 = 6","dice":[{"formula":"2d6kh1","faces":6,"total":5,"results":[{"result":5,"active":true},{"result":2,"active":false,"dropped":true}]}]}
> think roll(4d6kh3,,max)
18
> think roll(1d20-1d4,,min)
-3
```

Output: in JSON, `faces` is the number of sides, `F` for fate dice, `C` for coins or `pool`, and `flavor` is a term's label. A result's `active` is false when it does not count, with `dropped` or `rerolled` saying why; `exploded`, `success` and `failure` appear only when true.
