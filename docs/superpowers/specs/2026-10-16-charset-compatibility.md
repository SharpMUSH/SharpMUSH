# Charset compatibility: approved lane contract

Issue: [#1798](https://github.com/SharpMUSH/SharpMUSH/issues/1798).
This is the charset lane of the approved 2026-10-16 open issue consolidation, independently based
on main `38bab40c33ce362fe147b9b1d209367a02193b55`.

Advertise exactly UTF-8, ISO-8859-1 and US-ASCII in that preference order. For valid selections,
interpreter decoding, connection charset metadata, renderer selection and actual output bytes
must agree. Canonical encoding aliases are permitted. Reject unsupported/unoffered acceptance
before changing encoding, or terminate safely without later mismatched input/output. Exercise
fragmented and coalesced protocol frames, rejection, aliases and peer-initiated REQUEST.
GMCP independently uses UTF-8 for both directions regardless of the terminal text charset.

Use supported dependency configuration. Do not expand the renderer to arbitrary .NET encodings,
reflect over private dependency state, or add a second raw-wire parser. A bounded fix to the user's
owned TelnetNegotiationCore repository is approved when its acceptance path cannot enforce the
contract through its public configuration. No merge, tag, publication or deployment is included.
Keep the SharpMUSH PR draft until a reviewed dependency package is available and the production
pin has been updated and verified. A locally packed test dependency proves integration only.
