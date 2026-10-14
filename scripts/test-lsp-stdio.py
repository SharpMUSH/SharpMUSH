#!/usr/bin/env python3
"""Exercise the built standalone host. Build SharpMUSH.LanguageServer before running.

Checks diagnostics and built-in intelligence across all five extensions.
Command-only signature help is intentionally absent; function signatures in
command expressions are supported. Missing intelligence fails this probe.
"""
import json, os, pathlib, queue, subprocess, threading, tempfile
root = pathlib.Path(__file__).resolve().parents[1]
messages = queue.Queue()
with tempfile.TemporaryDirectory() as state:
    env = dict(os.environ, XDG_DATA_HOME=state)
    process = subprocess.Popen(['dotnet', str(root / 'SharpMUSH.LanguageServer/bin/Debug/net11.0/SharpMUSH.LanguageServer.dll')], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
    def read():
        while True:
            headers = {}
            while line := process.stdout.readline():
                if line == b'\r\n': break
                key, value = line.decode().split(':', 1)
                headers[key.lower()] = value.strip()
            if not headers: return
            messages.put(json.loads(process.stdout.read(int(headers['content-length']))))
    threading.Thread(target=read, daemon=True).start()
    def send(method, params=None, ident=None):
        obj = dict(jsonrpc='2.0', method=method)
        if params is not None: obj['params'] = params
        if ident is not None: obj['id'] = ident
        body = json.dumps(obj).encode()
        process.stdin.write(f'Content-Length: {len(body)}\r\n\r\n'.encode()+body)
        process.stdin.flush()
    def receive(predicate):
        while True:
            msg = messages.get(timeout=15)
            if 'method' in msg and 'id' in msg:
                body = json.dumps(dict(jsonrpc='2.0', id=msg['id'], result=None)).encode()
                process.stdin.write(f'Content-Length: {len(body)}\r\n\r\n'.encode()+body)
                process.stdin.flush()
            if predicate(msg): return msg
    failures=[]
    def check(label, action, predicate):
        try:
            value=action()
            ok=bool(predicate(value))
        except queue.Empty:
            value='TIMEOUT'; ok=False
        print(json.dumps(dict(check=label, passed=ok, response=value)))
        if not ok: failures.append(label)
    try:
        send('initialize', dict(processId=None, rootUri=None, capabilities={'textDocument': {name: {'dynamicRegistration': False} for name in ('hover','completion','signatureHelp','synchronization')}}), 1)
        check('initialize', lambda:receive(lambda m:m.get('id')==1), lambda m:'capabilities' in m.get('result',{}))
        if failures: raise RuntimeError('Host did not initialize')
        send('initialized', {})
        ident=2
        for ext, valid in [('mush','think [add(1,2)]\nthink hello'),('mu','think [add(1,2)]\nthink hello'),('mushfn','add(1,2)'),('fun','add(1,2)'),('mushcmd','think hello;think [add(1,2)]')]:
            uri=f'file:///tmp/probe.{ext}'
            for kind, text in [('valid',valid),('invalid','add(1,2' if ext in ('fun','mushfn') else 'think [add(1,2)')]:
                if kind=='valid': send('textDocument/didOpen',dict(textDocument=dict(uri=uri,languageId='mush',version=1,text=text)))
                else: send('textDocument/didChange',dict(textDocument=dict(uri=uri,version=2),contentChanges=[dict(text=text)]))
                check(f'{ext}-{kind}',lambda:receive(lambda m:m.get('method')=='textDocument/publishDiagnostics' and m.get('params',{}).get('uri')==uri),lambda m: bool(m['params']['diagnostics']) == (kind=='invalid') and not any('Parser error:' in d['message'] for d in m['params']['diagnostics']))
        def request(method, uri, position, predicate, label):
            global ident
            send('textDocument/'+method, dict(textDocument=dict(uri=uri), position=dict(line=0,character=position)), ident)
            check(label, lambda:receive(lambda m:m.get('id')==ident), lambda m:'error' not in m and predicate(m.get('result')))
            ident += 1
        def items(result):
            return result.get('items', []) if isinstance(result, dict) else result or []
        for ext in ('mushfn','fun','mush','mu','mushcmd'):
            uri=f'file:///tmp/intelligence.{ext}'
            code='GeT(#1/DESC)' if ext in ('mushfn','fun') else '@emit [GeT(#1/DESC)]'
            offset=code.index('GeT')
            send('textDocument/didOpen', dict(textDocument=dict(uri=uri,languageId='mush',version=1,text=code)))
            receive(lambda m:m.get('method')=='textDocument/publishDiagnostics' and m.get('params',{}).get('uri')==uri)
            request('hover', uri, offset+1, lambda r:r and 'Function: `GeT`' in r['contents']['value'] and 'Maximum: 1' in r['contents']['value'], f'{ext}-function-hover')
            request('completion', uri, offset+2, lambda r:any(i['label'].lower()=='get' and i['insertText'].lower()=='get($0)' for i in items(r)), f'{ext}-function-completion')
            request('signatureHelp', uri, offset+6, lambda r:r and r['signatures'][0]['label']=='GeT(object/attribute)' and r['activeParameter']==0, f'{ext}-function-signature')
            if ext not in ('mushfn','fun'):
                request('hover', uri, 2, lambda r:r and 'Command: `@emit`' in r['contents']['value'] and 'NOEVAL' in r['contents']['value'], f'{ext}-command-hover')
                request('completion', uri, 3, lambda r:any(i['label'].upper()=='@EMIT' and 'NOEVAL' in str(i.get('documentation')) for i in items(r)), f'{ext}-command-completion')
                request('signatureHelp', uri, 5, lambda r:r is None, f'{ext}-command-signature-unsupported')
        for ext, code, name in [('fun','AvG(1,2)','avg'), ('mushcmd','@sw 1=1,hello','@sw')]:
            uri=f'file:///tmp/alias.{ext}'
            send('textDocument/didOpen', dict(textDocument=dict(uri=uri,languageId='mush',version=1,text=code)))
            receive(lambda m:m.get('method')=='textDocument/publishDiagnostics' and m.get('params',{}).get('uri')==uri)
            request('hover', uri, 1, lambda r:r and name.lower() in r['contents']['value'].lower(), f'{ext}-alias-hover')
            request('completion', uri, 3, lambda r:any(i['label'].lower()==name for i in items(r)), f'{ext}-alias-completion')
        uri='file:///tmp/variadic.fun'
        send('textDocument/didOpen', dict(textDocument=dict(uri=uri,languageId='mush',version=1,text='add(1,2)')))
        receive(lambda m:m.get('method')=='textDocument/publishDiagnostics' and m.get('params',{}).get('uri')==uri)
        request('signatureHelp', uri, 6, lambda r:r and r['signatures'][0]['label'].startswith('add(number1, number2') and len(r['signatures'][0]['parameters'])==2 and r['activeParameter']==1, 'variadic-signature')
        send('shutdown',ident=ident)
        check('shutdown',lambda:receive(lambda m:m.get('id')==ident),lambda m:'error' not in m)
        send('exit')
        process.wait(timeout=15)
        check('exit',lambda:process.returncode,lambda c:c==0)
    finally:
        if process.poll() is None: process.kill(); process.wait()
        print(json.dumps(dict(stderr=process.stderr.read().decode(),failures=failures, logs={str(p.relative_to(state)):p.read_text()[-14000:] for p in pathlib.Path(state).rglob('*.log')})))
    raise SystemExit(bool(failures))
