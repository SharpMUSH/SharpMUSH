"""SharpMUSH update agent.

Holds the Docker socket so the game server never does. It polls the server, reports what each
component runs and what the registry offers, and carries out updates an administrator queued in the
portal: `docker compose pull` and `up -d --no-deps` for the renderer, the server and the socket server,
in that order, nothing else. Every command is a fixed argument list; nothing goes through a shell.
"""
import datetime
import json
import os
import re
import subprocess
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path

PROTOCOL = 1
PULL_STEP = 'pull'  # ServerComponents.PullStep
SERVER = os.environ.get('SHARPMUSH_UPDATE_AGENT_URL', 'http://sharpmush-server:8080').rstrip('/')
TOKEN = os.environ.get('SHARPMUSH_UPDATE_AGENT_TOKEN', '')
STATE = Path(os.environ.get('UPDATE_AGENT_STATE', '/agent-state'))
POLL_SECONDS = max(5, int(os.environ.get('UPDATE_AGENT_POLL_SECONDS', '10')))
REGISTRY_SECONDS = max(300, int(os.environ.get('UPDATE_AGENT_REGISTRY_SECONDS', '900')))
READY_SECONDS = max(30, int(os.environ.get('UPDATE_AGENT_READY_SECONDS', '600')))
SOCKET_HTTP_PORT = int(os.environ.get('UPDATE_AGENT_SOCKET_HTTP_PORT', '4202'))
MAX_SOCKET_DELAY = datetime.timedelta(seconds=1800)
REGISTRY = os.environ.get('UPDATE_AGENT_REGISTRY', 'https://registry-1.docker.io').rstrip('/')
AUTH = os.environ.get('UPDATE_AGENT_REGISTRY_AUTH', 'https://auth.docker.io/token')
TAG_VARIABLE = 'SHARPMUSH_IMAGE_TAG'

# The order an update replaces components in. The socket server owns every connection, so it goes last.
ORDER = ['renderer', 'server', 'socketserver']
IMAGES = {
    'sharpmush/sharpmush-renderer': 'renderer',
    'sharpmush/sharpmush-server': 'server',
    'sharpmush/sharpmush-socketserver': 'socketserver',
}
TAG = re.compile(r'^(\d{1,4}\.\d{1,4}\.\d{1,6}(-[0-9A-Za-z.]{1,32})?|latest|dev)$')
RELEASE = re.compile(r'^(\d{1,4})\.(\d{1,4})\.(\d{1,6})(-[0-9A-Za-z.]{1,32})?$')
ID = re.compile(r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$')
MANIFEST_TYPES = ', '.join([
    'application/vnd.oci.image.index.v1+json',
    'application/vnd.docker.distribution.manifest.list.v2+json',
    'application/vnd.oci.image.manifest.v1+json',
    'application/vnd.docker.distribution.manifest.v2+json',
])


def now():
    return datetime.datetime.now(datetime.timezone.utc)


def iso(value):
    return value.isoformat().replace('+00:00', 'Z') if value else None


def timestamp(value):
    """Docker's RFC 3339 time with nanoseconds, cut to microseconds; None for Docker's zero time."""
    if not value or value.startswith('0001'):
        return None
    match = re.fullmatch(r'(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(\.\d+)?(Z|[+-]\d\d:\d\d)', value)
    if not match:
        return None
    return match[1] + (match[2] or '')[:7] + match[3]


def docker(*args, timeout=600):
    return subprocess.run(['docker', *args], text=True, capture_output=True, timeout=timeout, check=False)


def inspect(kind, *names):
    result = docker(kind, 'inspect', *names, timeout=60)
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or f'docker {kind} inspect failed')
    return json.loads(result.stdout)


def present(kind, names):
    """Inspects each of names that still exists. Compose removes a container while it replaces it."""
    result = docker(kind, 'inspect', *names, timeout=60) if names else None
    if result is None:
        return []
    try:
        # docker inspect prints what it found even when one of the names is gone.
        return json.loads(result.stdout or '[]')
    except ValueError:
        raise RuntimeError(result.stderr.strip() or f'docker {kind} inspect failed')


# --- the Compose project ---------------------------------------------------------------------------

class Project:
    """The Compose project this agent's own container belongs to, read from Compose's labels."""

    def __init__(self, labels):
        self.name = labels.get('com.docker.compose.project')
        self.directory = labels.get('com.docker.compose.project.working_dir')
        files = labels.get('com.docker.compose.project.config_files') or ''
        self.files = [path for path in files.split(',') if path]
        if not self.name or not self.directory or not self.files:
            raise RuntimeError('This container was not started by docker compose.')
        missing = [path for path in [self.directory, *self.files] if not Path(path).exists()]
        if missing:
            raise RuntimeError('The project directory is not mounted at its own path: ' + ', '.join(missing))

    def compose(self, *args, timeout=900):
        files = [argument for path in self.files for argument in ('-f', path)]
        return docker('compose', '-p', self.name, '--project-directory', self.directory, *files, *args, timeout=timeout)

    @property
    def env_file(self):
        return Path(self.directory) / '.env'

    def tag_from_env(self):
        """The tag the game follows, when its Compose files take it from SHARPMUSH_IMAGE_TAG; else None."""
        if not any(('${' + TAG_VARIABLE) in Path(path).read_text() for path in self.files):
            return None
        value = read_env(self.env_file).get(TAG_VARIABLE)
        if value:
            return value
        # The Compose file's default, as in ${SHARPMUSH_IMAGE_TAG:-1.0.0}.
        for path in self.files:
            found = re.search(r'\$\{' + TAG_VARIABLE + r':-([^}]+)\}', Path(path).read_text())
            if found:
                return found[1]
        return None


def own_project():
    hostname = os.environ.get('HOSTNAME', '')
    return Project(inspect('container', hostname)[0]['Config'].get('Labels') or {})


def read_env(path):
    values = {}
    if path.exists():
        for line in path.read_text().splitlines():
            if '=' in line and not line.lstrip().startswith('#'):
                key, value = line.split('=', 1)
                value = value.strip()
                if len(value) >= 2 and value[0] == value[-1] and value[0] in '"\'':
                    value = value[1:-1]
                values[key.strip()] = value
    return values


def write_tag(path, tag):
    """Sets SHARPMUSH_IMAGE_TAG in .env, keeping every other line and the file's mode."""
    if not TAG.fullmatch(tag):
        raise ValueError('not a tag')
    lines = path.read_text().splitlines() if path.exists() else []
    replaced = False
    for index, line in enumerate(lines):
        if line.startswith(TAG_VARIABLE + '='):
            lines[index] = f'{TAG_VARIABLE}={tag}'
            replaced = True
    if not replaced:
        lines.append(f'{TAG_VARIABLE}={tag}')
    write_env(path, '\n'.join(lines) + '\n')


def write_env(path, text):
    """Replaces .env with text in one step, keeping its mode and owner."""
    temporary = path.with_name('.env.update-agent')
    temporary.unlink(missing_ok=True)
    # Created 0600 before anything is written: .env holds the game's secrets.
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, 'w') as file:
        file.write(text)
    if path.exists():
        current = path.stat()
        os.chmod(temporary, current.st_mode & 0o777)
        try:
            os.chown(temporary, current.st_uid, current.st_gid)
        except PermissionError:
            # Only root may give a file away; the agent runs as root, so this is a non-root test run.
            pass
    os.replace(temporary, path)


def components(project):
    """Each SharpMUSH component in the project, keyed by component name."""
    listing = docker('ps', '-a', '--filter', f'label=com.docker.compose.project={project.name}',
                     '--filter', 'label=com.docker.compose.oneoff=False', '--format', '{{.ID}}', timeout=60)
    if listing.returncode != 0:
        raise RuntimeError(listing.stderr.strip() or 'docker ps failed')
    found = {}
    for container in present('container', listing.stdout.split()):
        reference = container['Config']['Image']
        repository, _, tag = reference.rpartition(':') if ':' in reference.rsplit('/', 1)[-1] else (reference, '', 'latest')
        repository = repository.removeprefix('docker.io/')
        component = IMAGES.get(repository)
        if component is None:
            continue
        # A stopped leftover never hides the running container of the same service.
        if component in found and found[component]['state'] == 'running':
            continue
        images = present('image', [container['Image']])
        if not images:
            continue
        image = images[0]
        labels = (image.get('Config') or {}).get('Labels') or {}
        digests = [entry.split('@', 1)[1] for entry in image.get('RepoDigests') or []
                   if entry.split('@', 1)[0].removeprefix('docker.io/') == repository]
        started = container['State'].get('StartedAt')
        found[component] = {
            'component': component,
            'service': container['Config']['Labels'].get('com.docker.compose.service', ''),
            'image': repository,
            'tag': tag,
            'version': labels.get('org.opencontainers.image.version'),
            'revision': labels.get('org.opencontainers.image.revision'),
            'runningDigest': digests[0] if digests else None,
            'latestDigest': None,
            'state': container['State'].get('Status', 'unknown'),
            'startedAt': timestamp(started),
            # The image's HEALTHCHECK verdict (starting, healthy, unhealthy), or None without one. Not reported.
            'health': (container['State'].get('Health') or {}).get('Status'),
        }
    return found


# --- the registry ----------------------------------------------------------------------------------

def registry_request(repository, path, method='GET'):
    token_url = f'{AUTH}?service=registry.docker.io&scope=repository:{repository}:pull'
    with urllib.request.urlopen(token_url, timeout=30) as response:
        token = json.load(response)['token']
    request = urllib.request.Request(f'{REGISTRY}/v2/{repository}/{path}', method=method,
                                     headers={'Authorization': f'Bearer {token}', 'Accept': MANIFEST_TYPES})
    return urllib.request.urlopen(request, timeout=30)


def remote_digest(repository, tag):
    with registry_request(repository, f'manifests/{tag}', 'HEAD') as response:
        return response.headers.get('Docker-Content-Digest')


def release_key(tag):
    match = RELEASE.fullmatch(tag)
    # A prerelease sorts below its release: 1.0.0-rc.1 < 1.0.0.
    return (int(match[1]), int(match[2]), int(match[3]), match[4] is None, match[4] or '')


def release_tags(repository):
    with registry_request(repository, 'tags/list?n=1000') as response:
        tags = json.load(response).get('tags') or []
    return sorted((tag for tag in tags if RELEASE.fullmatch(tag)), key=release_key, reverse=True)[:30]


# --- the agent -------------------------------------------------------------------------------------

class Agent:
    def __init__(self):
        self.lock = threading.Lock()
        self.job = load_json(STATE / 'job.json')
        self.done = list(load_json(STATE / 'done.json') or [])
        # 'checked' is None until the first read, so the first poll reads it whatever the clock says.
        self.registry = {'digests': {}, 'tags': [], 'readAt': None, 'checked': None}
        self.reading = threading.Lock()
        self.error = None

    def report(self, project):
        found = components(project) if project else {}
        checked = self.registry['checked']
        due = checked is None or time.monotonic() - checked >= REGISTRY_SECONDS
        if project and due and not self.reading.locked():
            # Docker Hub can be slow: the poll goes on without waiting for it.
            threading.Thread(target=self.read_registry, args=(found,), daemon=True).start()
        for status in found.values():
            status['latestDigest'] = self.registry['digests'].get(f"{status['image']}:{status['tag']}")
        with self.lock:
            job = dict(self.job) if self.job else None
        return {
            'protocol': PROTOCOL,
            'project': project.name if project else None,
            'tag': project.tag_from_env() if project else None,
            'components': [{key: value for key, value in found[name].items() if key != 'health'}
                           for name in ORDER if name in found],
            'availableTags': self.registry['tags'],
            'registryReadAt': self.registry['readAt'],
            'job': job,
            'error': self.error,
        }

    def read_registry(self, found):
        with self.reading:
            self.read_registry_now(found)

    def read_registry_now(self, found):
        self.registry['checked'] = time.monotonic()
        try:
            digests = {}
            for status in found.values():
                key = f"{status['image']}:{status['tag']}"
                digests[key] = remote_digest(status['image'], status['tag'])
            self.registry['tags'] = release_tags('sharpmush/sharpmush-server')
            self.registry['digests'] = digests
            self.registry['readAt'] = iso(now())
        except (urllib.error.URLError, OSError, ValueError, KeyError) as error:
            log(f'registry: {error}')

    def accept(self, job):
        """Starts a job the server handed over, once per id, after checking every field. True when it started."""
        identifier = job.get('id', '')
        with self.lock:
            busy = self.job and self.job.get('state') == 'running'
            if not isinstance(identifier, str) or not ID.fullmatch(identifier) or identifier in self.done or busy:
                return False
            wanted = job.get('components') or []
            tag = job.get('tag')
            socket_not_before = parse_time(job.get('socketNotBefore'))
            problem = None
            if not isinstance(wanted, list) or not wanted or any(name not in ORDER for name in wanted) or len(set(wanted)) != len(wanted):
                problem = 'Unknown component.'
            elif tag is not None and not (isinstance(tag, str) and TAG.fullmatch(tag)):
                problem = 'That is not a release tag.'
            elif job.get('socketNotBefore') is not None and socket_not_before is None:
                problem = 'That is not a time.'
            self.job = {'id': identifier, 'components': [name for name in ORDER if name in wanted], 'tag': tag,
                        'state': 'failed' if problem else 'running', 'step': None, 'message': problem,
                        'finishedAt': iso(now()) if problem else None}
            self.done = [*self.done, identifier][-200:]
            self.save()
        if problem:
            return False
        if socket_not_before is not None:
            socket_not_before = min(socket_not_before, now() + MAX_SOCKET_DELAY)
        threading.Thread(target=self.run, args=(socket_not_before,), daemon=True).start()
        return True

    def run(self, socket_not_before):
        try:
            project = own_project()
            found = components(project)
            missing = [name for name in self.job['components'] if name not in found]
            if missing:
                raise RuntimeError(f'No {missing[0]} container in project {project.name}.')
            services = {name: found[name]['service'] for name in self.job['components']}
            self.pull(project, services, found)
            for name, service in services.items():
                if name == 'socketserver' and socket_not_before:
                    wait_until(socket_not_before)
                self.step(name)
                compose(project, 'up', '-d', '--no-deps', service)
                # Nothing further is replaced behind a component that did not come back.
                self.wait_ready(project, name, service)
            self.finish('succeeded', None)
        except Exception as error:  # Reported to the portal; the agent keeps polling.
            self.finish('failed', str(error)[:500])

    def pull(self, project, services, found):
        """Moves .env to the job's tag and pulls every image before anything is replaced; on a failure .env goes back."""
        tag = self.job['tag']
        original = project.env_file.read_text() if project.env_file.exists() else None
        previous = project.tag_from_env() if tag is not None else None
        if tag is not None:
            if project.tag_from_env() is None:
                raise RuntimeError("This game's Compose file fixes its image tags; change them there.")
            known = lambda: tag in self.registry['tags'] or tag in ('latest', 'dev') or tag == project.tag_from_env()
            if not known():
                # The list may predate the release, or this agent may not have read it yet.
                self.read_registry(components(project))
            if not known():
                raise RuntimeError(f'The registry has no release tagged {tag}.')
            write_tag(project.env_file, tag)
        self.step(PULL_STEP)
        try:
            compose(project, 'pull', '--quiet', *services.values())
            if tag is not None and tag != previous:
                moves_forward(found.get('server'), tag)
        except Exception:
            if tag is not None:
                if original is None:
                    project.env_file.unlink(missing_ok=True)
                else:
                    write_env(project.env_file, original)
            raise

    def wait_ready(self, project, name, service):
        """Waits until a replaced component serves again: running, healthy where the image says, and answering /ready."""
        deadline = time.monotonic() + READY_SECONDS
        while True:
            status = components(project).get(name, {})
            state = status.get('state', 'missing')
            health = status.get('health')
            ready = state == 'running' and health in (None, 'healthy')
            if health not in (None, 'healthy'):
                state = f'{state}, {health}'
            if ready and name == 'server':
                ready = http_ready(f'{SERVER}/ready')
            elif ready and name == 'socketserver':
                ready = http_ready(f'http://{service}:{SOCKET_HTTP_PORT}/ready')
            if ready:
                return
            if time.monotonic() >= deadline:
                raise RuntimeError(f'The {name} did not come back within {READY_SECONDS} seconds ({state}); nothing after it was replaced.')
            time.sleep(5)

    def step(self, name):
        with self.lock:
            self.job['step'] = name
            self.save()

    def finish(self, state, message):
        with self.lock:
            self.job.update({'state': state, 'step': None, 'message': message, 'finishedAt': iso(now())})
            self.save()
        # A finished update changes the images: read the registry again on the next poll.
        self.registry['checked'] = None
        log(f"update {self.job['id']}: {state}{': ' + message if message else ''}")

    def save(self):
        # A full or read-only state volume must not stop an update half way, or leave a job marked
        # running with no worker: the state is only for after a restart, and the portal shows the job.
        try:
            STATE.mkdir(parents=True, exist_ok=True)
            write_json(STATE / 'job.json', self.job)
            write_json(STATE / 'done.json', self.done)
        except OSError as error:
            log(f'state not saved: {error}')

    def resume(self):
        """A job the agent was running when it stopped cannot be trusted half done: report it failed."""
        with self.lock:
            if self.job and self.job.get('state') == 'running':
                self.job.update({'state': 'failed', 'step': None, 'finishedAt': iso(now()),
                                 'message': 'The update agent restarted during the update; check each component and run it again.'})
                self.save()


def moves_forward(server, tag):
    """Refuses a move whose pulled server is not a newer release than the running one: an older server
    refuses a world a newer one wrote. Catches what the server cannot see, such as latest naming an older
    release than a running prerelease."""
    if tag == 'dev':
        return
    running = (server or {}).get('version') or ''
    if not RELEASE.fullmatch(running):
        raise RuntimeError(f'The running server is {running or "unlabelled"}, not a release, so a move to {tag} '
                           'cannot be shown to go forward; nothing was replaced.')
    images = present('image', [f"{server['image']}:{tag}"])
    labels = ((images[0].get('Config') or {}).get('Labels') or {}) if images else {}
    pulled = labels.get('org.opencontainers.image.version') or ''
    if not RELEASE.fullmatch(pulled) or release_key(pulled) <= release_key(running):
        raise RuntimeError(f'{tag} is {pulled or "an unknown version"}, not newer than the running {running}; nothing was replaced.')


def compose(project, *args):
    result = project.compose(*args)
    if result.returncode != 0:
        raise RuntimeError(f"docker compose {' '.join(args)}: " + last_line(result.stderr))


def http_ready(url):
    """Whether url answers 200. The game server's /ready also waits for the bus."""
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            return response.status == 200
    except (urllib.error.URLError, OSError, ValueError):
        return False


def parse_time(value):
    if not isinstance(value, str):
        return None
    try:
        parsed = datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else None


def wait_until(target):
    while (remaining := (target - now()).total_seconds()) > 0:
        time.sleep(min(remaining, 5))


def last_line(text):
    lines = [line for line in (text or '').splitlines() if line.strip()]
    return lines[-1][:300] if lines else 'failed'


def load_json(path):
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return None


def write_json(path, value):
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(value))
    os.replace(temporary, path)


def log(message):
    print(f'{iso(now())} {message}', flush=True)


def poll(body):
    request = urllib.request.Request(f'{SERVER}/api/update-agent/poll', data=json.dumps(body).encode(), method='POST',
                                     headers={'Content-Type': 'application/json', 'X-Update-Agent-Token': TOKEN})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response) if response.status == 200 else None


def main():
    if len(TOKEN) < 32:
        # Idle rather than exit, so `restart: unless-stopped` does not loop on a game set up before the
        # token existed. Running the installer's configure step again writes one.
        log('SHARPMUSH_UPDATE_AGENT_TOKEN is not set (at least 32 characters); portal updates are off.')
        while True:
            time.sleep(3600)
    agent = Agent()
    agent.resume()
    log(f'polling {SERVER} every {POLL_SECONDS}s')
    while True:
        try:
            project = own_project()
            agent.error = None
        except (RuntimeError, OSError, ValueError, IndexError, subprocess.TimeoutExpired) as error:
            project = None
            agent.error = str(error)[:500]
        try:
            job = poll(agent.report(project))
            if job and project and agent.accept(job):
                # Tell the server at once that the update was taken, rather than a poll later.
                continue
        except urllib.error.HTTPError as error:
            log(f'server answered {error.code}')
        except (urllib.error.URLError, OSError, ValueError, RuntimeError, subprocess.TimeoutExpired) as error:
            # The server is restarting, often because this agent is replacing it.
            log(f'poll: {error}')
        time.sleep(POLL_SECONDS)


if __name__ == '__main__':
    main()
