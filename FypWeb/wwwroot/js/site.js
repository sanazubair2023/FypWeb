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
    var res = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': getAntiForgeryToken()
        },
        body: JSON.stringify(body)
    });
    return res.json();
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
