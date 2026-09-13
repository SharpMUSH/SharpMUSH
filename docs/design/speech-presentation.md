# Speech presentation

Local speech uses `CommunicationService.SpeechAsync` and the same Speech-lock admission and
location delivery as immediate emits. The location itself and its player/thing contents receive
the audience message; SAY sends its separate self-message once and excludes the speaker from
audience delivery. LOUD and Speech failure attributes use the common admission path.

`SpeechService` evaluates inherited SPEECHMOD once after admission. `%0` is the body and `%1`
is `"` for SAY, `:` for POSE, `;` for SEMIPOSE/POSE/NOSPACE, or `|` for immediate EMIT.
With `chat_strip_quote` enabled, SAY removes one initial quote before constructing `%0`.
NOSPACE preserves leading spaces in the body. Private, remote, zone and outermost emits do not
run SPEECHMOD. A spoofed immediate emit uses the executor's attribute and evaluation identity,
while the selected speaker supplies the Speech-lock identity and output attribution.

The transformation localizes q-register frames and retains the caller's shared budget, counters
and restrictions. Missing or empty expansion keeps the original body. Ordinary function error
text remains a nonempty expansion; syntax/control errors, restricted evaluation and cancellation
are not converted into successful original speech. SAY reuses the transformed body for both copies.

## Name contexts

`NameFormatter` supplies a single implementation for explicit names, automatic speech names and
final notification headers. Cosmetic attributes are literal templates, not evaluated softcode.

| Surface | Accent | MONIKER style | FULL_INVIS |
|---|---|---|---|
| `%n` / `%N` | No | No | No |
| `%~` | NAMEACCENT | No | No |
| `%k` / `%K` | No | Yes | No |
| `moniker()` | NAMEACCENT | Yes | No |
| Automatic local speech | NAMEACCENT | When `monikers` is true | DarkLegal replacement |
| Ordinary NOSPOOF header | No | No | DarkLegal replacement |
| PARANOID header | No | No | No; actual identities |

The substitution visitor retains responsibility for uppercase-selector capitalization.
SharpMUSH's boolean `monikers` enables automatic local speech styling for supported speaker types;
it is not PennMUSH's numeric context/type bitmask. Explicit substitutions/functions ignore this switch.

NAMEACCENT requires matching UTF-16 lengths and uses the same mapping as `accent()`. Unsupported
pairs stay unchanged; mismatched templates keep the actual name. MONIKER supplies positional
markup over the actual/accented name, never an alias. Plain or empty templates have no effect.
Longer names inherit the final template position's style; shorter names truncate styles. Each
whole grapheme selects the style at its UTF-16 starting position, preventing ANSI or HTML markup
from splitting surrogate pairs or combining sequences. This Unicode policy replaces Penn's byte
indexing. Sharp's markup model has no standalone style on empty text.

FULL_INVIS requires DARK plus Can_Dark or a nonliving object. Players, puppets and audible objects
with a local root FORWARDLIST are living for this check, even if its value is empty. Inherited
FORWARDLIST and nested attribute-tree leaves do not confer life. The shared IsAlive/IsDarkLegal
helpers inspect attribute names without evaluating their values. Hidden legal players appear as Someone; other hidden
legal speakers appear as Something. Explicit name operations remain unaffected.

## Final-recipient attribution

NOSPOOF and PARANOID include the recipient's owner's flags. NOSPOOF activates a header; PARANOID
alone does not. Ordinary headers are `[Name:] ` and omit self-attribution. PARANOID retains self
attribution and uses `[Name(#N)] ` or `[Owner(#O)'s Name(#N)] `. Authorized NS notification types
suppress headers. Command permission checks choose those types; a denied NS request uses ordinary
delivery. Administrative Announce remains undecorated.

HTTP capture and listener matching see the raw body before recipient headers. Headers are plain
markup fragments combined with the styled body before output framing. Empty prompts stay empty.
Object delivery shares prepared output across that object's connections; handle arrays cache it
per recipient while sharing undecorated serialization. Handle routes capture and recheck character
and session around asynchronous preparation and carry the captured session to transport publication.

Puppet relay publishes directly, so it invokes the same header formatter at its own publication
boundary. Puppet NOSPOOF/PARANOID flags force the effective owner header even if the owner lacks
those flags. Ordering is `Puppet> `, header, body; NS suppression and captured owner-binding checks
remain in force. Relay does not recursively invoke Notify or repeat listener processing.

Existing notifier constructor overloads retain their default configuration behavior. Production DI
provides configured options. The communication constructor's existing call shape is retained;
custom constructors must supply the new lazy speech service to opt into transformations and names.
Custom `ICommunicationService` implementations must implement the new `SpeechAsync` operation.
