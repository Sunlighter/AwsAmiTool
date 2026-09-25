var POLL_URL = "events";
var stopped = false;
var inFlight = false;

function setStatus(text) {
    var el = document.getElementById("status");
    if (el) el.textContent = text;
}

function markComplete(elementId) {
    var el = document.getElementById(elementId);
    if (el) el.style.color = "#FFC0C0";
}

function markFaulted(elementId) {
    var el = document.getElementById(elementId);
    if (el) el.style.color = "#008000";
}

function applyDirectives(directives) {
    if (!Array.isArray(directives)) {
        throw new Error("Response is not a JSON array");
    }
    var currentTimeEl = document.getElementById("currentTime");
    currentTimeEl.value = parseInt(currentTimeEl.value, 10) + directives.length;
    for (var i = 0; i < directives.length; i++) {
        var d = directives[i];
        if (!d || typeof d !== "object") continue;
        if (d.eof) {
            stopped = true;
            setStatus("Received EOF. Polling stopped.");
        } else if (d.complete) {
            markComplete(d.complete);
        } else if (d.fault) {
            markFaulted(d.fault);
        }
    }
}

function poll() {
    if (stopped || inFlight) return;
    inFlight = true;
    setStatus("Waiting on " + POLL_URL + "…");

    var xhr = new XMLHttpRequest();
    var totalUrl = POLL_URL + "?after=" + encodeURIComponent(document.getElementById("currentTime").value);
    xhr.open("GET", totalUrl, true);
    xhr.setRequestHeader("Accept", "application/json");

    xhr.onreadystatechange = function () {
        if (xhr.readyState !== 4) return;
        inFlight = false;

        if (xhr.status >= 200 && xhr.status < 300) {
            try {
                var body = xhr.responseText ? JSON.parse(xhr.responseText) : [];
                applyDirectives(body);
            } catch (err) {
                setStatus("Bad JSON: " + err.message + " — retrying");
            }
        } else {
            setStatus("HTTP " + xhr.status + " — retrying");
        }

        if (!stopped) setTimeout(poll, 50);
    };

    xhr.onerror = function () {
        inFlight = false;
        setStatus("Network error — retrying");
        if (!stopped) setTimeout(poll, 1000);
    };

    xhr.send();
}

document.addEventListener("DOMContentLoaded", function () {
    poll();
});