/** Leave the page with a same-origin, antiforgery-protected POST; no bearer appears in a URL. */
export function challenge(transactionId, fieldName, csrfToken) {
    if (typeof transactionId !== 'string' || !/^[A-Za-z0-9_-]{43}$/.test(transactionId)
        || typeof fieldName !== 'string' || !fieldName || typeof csrfToken !== 'string' || !csrfToken) {
        throw new Error('Invalid sign-in challenge');
    }
    const form = document.createElement('form');
    form.method = 'post';
    form.action = '/api/auth/external/challenge';
    form.hidden = true;
    for (const [name, value] of [['transactionId', transactionId], [fieldName, csrfToken]]) {
        const input = document.createElement('input');
        input.type = 'hidden'; input.name = name; input.value = value;
        form.appendChild(input);
    }
    document.body.appendChild(form);
    form.submit();
}
