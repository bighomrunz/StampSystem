// Service worker แคชแค่ไฟล์หน้าตา (shell) เท่านั้น
// ห้ามแคชคำขอที่เกี่ยวกับเงินหรือแต้ม เพราะยอดต้องมาจากเซิร์ฟเวอร์เสมอ
const CACHE_NAME = "coffeestamp-shell-v7";
const SHELL_FILES = [
    "/css/brand.css",
    "/css/app.css",
    "/js/app.js",
    "/js/pos.js",
    "/js/login.js",
    "/js/orders.js",
    "/js/scan.js",
    "/js/stamp.js",
    "/js/customer-picker.js",
    "/js/customers.js",
    "/js/liff-card.js",
    "/css/liff.css",
    "/icons/favicon.svg",
    "/icons/icon-192.png",
    "/icons/icon-512.png"
];

self.addEventListener("install", function (event) {
    event.waitUntil(
        caches.open(CACHE_NAME).then(function (cache) {
            return cache.addAll(SHELL_FILES);
        }).then(function () {
            return self.skipWaiting();
        })
    );
});

self.addEventListener("activate", function (event) {
    event.waitUntil(
        caches.keys().then(function (names) {
            return Promise.all(names
                .filter(function (name) { return name !== CACHE_NAME; })
                .map(function (name) { return caches.delete(name); }));
        }).then(function () {
            return self.clients.claim();
        })
    );
});

self.addEventListener("fetch", function (event) {
    const request = event.request;
    const url = new URL(request.url);

    // ทุกอย่างที่ไม่ใช่ไฟล์ static ให้ไปเซิร์ฟเวอร์ตรง ๆ ไม่แคช ไม่ fallback
    if (request.method !== "GET" ||
        url.origin !== self.location.origin ||
        url.pathname.startsWith("/api")) {
        return;
    }

    const isShellFile = SHELL_FILES.some(function (path) {
        return url.pathname === path;
    });

    if (!isShellFile) {
        return;
    }

    event.respondWith(
        caches.match(request).then(function (cached) {
            // ใช้ของในแคชก่อนเพื่อให้เปิดเร็ว แล้วอัปเดตเบื้องหลัง
            const fetching = fetch(request).then(function (response) {
                if (response && response.ok) {
                    const copy = response.clone();
                    caches.open(CACHE_NAME).then(function (cache) {
                        cache.put(request, copy);
                    });
                }
                return response;
            }).catch(function () {
                return cached;
            });

            return cached || fetching;
        })
    );
});
