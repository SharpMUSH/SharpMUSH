import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import vm from 'node:vm';

const source = readFileSync(new URL('../../SharpMUSH.Client/wwwroot/js/mush-monaco.js', import.meta.url), 'utf8');
// RegistryContractAuditTests separately verifies these declarations reach generated portal data.
// This test exercises the real browser consumer without needing a .NET build in the Node job.
const census = JSON.parse(readFileSync(new URL('../../docs/audits/softcode-contracts.json', import.meta.url), 'utf8'));
const censusDefinitions = Object.fromEntries(census.entries
    .filter(entry => entry.kind === 'function' && ['sortkey', 'index', 'munge', 'setunion', 'setdiff', 'setinter', 'setsymdiff', 'unique', 'json_query', 'json_fill'].includes(entry.name))
    .map(entry => [entry.name, { minArgs: entry.metadata.MinArgs, maxArgs: entry.metadata.MaxArgs, parameterNames: entry.metadata.ParameterNames }]));
const definitions = { functions: {
    ...censusDefinitions,
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

for (const [line, expected] of [
    ['json_query({}', ['json']],
    ['json_query({},size', ['json', '[action]']],
    ['json_query({},get,a,b', ['json', '[action]', '[arg1]', '[arg2]']],
    ['json_query({},extract,$.a', ['json', '[action]', '[arg1]']],
    ['json_fill({},/a,one', ['json', 'pointer1', 'value1']],
    ['json_fill({},/a,one,/b,two', ['json', 'pointer1', 'value1', '[pointer2]', '[value2]']]
]) {
    test(`JSON action and paired-fill signature for ${line}`, async () => {
        const provider = await signatureProvider();
        const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
        const signature = result.value.signatures[0];
        const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
        assert.deepEqual(labels.slice(0, expected.length), expected);
        assert.equal(result.value.activeParameter, expected.length - 1);
    });
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

for (const [line, expected] of [
    ['sortkey(me/key,ab|a|abc,n,|,;', ['attribute', 'list', '[sort-type]', '[delimiter]', '[osep]']],
    ['index(a|b|c,|,2,2', ['list', 'delimiter', 'first', 'length']],
    ['munge(me/order,b|a|c,2|1|3,|,;', ['attribute', 'list1', 'list2', '[delimiter]', '[osep]']]
]) {
    test(`portal signature describes actual list call shape for ${line}`, async () => {
        const provider = await signatureProvider();
        const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
        const signature = result.value.signatures[0];
        const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
        assert.deepEqual(labels, expected);
        assert.equal(result.value.activeParameter, expected.length - 1);
    });
}

for (const name of ['setunion', 'setdiff', 'setinter', 'setsymdiff']) {
    test(`${name} portal signature labels both optional sort and output separator`, async () => {
        const provider = await signatureProvider();
        const line = `${name}(9|2,2|10,|,n,;`;
        const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
        const signature = result.value.signatures[0];
        const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
        assert.deepEqual(labels, ['list1', 'list2', '[delimiter]', '[sort-type]', '[osep]']);
        assert.equal(result.value.activeParameter, 4);
    });
}

test('unique portal signature puts comparison type before input and output separators', async () => {
    const provider = await signatureProvider();
    const line = 'unique(01|1|2,n,|,;';
    const result = provider.provideSignatureHelp({ getLineContent: () => line }, { lineNumber: 1, column: line.length + 1 });
    const signature = result.value.signatures[0];
    const labels = Array.from(signature.parameters, parameter => signature.label.slice(...parameter.label));
    assert.deepEqual(labels, ['list', '[sort-type]', '[delimiter]', '[osep]']);
    assert.equal(result.value.activeParameter, 3);
});
