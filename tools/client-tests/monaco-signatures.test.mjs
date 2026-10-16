import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/js/mush-monaco.js', import.meta.url), 'utf8');
// RegistryContractAuditTests separately verifies these declarations reach generated portal data.
// This test exercises the real browser consumer without needing a .NET build in the Node job.
const definitions = { functions: {
    json_map: { minArgs: 2, maxArgs: 33, parameterNames: ['attribute', 'json', 'osep', 'arg...'] },
    switch: { minArgs: 3, maxArgs: 32, parameterNames: ['string', 'case...|result...'] },
    json_mod: { minArgs: 3, maxArgs: 4, parameterNames: ['json', 'action', 'path-or-patch', 'json2'] }
} };

async function signatureProvider() {
    let provider;
    const noop = () => {};
    const context = vm.createContext({
        window: {}, console,
        fetch: async () => ({ json: async () => definitions }),
        monaco: {
            languages: {
                register: noop, setMonarchTokensProvider: noop, setLanguageConfiguration: noop,
                registerCompletionItemProvider: noop, registerHoverProvider: noop,
                registerSignatureHelpProvider: (_, value) => { provider = value; }
            },
            editor: { defineTheme: noop, registerCommand: noop, setTheme: noop, getEditors: () => [] }
        }
    });
    vm.runInContext(source, context);
    context.window.SharpMUSH.Monaco.initEditor('test');
    await new Promise(resolve => setImmediate(resolve));
    assert.ok(provider, 'the actual Monaco signature provider registers');
    return provider;
}

for (const extraCount of [1, 2, 4]) {
    test(`json_map portal signature numbers ${extraCount} callback extras from arg1`, async () => {
        const provider = await signatureProvider();
        const line = 'json_map(me/callback,json(array,1),|,' + Array(extraCount).fill('extra').join(',');
        const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
        const signature = result.value.signatures[0];
        const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
        assert.deepEqual(labels.slice(0, 3), ['attribute', 'json', '[osep]']);
        assert.deepEqual(labels.slice(3, 3 + extraCount), Array.from({ length: extraCount }, (_, index) => `[arg${index + 1}]`));
        assert.equal(result.value.activeParameter, extraCount + 2);
        assert.equal(labels[result.value.activeParameter], `[arg${extraCount}]`);
    });
}

for (const [line, expected] of [
    ['switch(value,a,one,b,two', ['string', 'case1', 'result1', '[case2]', '[result2]']],
    ['json_mod({},set,$.a,2', ['json', 'action', 'path-or-patch', '[json2]']]
]) {
    test(`portal signature preserves declared labels for ${line}`, async () => {
        const provider = await signatureProvider();
        const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
        const signature = result.value.signatures[0];
        const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
        assert.deepEqual(labels.slice(0, expected.length), expected);
        assert.equal(result.value.activeParameter, expected.length - 1);
    });
}
