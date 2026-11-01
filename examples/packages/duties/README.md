# duties

`+duty`: short adventures a party plays through together. The party goes from challenge to challenge;
at each one everyone rolls, and how the party did picks the way on. Rewards come on the way and at the end.

Players write duties and submit them. A duty reviewer approves them or returns them with notes, and
staff choose which approved duties are on the active roster, and when.

Duties do not roll dice themselves. A **sheet system** bridge, such as `duties-d20` or `duties-fate`,
supplies the traits, the roll and the rewards it can give. Without one, duties can be written but not
checked or played.

The [`duties-app`](../duties-app/) package adds the portal page `/apps/duties`, where duties are written
on a map of their steps and reviewed. `duties` serves its routes on the HTTP handler.

## Playing

| Command | What |
|---|---|
| `+duty` | Your party's duty, or the duties you can start and the parties gathering |
| `+duty <duty>` | One duty, with the rewards you have received and the challenges you have seen |
| `+duty/start <duty>` | Gather a party |
| `+duty/join <number>` | Join a party that is gathering |
| `+duty/go` | Leader: set out, or go on from a narration |
| `+duty/try <option>` | Roll at a challenge; `improvise` is always there |
| `+duty/leave` | Leave the party |
| `+duty/force <name>` | Leader: roll Improvise for a member who has not rolled |
| `+duty/kick <name>` | Leader: send a member away; the challenge goes on without their roll |
| `+duty/abandon` | Leader: end the duty with no rewards |
| `+duty/info <duty>` | What you have learned from it |

There are no timers. The leader keeps a party moving with `/force` and `/kick`; when the leader leaves,
the longest-standing member leads, and the duty ends when nobody is left.

## How a challenge resolves

- Each round, every member rolls one open option, or Improvise.
- An option succeeds after its `successes` and is lost after its `failures` (1 each by default).
  While an option rolled this round is neither, everyone rolls another round.
- Each member's result is their last roll; a success on a lost option counts as a failure. The party's
  result is the `majority` of them (or the `best` or `worst`, per challenge).
- The result picks the port: `crit-success`, `partial` and `crit-fail` fall back to `success` or `fail`
  when the challenge has no link of its own there. Links whose settings all hold form the pool; one link
  is a fixed way, several are a choice: the steps the fewest members have seen, then by weight.
- Improvise rolls the system's fallback trait, or the first trait everyone may use, one difficulty step
  harder than the hardest option.

## Writing

`+duty/create`, `/set`, `/node`, `/option`, `/link`, `/reward`, `/steps`, `/check`, `/submit`,
`/withdraw`, `/discard`, `/delete`, `/mine`. Changing an approved duty makes a draft beside it; parties
keep playing the approved revision until the draft is approved. `+help duty writing` has the details.

## Reviewing

`softcode.duties.review`, held by the `duty-reviewer` role: `+duty/review`, `/approve`, `/return`,
`/note`, `/retire`, `/restore`, `/roster`, `/window`, `/end`.

## Writing a sheet system

A bridge package attaches `` PROVIDER`<NAME> `` to `{{duties/duties}}`, holding its object. The object
answers `` FN`DUTIES`TRAITS ``, `` FN`DUTIES`CHECK ``, `` FN`DUTIES`REWARDS `` and `` ON`DUTIES`GRANT ``,
and optionally `` FN`DUTIES`TRAINED ``, `` FN`DUTIES`CAN `` and `` FN`DUTIES`FALLBACK ``. Every call gets
its arguments by name (`%<roller>`, `%<trait>`, `%<difficulty>`, `%<mods>`, `%<context>`, `%<reward>`).
`+help duty systems` describes each.

## Storage

Everything lives on the Duty Store, inside the Duties object: `` DUTY`<ID> `` with its revisions
(`` R<n> ``), the roster (`` ROSTER`<ID> ``), parties (`` RUN`<n> ``), and per character, keyed by objid:
`` SEEN ``, `` GOT `` (rewards received), `` DONE `` and `` INRUN ``.
