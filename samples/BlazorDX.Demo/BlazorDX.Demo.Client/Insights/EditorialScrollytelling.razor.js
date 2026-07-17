// Reveals each .dx-editorial-scrolly-stage as it scrolls into view. IntersectionObserver only —
// no scroll-position listener, so nothing runs per scroll pixel (no jank). Reveals a stage once,
// then stops observing it (this is a one-way "reveal as you read" narrative, not a toggle).
function wire() {
    const stages = document.querySelectorAll(".dx-editorial-scrolly-stage:not([data-scrolly-wired])");
    if (stages.length === 0) {
        return false;
    }

    if (!("IntersectionObserver" in window)) {
        stages.forEach((el) => el.classList.add("is-visible"));
        return true;
    }

    const io = new IntersectionObserver(
        (entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    entry.target.classList.add("is-visible");
                    io.unobserve(entry.target);
                }
            }
        },
        { threshold: 0.25, rootMargin: "0px 0px -10% 0px" }
    );

    stages.forEach((el) => {
        el.dataset.scrollyWired = "true";
        io.observe(el);
    });
    return true;
}

let sawStages = wire();

// Blazor's enhanced navigation can swap page content without a full reload; re-wire any
// newly-inserted stages after each such navigation.
document.addEventListener("enhancedload", wire);

// Blazor Web App's interactive-WASM render mode prerenders this page as static HTML, then
// replaces that subtree once the WASM runtime hydrates — which can happen *after* the module-load
// `wire()` call above already ran, leaving it watching detached prerendered nodes. A
// MutationObserver on the document catches whatever DOM actually ends up live, regardless of
// exactly when hydration lands; it disconnects itself shortly after it's found and wired a batch,
// since this page's scrollytelling sections don't get removed/re-added afterward.
const mo = new MutationObserver(() => {
    sawStages = wire() || sawStages;
});
mo.observe(document.body, { childList: true, subtree: true });

setTimeout(() => {
    if (sawStages) {
        mo.disconnect();
    }
}, 4000);
