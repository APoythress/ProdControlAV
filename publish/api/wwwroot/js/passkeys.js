// Simple base64url helpers
const toBase64Url = (buffer) => {
    const bytes = new Uint8Array(buffer);
    let str = '';
    for (let i = 0; i < bytes.byteLength; i++) str += String.fromCharCode(bytes[i]);
    return btoa(str).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
};

const fromBase64Url = (b64url) => {
    const pad = '==='.slice((b64url.length + 3) % 4);
    const b64 = b64url.replace(/-/g, '+').replace(/_/g, '/') + pad;
    const str = atob(b64);
    const bytes = new Uint8Array(str.length);
    for (let i = 0; i < str.length; i++) bytes[i] = str.charCodeAt(i);
    return bytes.buffer;
};

const mapCredDescriptorsToBuffers = (list) => {
    if (!Array.isArray(list)) return [];
    return list.map((d) => ({
        ...d,
        id: fromBase64Url(typeof d.id === 'string' ? d.id : '')
    }));
};

export async function createCredential(options) {
    // Transform incoming options (challenge/user.id/excludeCredentials[].id) to ArrayBuffers
    const publicKey = {
        ...options,
        challenge: fromBase64Url(options.challenge),
        user: {
            ...options.user,
            id: fromBase64Url(options.user.id)
        },
        excludeCredentials: mapCredDescriptorsToBuffers(options.excludeCredentials)
    };

    if (publicKey.authenticatorSelection?.residentKey === undefined && publicKey.authenticatorSelection) {
        // no-op; keep broad compatibility
    }

    const cred = await navigator.credentials.create({ publicKey });
    const attestationResponse = cred.response;

    return {
        id: cred.id,
        rawId: toBase64Url(cred.rawId),
        type: cred.type,
        response: {
            clientDataJSON: toBase64Url(attestationResponse.clientDataJSON),
            attestationObject: toBase64Url(attestationResponse.attestationObject),
            transports: (attestationResponse.getTransports && attestationResponse.getTransports()) || []
        },
        clientExtensionResults: (cred.getClientExtensionResults && cred.getClientExtensionResults()) || {}
    };
}

export async function getAssertion(options) {
    // Transform incoming options (challenge/allowCredentials[].id) to ArrayBuffers
    const publicKey = {
        ...options,
        challenge: fromBase64Url(options.challenge),
        allowCredentials: mapCredDescriptorsToBuffers(options.allowCredentials)
    };

    const cred = await navigator.credentials.get({ publicKey });
    const assertionResponse = cred.response;

    return {
        id: cred.id,
        rawId: toBase64Url(cred.rawId),
        type: cred.type,
        response: {
            clientDataJSON: toBase64Url(assertionResponse.clientDataJSON),
            authenticatorData: toBase64Url(assertionResponse.authenticatorData),
            signature: toBase64Url(assertionResponse.signature),
            userHandle: assertionResponse.userHandle ? toBase64Url(assertionResponse.userHandle) : null
        },
        clientExtensionResults: (cred.getClientExtensionResults && cred.getClientExtensionResults()) || {}
    };
}
