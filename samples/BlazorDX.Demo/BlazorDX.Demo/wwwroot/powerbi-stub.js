// powerbi-stub.js — a DEMO/TEST-ONLY stand-in for the Power BI client service
// (window.powerbi). It is NOT the real SDK.
//
// In production, dx-powerbi.js lazy-loads the real powerbi-client SDK from a CDN and
// calls the genuine window.powerbi.embed(...), which contacts app.powerbi.com. That
// path needs a live Azure tenant and a valid embed token, so it cannot run in the
// demo or E2E. This stub provides a window.powerbi BEFORE dx-powerbi.js loads, so
// the wrapper uses it instead of fetching the CDN — meaning:
//   * NO network call to app.powerbi.com (no console errors), and
//   * the embed config the wrapper was given is recorded on window.__lastEmbed,
//     so an E2E can prove our wrapper called embed() with the mock's embedUrl +
//     embed token (server got embed token from mock -> passed to client ->
//     wrapper called embed).
// It renders a visible placeholder into the target element so the page looks alive.
(function () {
    window.__lastEmbed = null;

    window.powerbi = {
        embed: function (element, config) {
            // Record exactly what the wrapper passed across.
            window.__lastEmbed = {
                embedUrl: config && config.embedUrl,
                accessToken: config && config.accessToken,
                tokenType: config && config.tokenType,
                id: config && config.id,
            };

            // Render a simple placeholder (no iframe, no network) so the demo shows
            // something and the container is visibly populated.
            if (element) {
                element.innerHTML =
                    '<div class="dx-powerbi-stub" style="padding:24px;text-align:center;' +
                    'color:#475569;background:#f1f5f9;height:100%;box-sizing:border-box;' +
                    'display:flex;align-items:center;justify-content:center;flex-direction:column;gap:8px">' +
                    '<strong>Power BI embed (demo stub)</strong>' +
                    '<span style="font-size:0.85rem">Stub captured the embed config — see window.__lastEmbed.</span>' +
                    '<span style="font-size:0.75rem;word-break:break-all">' +
                    (config && config.embedUrl ? escapeHtml(config.embedUrl) : '') +
                    '</span></div>';
            }

            // Mimic the real embed object's event API so dx-powerbi.js can wire
            // loaded/rendered without error. Fire them on the next tick.
            var handlers = {};
            var report = {
                on: function (name, cb) { handlers[name] = cb; return report; },
                off: function (name) { delete handlers[name]; return report; },
            };
            setTimeout(function () {
                if (handlers.loaded) { handlers.loaded(); }
                if (handlers.rendered) { handlers.rendered(); }
            }, 0);
            return report;
        },
        reset: function (element) {
            if (element) { element.innerHTML = ''; }
        },
    };

    // Provide the models namespace the wrapper looks for (TokenType.Embed === 1).
    window["powerbi-client"] = { models: { TokenType: { Aad: 0, Embed: 1 } } };

    function escapeHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
})();
