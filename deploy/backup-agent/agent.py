"""Single-purpose Restic worker. No shell, arbitrary arguments, source paths or log receipts."""
import datetime
import fcntl
import uuid
import json
import os
import re
import subprocess
import time
import threading
import urllib.error
import urllib.request
from pathlib import Path

HEX = re.compile(r'^[a-f0-9]{64}$')
STATE = Path(os.environ.get('BACKUP_AGENT_STATE', '/agent-state'))
LOCK_FD = None
WORKER_ID = None

def snapshot_id(line):
    try:
        value = json.loads(line)
        candidate = value.get('snapshot_id')
        return candidate if value.get('message_type') == 'summary' and isinstance(candidate, str) and HEX.fullmatch(candidate) else None
    except (ValueError, AttributeError):
        return None

def copy_path(path, job_id):
    identifier = job_id.replace('-', '')
    prefix = '/app/data/backup.remote-jobs/' + identifier + '/'
    if not re.fullmatch(r'[a-f0-9]{32}', identifier) or not path.startswith(prefix):
        raise ValueError('invalid-copy-path')
    suffix = path[len(prefix):]
    if not re.fullmatch(r'[a-zA-Z0-9_.-]+', suffix) or suffix.startswith('.'):
        raise ValueError('invalid-copy-path')
    return '/data/backup.remote-jobs/' + identifier + '/' + suffix

def receipt_for(job, code, snapshot):
    success = code == 0 and snapshot is not None and HEX.fullmatch(snapshot)
    return dict(id=job['id'], nonce=job['nonce'], exitCode=code,
                snapshotId=snapshot if success else None,
                errorCode=None if success else 'partial-backup' if code == 3 else 'restic-failed')

def environment(job):
    # Inherited RESTIC_* variables can execute helper commands or select another repository.
    env = {key: value for key, value in os.environ.items()
           if key in ('PATH', 'HOME', 'TMPDIR', 'TMP', 'TEMP', 'SSL_CERT_FILE', 'SSL_CERT_DIR')}
    env.update(RESTIC_REPOSITORY=job['repository'], RESTIC_PASSWORD=job['password'],
               RESTIC_CACHE_DIR=str(STATE / 'cache'))
    if job.get('accountId'):
        env.update(B2_ACCOUNT_ID=job['accountId'], AWS_ACCESS_KEY_ID=job['accountId'], RESTIC_REST_USERNAME=job['accountId'])
    if job.get('accountKey'):
        env.update(B2_ACCOUNT_KEY=job['accountKey'], AWS_SECRET_ACCESS_KEY=job['accountKey'], RESTIC_REST_PASSWORD=job['accountKey'])
    return env

def run_restic(job):
    source = copy_path(job['copyPath'], job['id'])
    if not Path(source, 'data.mdb').is_file():
        return receipt_for(job, -1, None)
    tag = 'sharpmush-job:' + job['id']
    env = environment(job)
    deadline = datetime.datetime.fromisoformat(job['deadline'].replace('Z', '+00:00'))
    remaining = max(1, min(6600, (deadline - datetime.datetime.now(datetime.timezone.utc)).total_seconds() - 30))
    if job.get('reconcileOnly'):
        # The inherited process lock proves no previous Restic child is still reading.
        # An existing snapshot cannot prove exit zero (partial snapshots exist).
        return receipt_for(job, -2, None)
    sources = [source]
    for fixed in ('/data/wiki-assets', '/data/lightning.dataprotection-keys', '/data/lightning.plugins'):
        if Path(fixed).exists():
            sources.append(fixed)
    argv = ['restic', 'backup', '--json', '--tag', 'sharpmush', '--tag', tag, '--host', 'sharpmush', '--'] + sources
    # Stream stdout to bound memory even for a large world. Never log filenames or credentials.
    with subprocess.Popen(argv, env=env, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, pass_fds=(LOCK_FD,) if LOCK_FD is not None else ()) as process:
        import threading
        timed_out = threading.Event()
        def stop():
            timed_out.set()
            process.kill()
        timer = threading.Timer(remaining, stop)
        timer.start()
        snapshot = None
        try:
            for line in process.stdout:
                snapshot = snapshot_id(line) or snapshot
            code = process.wait()
        finally:
            timer.cancel()
        return receipt_for(job, -1 if timed_out.is_set() else code, snapshot)

def request(route, body):
    url = os.environ['SHARPMUSH_BACKUP_AGENT_URL'].rstrip('/') + '/api/backup-agent/' + route
    token = os.environ['SHARPMUSH_BACKUP_AGENT_TOKEN']
    if len(token) < 32:
        raise ValueError('agent-token-too-short')
    req = urllib.request.Request(url, data=json.dumps(body).encode(), method='POST',
        headers={'Content-Type': 'application/json', 'X-Backup-Agent-Token': token, 'X-Backup-Agent-Id': WORKER_ID})
    # Never forward the deployment token through redirects.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self, *args):
            return None
    with urllib.request.build_opener(NoRedirect).open(req, timeout=30) as response:
        return json.loads(response.read(512 * 1024)) if response.status != 204 else None

def save_receipt(receipt):
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    temporary = STATE / 'receipt.incoming'
    with temporary.open('w') as stream:
        os.chmod(temporary, 0o600)
        json.dump(receipt, stream)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(STATE / 'receipt.json')

def main():
    os.umask(0o077)
    global LOCK_FD, WORKER_ID
    STATE.mkdir(mode=0o700, parents=True, exist_ok=True)
    (STATE / 'tmp').mkdir(mode=0o700, exist_ok=True)
    # Children inherit this lock. A crashed agent cannot cause a second process to finish
    # and clean up its job while Restic is still reading the copy.
    lock = (STATE / 'worker.lock').open('a')
    fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    LOCK_FD = lock.fileno()
    identity = STATE / 'worker-id'
    if not identity.exists():
        identity.write_text(uuid.uuid4().hex)
    WORKER_ID = identity.read_text().strip()
    if not re.fullmatch(r'[a-f0-9]{32}', WORKER_ID):
        raise ValueError('invalid-worker-id')
    def heartbeat():
        while True:
            time.sleep(30)
            try:
                request('heartbeat', {})
            except Exception:
                pass
    threading.Thread(target=heartbeat, daemon=True).start()
    while True:
        try:
            pending = STATE / 'receipt.json'
            if pending.exists():
                request('receipt', json.loads(pending.read_text()))
                pending.unlink()
            job = request('claim', {})
            if job:
                try:
                    receipt = run_restic(job)
                except (OSError, ValueError, subprocess.SubprocessError):
                    receipt = receipt_for(job, -1, None)
                save_receipt(receipt)
                request('receipt', receipt)
                pending.unlink(missing_ok=True)
        except Exception as error:
            # Exception messages may contain endpoint credentials. Only emit a stable type.
            print('backup-agent retry: ' + type(error).__name__, flush=True)
        time.sleep(10)

if __name__ == '__main__':
    main()
