# Emit routing

The fourteen emit/prompt commands and their fourteen function counterparts delegate to
`ICommunicationService.EmitAsync`. Wrappers evaluate arguments and select list, silent,
no-spoof and explicit spoof options. `EmitRequest` carries the resulting intent.

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

The executor owns targeting and interaction-lock checks. Explicit `/spoof`, when authorized,
selects the enactor as speaker for Speech locks, reality hearing and output attribution.
NS variants suppress recipient NOSPOOF tagging when permitted; they do not implicitly select
the enactor. LOUD on the speaker bypasses Speech locks.

Explicit failed Speech locks use the generic `SPEECH_LOCK` failure attribute triad
(`SPEECH_LOCK` + backtick + `FAILURE`, `OFAILURE`, `AFAILURE`). Implicit OEMIT and zone
fanout filter denied locations without running a refusal for every room. Page refusals use
FailLock too. List private output suppresses the default refusal while retaining custom Page
failure attributes. Ordinary empty private messages do nothing; empty prompts retain the
protocol boundary.

PEMIT descriptor routing requires privilege and an active positive descriptor. An all-integer
target list selects descriptors; a mixed numeric/DBRef list remains object matching. `/port`
selects descriptor mode explicitly. Private/list defaults to silent unless `/noisy` is supplied;
other command confirmation defaults follow `silent_pemit`, with `/silent` and `/noisy` overrides.

OEMIT retains PennMUSH's maximum of ten matched exclusions. This is a recipient-selection
contract, unrelated to the obsolete 8,192-character output buffers. Explicit location syntax
with no matching exclusions still emits to that location; implicit unmatched lists report failure.

Private-output listener/puppet propagation is tracked separately by issue #1041. This change
preserves ordinary/NS private notification parity and the distinct Prompt protocol path.
