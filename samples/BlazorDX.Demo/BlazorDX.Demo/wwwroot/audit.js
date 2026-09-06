// Progressive enhancement for /audit. The checklist itself is server-rendered and fully
// answerable without this file; everything here is additive — progress, saving, and the report.
//
// Loaded globally from App.razor and a no-op on every other route. It re-initialises on Blazor's
// `enhancedload` because enhanced navigation swaps the DOM without re-running page scripts, so a
// tester who reaches /audit from a link would otherwise get the static page with none of the
// tooling and no indication anything was missing.
(function () {
    "use strict";

    var KEY = "blazordx-sr-audit-v1";

    function readState() {
        try {
            return JSON.parse(localStorage.getItem(KEY) || "{}");
        } catch (e) {
            return {};
        }
    }

    function init() {
        var root = document.querySelector("[data-audit-root]");
        if (!root || root.dataset.auditReady === "1") {
            return;
        }

        root.dataset.auditReady = "1";

        var fieldsets = Array.prototype.slice.call(root.querySelectorAll(".dx-audit-checks fieldset"));
        var total = fieldsets.length;
        if (total === 0) {
            return;
        }

        // Only revealed once the tooling is running, so a no-JS reader is never shown a progress
        // panel stuck at zero or a report button that does nothing.
        var progress = root.querySelector("[data-audit-progress]");
        var reportCard = root.querySelector("[data-audit-report-card]");
        if (progress) { progress.hidden = false; }
        if (reportCard) { reportCard.hidden = false; }

        var state = readState();
        var saveStatus = root.querySelector("[data-audit-save-status]");
        var reportStatus = root.querySelector("[data-audit-report-status]");
        var output = root.querySelector("[data-audit-output]");

        function save(message) {
            var ok = true;
            try {
                localStorage.setItem(KEY, JSON.stringify(state));
            } catch (e) {
                ok = false;
            }

            if (saveStatus) {
                saveStatus.textContent = ok
                    ? (message || "Saved in this browser.")
                    : "Could not save — this browser is blocking site storage, so copy your report before leaving.";
            }
        }

        // Restore anything from a previous sitting.
        Object.keys(state.answers || {}).forEach(function (name) {
            var el = document.getElementById(name + "-" + state.answers[name]);
            if (el) { el.checked = true; }
        });
        Object.keys(state.notes || {}).forEach(function (name) {
            var el = root.querySelector('[data-audit-note="' + name + '"]');
            if (el) { el.value = state.notes[name]; }
        });
        Object.keys(state.meta || {}).forEach(function (key) {
            var el = root.querySelector('[data-audit-meta="' + key + '"]');
            if (el) { el.value = state.meta[key]; }
        });

        function tally() {
            var counts = { pass: 0, issue: 0, na: 0, todo: 0 };
            fieldsets.forEach(function (fs) {
                var picked = fs.querySelector("input[type=radio]:checked");
                counts[picked ? picked.value : "todo"]++;
            });

            var answered = counts.pass + counts.issue + counts.na;
            var set = function (sel, value) {
                var el = root.querySelector(sel);
                if (el) { el.textContent = String(value); }
            };
            set("[data-audit-done]", answered);
            set("[data-audit-t-pass]", counts.pass);
            set("[data-audit-t-issue]", counts.issue);
            set("[data-audit-t-na]", counts.na);
            return counts;
        }

        root.addEventListener("change", function (e) {
            var t = e.target;
            if (t.type === "radio" && t.name.charAt(0) === "c") {
                state.answers = state.answers || {};
                state.answers[t.name] = t.value;
                var counts = tally();
                save("Saved. " + (counts.pass + counts.issue + counts.na) + " of " + total + " answered.");
            }
        });

        root.addEventListener("input", function (e) {
            var t = e.target;
            if (!t.dataset) {
                return;
            }

            if (t.dataset.auditNote) {
                state.notes = state.notes || {};
                state.notes[t.dataset.auditNote] = t.value;
                save();
            } else if (t.dataset.auditMeta) {
                state.meta = state.meta || {};
                state.meta[t.dataset.auditMeta] = t.value;
                save();
            }
        });

        function buildReport() {
            var meta = state.meta || {};
            var counts = tally();
            var lines = [
                "# BlazorDX manual screen-reader pass",
                "",
                "- Tester: " + (meta.who || "(not given)"),
                "- Screen reader: " + (meta.at || "(not given)"),
                "- Browser: " + (meta.browser || "(not given)"),
                "- OS: " + (meta.os || "(not given)"),
                "- Date: " + new Date().toISOString().slice(0, 10),
                "- Result: " + counts.pass + " pass, " + counts.issue + " issue, " +
                    counts.na + " n/a, " + counts.todo + " not run (of " + total + ")",
                ""
            ];

            root.querySelectorAll(".dx-audit-area").forEach(function (area) {
                var rows = [];
                area.querySelectorAll("fieldset").forEach(function (fs) {
                    var picked = fs.querySelector("input[type=radio]:checked");
                    var value = picked ? picked.value : "todo";
                    if (value === "todo") {
                        return;
                    }

                    var text = fs.querySelector("legend").textContent.trim().replace(/\s+/g, " ");
                    var noteInput = fs.querySelector('input[type="text"]');
                    var note = noteInput ? noteInput.value : "";
                    var mark = value === "pass" ? "PASS " : value === "issue" ? "ISSUE" : "N/A  ";
                    rows.push("- `" + mark + "` " + text + (note ? "\n      - " + note : ""));
                });

                if (rows.length) {
                    lines.push("## " + area.querySelector("h2").textContent.trim());
                    lines.push("");
                    lines.push(rows.join("\n"));
                    lines.push("");
                }
            });

            if (counts.pass + counts.issue + counts.na === 0) {
                lines.push("_No checks answered yet._");
            }

            return lines.join("\n");
        }

        var generate = root.querySelector("[data-audit-generate]");
        if (generate) {
            generate.addEventListener("click", function () {
                output.value = buildReport();
                if (reportStatus) { reportStatus.textContent = "Report generated below."; }
                output.focus();
            });
        }

        var copy = root.querySelector("[data-audit-copy]");
        if (copy) {
            copy.addEventListener("click", function () {
                if (!output.value) {
                    output.value = buildReport();
                }

                output.select();
                // The clipboard API needs a secure context and permission; selecting the text and
                // saying so is the fallback that always works, including for a keyboard user.
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(output.value).then(function () {
                        if (reportStatus) { reportStatus.textContent = "Report copied to the clipboard."; }
                    }, function () {
                        if (reportStatus) {
                            reportStatus.textContent =
                                "Could not copy automatically — the report is selected, so copy it with your keyboard.";
                        }
                    });
                } else if (reportStatus) {
                    reportStatus.textContent =
                        "The report is selected — copy it with your keyboard.";
                }
            });
        }

        var reset = root.querySelector("[data-audit-reset]");
        if (reset) {
            reset.addEventListener("click", function () {
                state = {};
                try {
                    localStorage.removeItem(KEY);
                } catch (e) {
                    // Nothing stored to clear.
                }

                root.querySelectorAll('input[type=radio][value="todo"]').forEach(function (r) {
                    r.checked = true;
                });
                root.querySelectorAll('input[type="text"]').forEach(function (i) {
                    i.value = "";
                });
                output.value = "";
                tally();
                if (saveStatus) { saveStatus.textContent = "Answers cleared."; }
                if (reportStatus) { reportStatus.textContent = ""; }
            });
        }

        tally();
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }

    window.addEventListener("load", function () {
        if (window.Blazor && typeof window.Blazor.addEventListener === "function") {
            try {
                window.Blazor.addEventListener("enhancedload", init);
            } catch (e) {
                // Enhanced navigation unavailable; a full page load still initialises above.
            }
        }
    });
})();
