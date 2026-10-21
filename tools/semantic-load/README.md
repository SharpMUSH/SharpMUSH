# Semantic wiki load measurements

Recorded measurements, not gates: nothing here passes or fails on a number. See
[the report](../../docs/superpowers/reports/2026-10-21-semantic-wiki-measurements.md) for the method and the
results these tools produced.

- `generate.py` builds an N-times dataset from an extracted PF2e dataset (`tools/WikiSemantics dry-run`
  output): every page and fact copied N times under new ids, 99.9% of pages in a category a read rule closes
  to non-administrators, and one hot target referenced by every subject. It writes a checksum manifest.
- `run-fixture.sh <dataset> <label> <output-root> [seconds] [rates...]` builds an issued fixture world from
  the dataset in one test process (`SemanticLoadMeasurement`, mode `build`), then measures each rate in its
  own fresh process (mode `measure`): a pass straight after process start, then a warm pass, each
  `seconds` long, with commands through the real command queue alongside and one rebuild during each warm
  pass. Semantic queries only ever run against these fixture worlds.
- `command_load.py` measures ordinary command latency on a disposable SharpMUSH built the parity harness's
  way, with wiki reads off, and samples the server's memory, page faults and `/metrics`.

Every process these start is stopped at the end, and all scratch goes under the given directories. The OS
page cache is never dropped: a first pass is "first run after process start, page cache not dropped".
