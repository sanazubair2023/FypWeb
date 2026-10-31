// ── Sidebar toggle (mobile) ─────────────────────────────────────────────────
(function () {
    var btn = document.getElementById('btnHamburger');
    var sidebar = document.getElementById('appSidebar');
    var overlay = document.getElementById('sidebarOverlay');
    function toggle() {
        if (!sidebar) return;
        sidebar.classList.toggle('open');
        if (overlay) overlay.classList.toggle('show');
    }
    if (btn) btn.addEventListener('click', toggle);
    if (overlay) overlay.addEventListener('click', toggle);
})();

// ── CSRF helper: reads the antiforgery token from any form on the page ───────
function getAntiForgeryToken() {
    var el = document.querySelector('input[name="__RequestVerificationToken"]');
    return el ? el.value : '';
}

// ── JSON POST helper (adds antiforgery header) ───────────────────────────────
async function postJson(url, body) {
    try {
        var res = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': getAntiForgeryToken()
            },
            body: JSON.stringify(body)
        });
        var data = await res.json().catch(function () { return {}; });
        if (!res.ok) return { ok: false, message: data.message || 'The request could not be completed (' + res.status + ').' };
        return data;
    } catch (e) {
        return { ok: false, message: 'Network error. Check your connection and try again.' };
    }
}

// ── Geolocation → returns {latitude, longitude} ──────────────────────────────
function getBrowserLocation() {
    return new Promise(function (resolve, reject) {
        if (!navigator.geolocation) { reject('Geolocation not supported by this browser.'); return; }
        navigator.geolocation.getCurrentPosition(
            function (pos) { resolve({ latitude: pos.coords.latitude, longitude: pos.coords.longitude }); },
            function (err) { reject(err.message || 'Unable to retrieve your location.'); },
            { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 }
        );
    });
}

// ── Reusable Leaflet pin-drop map ────────────────────────────────────────────
// options: { elementId, lat, lng, onPick(lat,lng), readOnly }
function initPinMap(options) {
    var startLat = (options.lat != null && !isNaN(options.lat)) ? options.lat : 33.6844;   // Islamabad/Rawalpindi default
    var startLng = (options.lng != null && !isNaN(options.lng)) ? options.lng : 73.0479;
    var hasInitial = (options.lat != null && !isNaN(options.lat) && options.lng != null && !isNaN(options.lng));

    var map = L.map(options.elementId).setView([startLat, startLng], hasInitial ? 15 : 12);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; OpenStreetMap contributors'
    }).addTo(map);

    var marker = null;
    function place(lat, lng) {
        if (marker) { marker.setLatLng([lat, lng]); }
        else { marker = L.marker([lat, lng], { draggable: !options.readOnly }).addTo(map); }
        if (!options.readOnly && marker) {
            marker.off('dragend').on('dragend', function (e) {
                var p = e.target.getLatLng();
                if (options.onPick) options.onPick(p.lat, p.lng);
            });
        }
        if (options.onPick) options.onPick(lat, lng);
    }
    if (hasInitial) place(startLat, startLng);

    if (!options.readOnly) {
        map.on('click', function (e) { place(e.latlng.lat, e.latlng.lng); map.panTo(e.latlng); });
    }

    // give Leaflet a tick to size correctly inside flex/tab layouts
    setTimeout(function () { map.invalidateSize(); }, 250);

    return {
        map: map,
        setPoint: function (lat, lng) { place(lat, lng); map.setView([lat, lng], 15); }
    };
}

// ── Star rating render (read-only) ───────────────────────────────────────────
function renderStars(value) {
    var full = Math.floor(value);
    var half = (value - full) >= 0.5;
    var html = '';
    for (var i = 1; i <= 5; i++) {
        if (i <= full) html += '<i class="bi bi-star-fill"></i>';
        else if (i === full + 1 && half) html += '<i class="bi bi-star-half"></i>';
        else html += '<i class="bi bi-star"></i>';
    }
    return html;
}


// Worker notification unread badge. Only numeric JSON is inserted into the DOM.
(function () {
    var badge = document.getElementById('workerNotificationBadge');
    if (!badge) return;
    async function refresh() {
        try {
            var response = await fetch(badge.getAttribute('data-notification-url'), { headers: { 'Accept': 'application/json' } });
            if (!response.ok) return;
            var data = await response.json();
            var count = Number.isFinite(data.count) ? Math.max(0, data.count) : 0;
            badge.textContent = count > 99 ? '99+' : String(count);
            badge.classList.toggle('d-none', count === 0);
            badge.setAttribute('aria-label', count + ' unread notifications');
        } catch (e) { /* retain the last known count during a transient failure */ }
    }
    refresh();
    setInterval(refresh, 60000);
})();


// Habit metadata can define mutually exclusive groups (for example, one
// smoking preference). The server validates the same rule; this is only UX.
(function () {
    document.querySelectorAll('.habit-choice[data-habit-group]').forEach(function (input) {
        input.addEventListener('change', function () {
            if (!this.checked) return;
            var group = this.getAttribute('data-habit-group');
            if (!group) return;
            var form = this.closest('form') || document;
            form.querySelectorAll('.habit-choice[data-habit-group]').forEach(function (other) {
                if (other !== input && other.getAttribute('data-habit-group') === group) other.checked = false;
            });
        });
    });
})();

// Progressive-enhancement Photon place search (the reviewed app uses the same
// public geocoder). Results are rendered as text, never as HTML. Map/pin and
// native geolocation remain usable when the service cannot be reached.
function initPhotonPlaceSearch(options) {
    var input = document.getElementById(options.inputId || 'placeSearch');
    var results = document.getElementById(options.resultsId || 'placeResults');
    if (!input || !results) return;
    var timer = null, activeRequest = null;
    function clearResults() { results.replaceChildren(); }
    function message(text, isError) {
        clearResults();
        var div = document.createElement('div');
        div.className = 'small ' + (isError ? 'text-danger' : 'text-muted') + ' mt-1';
        div.textContent = text; results.appendChild(div);
    }
    function choose(feature) {
        var coordinates = feature.geometry && feature.geometry.coordinates;
        if (!coordinates || coordinates.length < 2) return;
        var p = feature.properties || {};
        var label = [p.name, p.street, p.city, p.state, p.country].filter(function (part, index, parts) {
            return part && parts.indexOf(part) === index;
        }).join(', ');
        input.value = label || p.name || 'Selected place';
        clearResults();
        options.onPick(Number(coordinates[1]), Number(coordinates[0]), input.value);
    }
    input.addEventListener('input', function () {
        clearTimeout(timer);
        var query = input.value.trim();
        if (activeRequest) activeRequest.abort();
        if (query.length < 3) { clearResults(); return; }
        timer = setTimeout(async function () {
            activeRequest = new AbortController();
            message('Searching places…', false);
            try {
                var response = await fetch('https://photon.komoot.io/api/?q=' + encodeURIComponent(query) + '&limit=5', {
                    signal: activeRequest.signal, headers: { 'Accept': 'application/json' }
                });
                if (!response.ok) throw new Error('Geocoder unavailable');
                var data = await response.json();
                clearResults();
                if (!data.features || !data.features.length) { message('No matching places found.', false); return; }
                data.features.forEach(function (feature) {
                    var p = feature.properties || {};
                    var button = document.createElement('button');
                    button.type = 'button'; button.className = 'list-group-item list-group-item-action';
                    button.textContent = [p.name, p.street, p.city, p.state, p.country].filter(function (part, index, parts) {
                        return part && parts.indexOf(part) === index;
                    }).join(', ') || 'Unnamed place';
                    button.addEventListener('click', function () { choose(feature); });
                    results.appendChild(button);
                });
            } catch (e) {
                if (e.name !== 'AbortError') message('Place search is unavailable. You can still drop a pin on the map.', true);
            }
        }, 350);
    });
}
