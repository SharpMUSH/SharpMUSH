# Emit routing

The fourteen emit/prompt commands and their fourteen function counterparts delegate to
one `ICommunicationService.EmitWithOutcomeAsync` core. `EmitAsync` delegates to it and retains
the function result contract. Wrappers evaluate arguments and select list, silent,
no-spoof and explicit spoof options. `EmitRequest` carries the resulting intent.
Custom service implementations must supply the outcome method; an empty result alone cannot
establish admission.

| Scope | Recipients and gates |
| --- | --- |
| Immediate | Executor's immediate location and its contents; Speech lock |
| Outermost | Outermost room and its contents; mobile executors only; Speech lock |
| Room | Each named container and its contents; Page/HAVEN and Speech locks |
| Omit | Explicit container or distinct locations of excluded objects; Speech lock per location |
| Zone | Rooms belonging to a controlled zone; Speech lock per room |
| Private | Located objects, or privileged descriptor recipients; Page/HAVEN for objects |
| Prompt | Located objects through the Prompt protocol method; Page/HAVEN |

`SendToRoomAsync` remains the contents-only operation used by movement and other callers.
The emit core includes the location object, so an object in an inventory can notify its carrier.
Recipient exclusions compare DBRefs, not object-wrapper identity.

Target resolution, spoof authorization and recipient Interact-lock checks use the executor.
Page/HAVEN admission, including Wizard/Pemit_All bypasses and Page failure actions, uses the
selected speaker. Speech checks the speaker too, while Speech refusal actions target the executor.
Explicit `/spoof`, when authorized,
selects the enactor as speaker for Speech locks, reality hearing and output attribution.
NS variants suppress recipient NOSPOOF tagging when permitted; they do not implicitly select
the enactor. LOUD on the speaker bypasses Speech locks.

Explicit failed Speech locks use the generic `SPEECH_LOCK` failure attribute triad
(`SPEECH_LOCK` + backtick + `FAILURE`, `OFAILURE`, `AFAILURE`). Implicit OEMIT and zone
fanout filter denied locations without running a refusal for every room. Page refusals use
FailLock too. List private output suppresses the default refusal while retaining custom Page
failure attributes. Ordinary empty private messages do nothing; empty prompts publish a prompt
message and reach the protocol callback with an empty body.

EMIT, NSEMIT, NSOEMIT and NSPROMPT commands return the attempted message only after admission.
A location passes admission before recipient hearing filters, even if nobody ultimately hears it;
private output needs at least one target to pass lookup, Page/HAVEN and hearing checks. Lock
refusals return empty. Lookup failures take precedence in the payload-command result without
preventing delivery to other admitted targets. NSPROMPT retains the first lookup failure's
CallState; unresolved explicit NSOEMIT locations return InvalidRoom. These lookup results retain
their existing error metadata. Functions retain empty results for these refusals; explicit
noncontainer OEMIT locations return InvalidRoom with HadErrors on both surfaces.

Missing personal Speech/Page failure attributes use a resource key resolved separately for each
connection, including two connections on the same player with different locales. Custom failure
attributes still evaluate once; present-empty attributes suppress the default, and O/A failure
attributes still run. HTTP capture receives the neutral default through the normal notification path.
Room triad defaults remain literal pending the separate callback work in #1006.

`@emit/room` and `@nsemit/room` select the outermost room, like LEMIT. Both aliases accept
`/silent` and `/noisy` to override `silent_pemit` for their confirmation. This is SharpMUSH's
documented ROOM alias behavior. `@pemit/contents`
selects a container and its contents, like REMIT, retaining spaces in the target name even with
`/list`. `@pemit/spoof` selects an authorized speaker for ordinary private or contents output.

PEMIT descriptor routing requires privilege and an active positive descriptor. Functions infer
descriptors from an all-integer target list; a mixed numeric/DBRef list remains object matching.
Commands require explicit `/port`, so numeric object names remain addressable. SharpMUSH also
supports `/port` on NSPEMIT. PORT takes precedence over CONTENTS and uses executor attribution;
CONTENTS takes precedence over LIST. Neither branch inherits private-object-list implicit silence.
Private/list defaults to silent unless `/noisy` is supplied;
other command confirmation defaults follow `silent_pemit`, with `/silent` and `/noisy` overrides.

PEMIT/NSPEMIT and REMIT/NSREMIT functions suppress confirmations. PROMPT/NSPROMPT,
LEMIT/NSLEMIT and ZEMIT/NSZEMIT retain PennMUSH's confirmation behavior.

OEMIT retains PennMUSH's maximum of ten matched exclusions. This is a recipient-selection
contract, unrelated to the obsolete 8,192-character output buffers. Explicit location syntax
with no matching exclusions still emits to that location; implicit unmatched lists report failure.
Explicit-location exclusions accept quoted English ordinals and `*Player` names, but only immediate
members count toward the exclusion limit. Player and thing containers are valid locations; exits
produce the localized invalid-room diagnostic and an error result without delivering the message.

Private object output uses PrivateEmit/NSPrivateEmit notification intents. Both activate LISTEN
and MONITOR and force puppet relay even when the owner is colocated. Descriptor output retains
its announcement intent. Administrative Announce/NSAnnounce notifications remain inert.

Notify and Prompt share recipient admission and listener preparation. HTTP capture retains its
Notify-only behavior before listener routing. Direct prompts carry MarkupPromptMessage without
output prefix/suffix wrapping. Prompt framing is not inherited by puppet relays or queued actions.
Puppet relay retains styled text, owner reality checks and connection-binding validation.

Listener reactions await queue admission, then execute as independent command lists. The listener
is executor and the speaker is enactor/caller; command text and positional captures are snapshots.
Wildcard captures start at %0; regexp %0 is the whole match. Both preserve markup, honor CASE,
and use the same capture implementation as command discovery. MONITOR actions start after the
parsed pattern separator, including escaped colons and paired backslashes.

LISTEN is local to the object and matches even a present-empty pattern. Its actions may inherit;
a local empty action suppresses inheritance. AHEAR runs for another speaker, AMHEAR for self,
and AAHEAR additionally. PlayerListen gates private player LISTEN/MONITOR; PlayerAHear gates
only player AHEAR/AMHEAR/AAHEAR. The existing propagated speech path bypasses PlayerListen.
MONITOR respects HALT. Queue rejection does not execute a reaction inline, and restrictions and
cancellation cannot turn it into an unrestricted callback.

Existing listener constructors and the string matcher entry point remain available. Custom
matchers must override the markup overload to preserve styling in captures; its compatibility
default retains their plain string captures. General queued-action HALT enforcement remains
part of #1006. Empty prompt delivery does not establish the other lifecycle ordering proposed
in #1010.
Forwarding matched LISTEN messages into contents with INFILTER/INPREFIX and `@lock/infilter` semantics remains
[issue #1048](https://github.com/SharpMUSH/SharpMUSH/issues/1048).
