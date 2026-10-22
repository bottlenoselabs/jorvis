export function isSupported() {
    return !!(window.PublicKeyCredential
        && PublicKeyCredential.parseCreationOptionsFromJSON
        && PublicKeyCredential.parseRequestOptionsFromJSON);
}

async function run(ask, optionsJson) {
    try {
        const credential = await ask(JSON.parse(optionsJson));
        return JSON.stringify({ credential: credential.toJSON() });
    } catch (error) {
        return JSON.stringify({ error: (error && error.name) || 'Error' });
    }
}

export function create(optionsJson) {
    return run((options) => navigator.credentials.create({
        publicKey: PublicKeyCredential.parseCreationOptionsFromJSON(options)
    }), optionsJson);
}

export function get(optionsJson) {
    return run((options) => navigator.credentials.get({
        publicKey: PublicKeyCredential.parseRequestOptionsFromJSON(options)
    }), optionsJson);
}

export function clearFragment() {
    history.replaceState(null, '', location.pathname);
}
