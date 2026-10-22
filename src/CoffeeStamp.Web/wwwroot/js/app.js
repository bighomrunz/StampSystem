// ฟังก์ชันกลางที่ทุกหน้าใช้: เรียก API, แจ้งเตือน, ออกจากระบบ, ล็อกเอาต์เมื่อ idle
(function () {
    "use strict";

    const csrfToken = document.querySelector('meta[name="csrf-token"]')?.content || "";

    const api = {
        async request(method, url, body) {
            const options = {
                method: method,
                headers: { "X-CSRF-TOKEN": csrfToken },
                credentials: "same-origin"
            };

            if (body !== undefined) {
                options.headers["Content-Type"] = "application/json";
                options.body = JSON.stringify(body);
            }

            const response = await fetch(url, options);

            if (response.status === 401) {
                window.location.href = "/Staff/Login";
                throw new Error("เซสชันหมดอายุ");
            }

            if (response.status === 429) {
                throw new Error("ลองบ่อยเกินไป กรุณารอสักครู่");
            }

            const isJson = (response.headers.get("content-type") || "").includes("json");
            const payload = isJson ? await response.json() : null;

            if (!response.ok) {
                throw new Error((payload && payload.error) || "เกิดข้อผิดพลาด กรุณาลองใหม่");
            }

            return payload;
        },
        get(url) { return this.request("GET", url); },
        post(url, body) { return this.request("POST", url, body === undefined ? {} : body); },
        put(url, body) { return this.request("PUT", url, body); }
    };

    let toastTimer = null;

    function toast(message, kind) {
        const el = document.getElementById("toast");
        if (!el) { return; }
        el.textContent = message;
        el.className = "toast show" + (kind ? " " + kind : "");
        clearTimeout(toastTimer);
        toastTimer = setTimeout(function () { el.className = "toast"; }, 3200);
    }

    function money(value) {
        return Number(value || 0).toLocaleString("th-TH", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    async function logout() {
        try {
            const result = await api.post("/api/staff/logout");
            window.location.href = (result && result.redirectTo) || "/Staff/Login";
        } catch (err) {
            window.location.href = "/Staff/Login";
        }
    }

    document.addEventListener("click", function (event) {
        const target = event.target.closest("[data-logout]");
        if (target) {
            event.preventDefault();
            logout();
        }
    });

    // ล็อกเอาต์อัตโนมัติเมื่อไม่มีการใช้งาน กันเครื่องเปิดค้างไว้หน้าร้าน
    const idleMinutes = Number(document.body.dataset.idleMinutes || 0);
    if (idleMinutes > 0) {
        let lastActivity = Date.now();
        const reset = function () { lastActivity = Date.now(); };

        ["pointerdown", "keydown", "touchstart", "focusin"].forEach(function (name) {
            document.addEventListener(name, reset, { passive: true });
        });

        setInterval(function () {
            if (Date.now() - lastActivity > idleMinutes * 60000) {
                logout();
            }
        }, 15000);
    }

    window.CoffeeStamp = { api: api, toast: toast, money: money, logout: logout };
})();

// ลงทะเบียน service worker เพื่อให้ติดตั้งเป็น PWA บนแท็บเล็ตได้
if ("serviceWorker" in navigator) {
    window.addEventListener("load", function () {
        navigator.serviceWorker.register("/sw.js").catch(function () {
            // ติดตั้งไม่ได้ก็ยังใช้งานผ่านเบราว์เซอร์ได้ปกติ
        });
    });
}
