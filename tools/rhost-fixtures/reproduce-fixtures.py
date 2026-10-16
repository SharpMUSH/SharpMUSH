#!/usr/bin/env python3
"""Offline fixture recipe. Requires existing clang/make/OpenSSL/PCRE/crypt headers/libs.
No dependency installation, server startup, production inputs, or network calls.
Usage: python3 reproduce-fixtures.py PINNED_SOURCE SAMPLE_FLAT OUTPUT_DIRECTORY
OUTPUT_DIRECTORY must not already exist. Build original remains unchanged.
"""
from pathlib import Path
import gzip
import hashlib
import json
import os
import platform
import shutil
import subprocess
import sys
import struct

revision = 'bfd2894c7239d02b8bec15c36939706be371715c'
source, sample, output = map(lambda p: Path(p).resolve(), sys.argv[1:])
if subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip() != revision:
    raise SystemExit('Source must be the pinned Rhost revision: ' + revision)
if subprocess.check_output(['git', '-C', str(source), 'status', '--porcelain']):
    raise SystemExit('Original upstream source must remain clean')
if platform.system() != 'Linux' or platform.machine() != 'x86_64' or sys.byteorder != 'little' or struct.calcsize('P') != 8:
    raise SystemExit('This build profile supports only Linux x86_64 little-endian')
output.mkdir()
scratch = output / 'scratch'
scratch.mkdir()
env = dict(os.environ, TMPDIR=str(scratch), TMP=str(scratch), TEMP=str(scratch))
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def run(args, cwd, log):
    with log.open('wb') as stream:
        subprocess.run(args, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT, check=True, env=env)
def convert(exe, base, flag, inp, out, log):
    with inp.open('rb') as i, out.open('wb') as o, log.open('wb') as e:
        subprocess.run([str(exe), str(base), flag], stdin=i, stdout=o, stderr=e, check=True, env=env)
# Existing sample uses visible escapes. Upstream requires actual SOH metadata
# and CRLF continuation bytes. Add a Unicode sentinel to the known description.
seed = sample.read_bytes().replace(b'\\u0001', b'\x01').replace(b'first line\\r\\nsecond line', b'first line\r\nsecond line').replace(b'A shiny widget%r', 'A shiny widget — café 雪%r'.encode())
# Offline world cannot retain the transient CONNECTED flag on Wizard.
seed = seed.replace(b'1000\n19\n1174405120\n', b'1000\n19\n100663296\n')
(output / 'seed.flat').write_bytes(seed)
manifest = {'revision': revision, 'architecture': platform.machine(), 'system': platform.platform(),
 'toolchain': {'clang': subprocess.check_output(['clang', '--version'], text=True).splitlines()[0], 'make': subprocess.check_output(['make', '--version'], text=True).splitlines()[0]},
 'sample_sha256': sha(sample), 'seed_sha256': sha(output / 'seed.flat'),
 'converter_build_repair': 'QDBM only: add #include "externs.h" after debug.h in udb_ocache.c; pinned source omits rhost_time declaration.',
 'backends': {}}
for backend in ('legacy', 'mdbx'):
    build = output / ('build-' + backend)
    shutil.copytree(source / 'Server', build / 'Server', symlinks=True)
    server = build / 'Server'
    run(['bash', 'asksource.sh', 'default'], server / 'bin', build / 'configure.log')
    defs = server / 'src/custom.defs'
    if backend == 'mdbx':
        defs.write_text(defs.read_text().replace('-DQDBM', '-DMDBX').replace('-L../src/qdbm/ -lqdbm', '-L../src/mdbx/ -lmdbx'))
        (server / 'src/do_compile.var').write_text('COMP=mdbx\n')
    else:
        cache = server / 'src/udb_ocache.c'
        cache.write_text(cache.read_text().replace('#include        "debug.h"', '#include        "debug.h"\n#include "externs.h"'))
    run(['bash', 'do_compile.sh'], server / 'src', build / 'library-build.log')
    run(['make', 'rundate'], server / 'src', build / 'date-build.log')
    run(['make', '-j2', '../bin/dbconvert'], server / 'src', build / 'converter-build.log')
    exe = server / 'bin/dbconvert'
    fixture = output / backend
    fixture.mkdir()
    profile = {'schema': 1, 'upstreamRevision': revision,
      'backend': 'legacy-qdbm' if backend == 'legacy' else 'mdbx',
      'rid': 'linux-x64', 'littleEndian': True,
      'pointerSize': 8, 'intSize': 4, 'offTSize': 8, 'exporterSha256': sha(exe)}
    (fixture / 'profile.json').write_text(json.dumps(profile, indent=2) + '\n')
    convert(exe, fixture / 'netrhost.gdbm', 'X', output / 'seed.flat', fixture / 'netrhost.db', fixture / 'load.log')
    native_paths = [p for p in fixture.iterdir() if p.name.startswith('netrhost') and not p.name.endswith('-lck')]
    before = {p.name: sha(p) for p in native_paths}
    copied = output / (backend + '-export-copy')
    shutil.copytree(fixture, copied)
    convert(exe, copied / 'netrhost.gdbm', 'x', copied / 'netrhost.db', fixture / 'export.flat', fixture / 'export.log')
    assert before == {p.name: sha(p) for p in native_paths}, 'export changed immutable fixture source'
    exported = (fixture / 'export.flat').read_bytes()
    for sentinel in ('café 雪'.encode(), b'\x011:32768:', b'first line\r\nsecond line', b'Thu Oct 15 03:55:58 2026', b'!10\n', b'***END OF DUMP***\n'):
        assert sentinel in exported, repr(sentinel)
    if backend == 'mdbx':
        assert exported == (output / 'legacy/export.flat').read_bytes()
    for p in native_paths:
        (fixture / (p.name + '.gz')).write_bytes(gzip.compress(p.read_bytes(), mtime=0))
    manifest['backends'][backend] = {'custom_defs': defs.read_text(), 'converter_sha256': sha(exe),
      'export_sha256': sha(fixture / 'export.flat'), 'native': {p.name: {'bytes': p.stat().st_size, 'sha256': sha(p), 'gzip_bytes': (fixture / (p.name + '.gz')).stat().st_size} for p in native_paths}}
if subprocess.check_output(['git', '-C', str(source), 'status', '--porcelain']):
    raise SystemExit('Original upstream source must remain clean')
(output / 'provenance.json').write_text(json.dumps(manifest, indent=2) + '\n')
print(output / 'provenance.json')
