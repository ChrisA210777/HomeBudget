// Sharing an invitation link (ADR-015): the system share sheet where the browser has one, and the clipboard.

export function canShare() {
    return typeof navigator.share === 'function';
}

// Resolves true when shared, false when the person closed the share sheet or sharing failed.
export async function share(title, text, url) {
    try {
        await navigator.share({ title, text, url });
        return true;
    } catch {
        return false;
    }
}

export async function copy(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return false;
    }
}
