// dx-powerbi.js — the hand-written ESM wrapper around the Power BI client SDK
// (powerbi-client). It is intentionally tiny: it does NOT bundle the ~MB SDK.
// Instead, if the global powerbi service is not already present it lazy-loads the
// real SDK from a CDN by injecting a <script> and awaiting it, then calls
// powerbi.embed(...) with the config the .NET component handed across.
//
// SECURITY: the config carries only the embed token + embedUrl + reportId — all
// meant for the browser (that is how Power BI "app owns data" embedding works).
// The Azure AD token never leaves the server; it is used there to MINT this embed
// token and is never serialized into the config that reaches this module.
//
// The demo/tests substitute a tiny stub `window.powerbi` (powerbi-stub.js) so the
// full embed loop is provable without a live Azure tenant — when that stub is
// present this module never touches the CDN.

const SDK_URL = "https://cdn.jsdelivr.net/npm/powerbi-client@2/dist/powerbi.min.js";

// One in-flight CDN load shared across every embed call, so concurrent components
// do not each inject a script tag.
let sdkLoad = null;

function loadSdk() {
    if (window.powerbi && window["powerbi-client"]) {
        return Promise.resolve();
    }
    if (window.powerbi) {
        // A stub (or a prior load) already provided the service object.
        return Promise.resolve();
    }
    if (sdkLoad) {
        return sdkLoad;
    }

    sdkLoad = new Promise((resolve, reject) => {
        const existing = document.querySelector('script[data-dx-powerbi-sdk]');
        if (existing) {
            existing.addEventListener("load", () => resolve());
            existing.addEventListener("error", () => reject(new Error("Power BI SDK failed to load.")));
            return;
        }
        const script = document.createElement("script");
        script.src = SDK_URL;
        script.async = true;
        script.setAttribute("data-dx-powerbi-sdk", "true");
        script.addEventListener("load", () => resolve());
        script.addEventListener("error", () => reject(new Error("Power BI SDK failed to load from the CDN.")));
        document.head.appendChild(script);
    });
    return sdkLoad;
}

// The Embed token-type enum value. The SDK's models.TokenType.Embed is 1; when the
// real SDK is present we read it from there, otherwise we fall back to the constant
// so the stub path works without the models namespace.
function embedTokenType() {
    const models = window["powerbi-client"] && window["powerbi-client"].models;
    if (models && models.TokenType && typeof models.TokenType.Embed === "number") {
        return models.TokenType.Embed;
    }
    return 1; // models.TokenType.Embed
}

// Resolve the target element from the id the .NET side passed.
function resolveElement(element) {
    if (typeof element === "string") {
        return document.getElementById(element);
    }
    return element;
}

/**
 * Embeds the report described by configJson into the element with the given id.
 * Returns once embed() has been called; load/render/error events are wired when
 * the SDK exposes them so the host container can react (we surface them to the DOM
 * via data-* attributes for the demo/E2E to observe — no console noise).
 */
export async function embed(elementId, configJson) {
    const element = resolveElement(elementId);
    if (!element) {
        throw new Error("dx-powerbi: target element '" + elementId + "' was not found.");
    }

    let config;
    try {
        config = JSON.parse(configJson);
    } catch {
        throw new Error("dx-powerbi: the embed config was not valid JSON.");
    }

    await loadSdk();

    const service = window.powerbi;
    if (!service || typeof service.embed !== "function") {
        throw new Error("dx-powerbi: the Power BI service (window.powerbi) is unavailable.");
    }

    // Reset any previous embed in this element so re-embedding is clean.
    try {
        service.reset(element);
    } catch {
        // reset is best-effort; ignore if the service has no prior embed here.
    }

    const embedConfig = {
        type: "report",
        embedUrl: config.embedUrl,
        accessToken: config.embedToken,
        tokenType: embedTokenType(),
        id: config.reportId,
        settings: {
            // Microsoft's report provides its own filter pane + page navigation;
            // these are the conventional defaults. The embedded report supplies its
            // own keyboard navigation and "Show as a table" accessibility surface.
            panes: {
                filters: { visible: true },
                pageNavigation: { visible: true },
            },
        },
    };

    const report = service.embed(element, embedConfig);

    // Wire lifecycle events when the embed object supports them. We mark the host
    // element with data-* state rather than logging, so the demo/E2E can observe
    // progress without any console output.
    if (report && typeof report.on === "function") {
        element.setAttribute("data-dx-powerbi-state", "embedding");
        report.on("loaded", () => element.setAttribute("data-dx-powerbi-state", "loaded"));
        report.on("rendered", () => element.setAttribute("data-dx-powerbi-state", "rendered"));
        report.on("error", () => element.setAttribute("data-dx-powerbi-state", "error"));
    } else {
        element.setAttribute("data-dx-powerbi-state", "embedded");
    }

    return true;
}

/**
 * Tears down the embedded report in the given element, so a disposed component
 * leaves no dangling iframe or event handlers behind.
 */
export function unmount(elementId) {
    const element = resolveElement(elementId);
    if (!element) {
        return;
    }
    const service = window.powerbi;
    if (service && typeof service.reset === "function") {
        try {
            service.reset(element);
        } catch {
            // best-effort teardown
        }
    }
    element.removeAttribute("data-dx-powerbi-state");
}
