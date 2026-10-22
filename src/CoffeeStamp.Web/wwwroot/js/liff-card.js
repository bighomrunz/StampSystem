// หน้าบัตรสะสมแต้มของลูกค้าใน LIFF
// ขั้นตอน: liff.init → ขอ ID token → ส่งให้เซิร์ฟเวอร์ verify → ขอความยินยอม → แสดงบัตร + QR
// ฝั่งนี้ไม่คำนวณแต้มเอง ทุกตัวเลขมาจากเซิร์ฟเวอร์
(function () {
    "use strict";

    const body = document.body;

    // token นี้ผูกกับตัวตนของผู้เรียก พอล็อกอินเสร็จต้องขอใหม่ ไม่งั้น POST ถัดไปจะถูกปฏิเสธ
    let csrfToken = document.querySelector('meta[name="csrf-token"]').content;
    const liffId = body.dataset.liffId || "";
    const consentVersion = body.dataset.consentVersion || "";
    const devMock = body.dataset.devMock === "true";

    const el = {
        loading: document.getElementById("loading"),
        error: document.getElementById("error"),
        consent: document.getElementById("consent"),
        card: document.getElementById("card"),
        name: document.getElementById("customer-name"),
        balance: document.getElementById("balance"),
        perReward: document.getElementById("per-reward"),
        dots: document.getElementById("stamp-dots"),
        rewardNote: document.getElementById("reward-note"),
        qrImage: document.getElementById("qr-image"),
        qrTimer: document.getElementById("qr-timer"),
        qrRefresh: document.getElementById("qr-refresh"),
        expiringCard: document.getElementById("expiring-card"),
        expiringList: document.getElementById("expiring-list"),
        historyList: document.getElementById("history-list"),
        historyEmpty: document.getElementById("history-empty"),
        consentAt: document.getElementById("consent-at"),
        consentAccept: document.getElementById("consent-accept"),
        withdraw: document.getElementById("withdraw"),
        toast: document.getElementById("toast")
    };

    let qrTimerHandle = null;
    let qrCountdown = 0;

    function toast(message, kind) {
        el.toast.textContent = message;
        el.toast.className = "toast show" + (kind ? " " + kind : "");
        setTimeout(function () { el.toast.className = "toast"; }, 3000);
    }

    function showError(message) {
        el.loading.hidden = true;
        el.error.hidden = false;
        el.error.textContent = message;
    }

    async function refreshCsrf() {
        try {
            const response = await fetch("/api/liff/csrf", { credentials: "same-origin" });
            if (!response.ok) { return; }
            const payload = await response.json();
            if (payload && payload.token) { csrfToken = payload.token; }
        } catch (err) {
            // ขอ token ใหม่ไม่ได้ ปล่อยให้ใช้ตัวเดิมแล้วไปเจอ error ตอนเรียกจริง
        }
    }

    async function api(method, url, body, isRetry) {
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
        const isJson = (response.headers.get("content-type") || "").includes("json");
        const payload = isJson ? await response.json() : null;

        if (!response.ok) {
            // token หมดอายุเพราะตัวตนเปลี่ยน ขอใหม่แล้วลองอีกครั้งเดียว
            if (response.status === 400 && !isRetry &&
                payload && payload.error && payload.error.indexOf("เซสชันหมดอายุ") >= 0) {
                await refreshCsrf();
                return api(method, url, body, true);
            }

            const error = new Error((payload && payload.error) || "เกิดข้อผิดพลาด กรุณาลองใหม่");
            error.status = response.status;
            throw error;
        }
        return payload;
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    function renderDots(balance, perReward) {
        if (!perReward || perReward < 1) { el.dots.innerHTML = ""; return; }

        // แสดงความคืบหน้าของรอบปัจจุบัน ไม่ใช่แต้มทั้งหมด
        const inCycle = balance % perReward;
        const filled = balance > 0 && inCycle === 0 ? perReward : inCycle;

        let html = "";
        for (let i = 0; i < perReward; i++) {
            html += '<span class="dot' + (i < filled ? " dot-filled" : "") + '"></span>';
        }
        el.dots.innerHTML = html;
    }

    function renderSummary(summary) {
        el.balance.textContent = summary.balance;
        el.perReward.textContent = summary.stampsPerReward;
        renderDots(summary.balance, summary.stampsPerReward);

        if (summary.rewardsAvailable > 0) {
            el.rewardNote.textContent =
                "แลกเครื่องดื่มฟรีได้ " + summary.rewardsAvailable + " แก้ว";
            el.rewardNote.className = "stamp-note stamp-note-ready";
        } else {
            el.rewardNote.textContent =
                "อีก " + summary.stampsToNextReward + " แต้มแลกฟรี 1 แก้ว";
            el.rewardNote.className = "stamp-note";
        }

        const expiring = summary.expiring || [];
        if (expiring.length > 0) {
            el.expiringCard.hidden = false;
            el.expiringList.innerHTML = expiring.map(function (e) {
                const parts = e.expiresOn.split("-");
                const shown = parts.length === 3
                    ? parts[2] + "/" + parts[1] + "/" + parts[0]
                    : e.expiresOn;
                return "<li><span>" + e.stamps + " แต้ม</span>" +
                    '<span class="text-muted">หมดอายุ ' + escapeHtml(shown) + "</span></li>";
            }).join("");
        } else {
            el.expiringCard.hidden = true;
        }
    }

    function renderHistory(history) {
        if (!history || history.length === 0) {
            el.historyList.innerHTML = "";
            el.historyEmpty.hidden = false;
            return;
        }

        el.historyEmpty.hidden = true;
        el.historyList.innerHTML = history.map(function (h) {
            const sign = h.quantity > 0 ? "+" : "";
            const cls = h.quantity > 0 ? "qty-plus" : "qty-minus";
            return "<li><span>" + escapeHtml(h.typeLabel) +
                (h.orderNo ? ' <span class="text-muted">' + escapeHtml(h.orderNo) + "</span>" : "") +
                '<br><span class="text-muted">' + escapeHtml(h.createdAt) + "</span></span>" +
                '<span class="' + cls + '">' + sign + h.quantity + "</span></li>";
        }).join("");
    }

    function startQrLoop() {
        refreshQr();
        if (qrTimerHandle) { clearInterval(qrTimerHandle); }

        qrTimerHandle = setInterval(function () {
            qrCountdown -= 1;
            if (qrCountdown <= 0) {
                refreshQr();
            } else {
                el.qrTimer.textContent = "QR นี้จะเปลี่ยนในอีก " + qrCountdown + " วินาที";
            }
        }, 1000);
    }

    function refreshQr() {
        // ใส่ตัวกัน cache เพราะ QR เปลี่ยนทุก step
        el.qrImage.src = "/api/liff/qr.png?t=" + Date.now();
        qrCountdown = 30;
        el.qrTimer.textContent = "QR นี้จะเปลี่ยนในอีก " + qrCountdown + " วินาที";
    }

    function showCard(data) {
        el.loading.hidden = true;
        el.consent.hidden = true;
        el.error.hidden = true;
        el.card.hidden = false;

        el.name.textContent = data.displayName || "สมาชิก";
        renderSummary(data.summary);
        renderHistory(data.history);
        el.consentAt.textContent = data.consentAt || "-";
        startQrLoop();
    }

    function showConsent(displayName) {
        el.loading.hidden = true;
        el.card.hidden = true;
        el.error.hidden = true;
        el.consent.hidden = false;
        if (displayName) { el.name.textContent = displayName; }
    }

    async function loadCard(isRetry) {
        let me;
        try {
            me = await api("GET", "/api/liff/me");
        } catch (err) {
            // เซสชันถูกตัด เช่นพนักงานผูกบัตรนี้เข้ากับบัญชี LINE ที่เคาน์เตอร์
            // ล็อกอินใหม่หนึ่งครั้งเพื่อให้ได้ระเบียนที่ถูกต้อง
            if (!isRetry && err.status === 401) {
                await authenticateThenRefreshToken();
                return loadCard(true);
            }
            throw err;
        }

        if (me.needsConsent) {
            showConsent(me.displayName);
        } else {
            showCard(me);
        }
    }

    async function authenticate() {
        // โหมดทดสอบในเครื่อง: ไม่มี LINE SDK ให้ใช้ userId สมมติ
        if (devMock && !liffId) {
            let devUser = window.localStorage.getItem("cs_dev_user");
            if (!devUser) {
                devUser = window.prompt("โหมดทดสอบ: ใส่รหัสลูกค้าสมมติ", "1") || "1";
                window.localStorage.setItem("cs_dev_user", devUser);
            }
            return api("POST", "/api/liff/auth", { devUserId: devUser });
        }

        if (typeof liff === "undefined") {
            throw new Error("โหลด LINE SDK ไม่สำเร็จ กรุณาเปิดหน้านี้จากแอป LINE");
        }

        await liff.init({ liffId: liffId });

        if (!liff.isLoggedIn()) {
            liff.login({ redirectUri: window.location.href });
            return null;
        }

        const idToken = liff.getIDToken();
        if (!idToken) {
            throw new Error("ไม่ได้รับ ID token จาก LINE กรุณาเข้าสู่ระบบใหม่");
        }

        return api("POST", "/api/liff/auth", { idToken: idToken });
    }

    async function authenticateThenRefreshToken() {
        const result = await authenticate();
        // ล็อกอินแล้วตัวตนเปลี่ยน ต้องขอ antiforgery token ใหม่ก่อนเรียก POST อื่น
        if (result !== null) { await refreshCsrf(); }
        return result;
    }

    el.consentAccept.addEventListener("click", async function () {
        el.consentAccept.disabled = true;
        try {
            await api("POST", "/api/liff/consent", { version: consentVersion });
            await loadCard();
            toast("เริ่มใช้บัตรสะสมแต้มได้เลย", "success");
        } catch (err) {
            toast(err.message, "error");
        } finally {
            el.consentAccept.disabled = false;
        }
    });

    el.qrRefresh.addEventListener("click", refreshQr);

    el.withdraw.addEventListener("click", async function () {
        if (!window.confirm(
            "ถอนความยินยอมและปิดบัตรสะสมแต้ม? แต้มที่มีอยู่จะใช้ต่อไม่ได้")) {
            return;
        }

        try {
            await api("POST", "/api/liff/withdraw-consent");
            if (qrTimerHandle) { clearInterval(qrTimerHandle); }
            el.card.hidden = true;
            showError("ปิดบัตรเรียบร้อย ขอบคุณที่ใช้บริการ");
        } catch (err) {
            toast(err.message, "error");
        }
    });

    // หน้าถูกซ่อนไว้ไม่ต้องขอ QR ใหม่ตลอด ประหยัดทั้งเครื่องลูกค้าและเซิร์ฟเวอร์
    document.addEventListener("visibilitychange", function () {
        if (document.hidden) {
            if (qrTimerHandle) { clearInterval(qrTimerHandle); qrTimerHandle = null; }
        } else if (!el.card.hidden && !qrTimerHandle) {
            startQrLoop();
        }
    });

    (async function start() {
        try {
            const result = await authenticateThenRefreshToken();
            if (result === null) { return; }

            if (result.needsConsent) {
                showConsent(result.displayName);
            } else {
                await loadCard();
            }
        } catch (err) {
            showError(err.message);
        }
    })();
})();
