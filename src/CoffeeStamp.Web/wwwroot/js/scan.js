// สแกน QR ของลูกค้าฝั่งพนักงาน
// ร้านเป็นฝ่ายสแกนเสมอ ไม่มีกรณีที่ลูกค้าสแกน QR ของร้านแล้วได้แต้ม
//
// ลำดับการเลือกตัวอ่าน QR
// 1. html5-qrcode ถ้ามีในหน้า (ตาม README — ต้องเพิ่มไฟล์ไลบรารีเอง ดู docs/PHASE2.md)
// 2. BarcodeDetector ที่มีมาในเบราว์เซอร์ (Chrome/Edge/Android)
// 3. พิมพ์โค้ดด้วยมือ ใช้ตอนกล้องเสียหรือเบราว์เซอร์ไม่รองรับ
(function () {
    "use strict";

    const MODAL_ID = "cs-scan-modal";
    let state = null;

    function buildModal() {
        const wrap = document.createElement("div");
        wrap.id = MODAL_ID;
        wrap.className = "modal-backdrop";
        wrap.hidden = true;
        wrap.innerHTML =
            '<div class="modal" role="dialog" aria-modal="true" aria-labelledby="cs-scan-title">' +
            '<h2 id="cs-scan-title">สแกน QR ลูกค้า</h2>' +
            '<p class="hint" id="cs-scan-order"></p>' +
            '<div id="cs-scan-view" class="scan-view">' +
            '<video id="cs-scan-video" playsinline muted></video>' +
            '<div id="cs-scan-reader"></div>' +
            "</div>" +
            '<p class="hint" id="cs-scan-status">กำลังเปิดกล้อง…</p>' +
            '<div class="field">' +
            '<label for="cs-scan-manual">หรือพิมพ์โค้ดจากหน้าจอลูกค้า</label>' +
            '<input type="text" id="cs-scan-manual" autocomplete="off" spellcheck="false" />' +
            "</div>" +
            '<div class="modal-actions">' +
            '<button type="button" class="btn btn-green" id="cs-scan-submit">ใช้โค้ดนี้</button>' +
            '<button type="button" class="btn btn-outline" id="cs-scan-close">ข้าม</button>' +
            "</div></div>";

        document.body.appendChild(wrap);
        return wrap;
    }

    function elements() {
        const modal = document.getElementById(MODAL_ID) || buildModal();
        return {
            modal: modal,
            order: modal.querySelector("#cs-scan-order"),
            status: modal.querySelector("#cs-scan-status"),
            video: modal.querySelector("#cs-scan-video"),
            reader: modal.querySelector("#cs-scan-reader"),
            manual: modal.querySelector("#cs-scan-manual"),
            submit: modal.querySelector("#cs-scan-submit"),
            close: modal.querySelector("#cs-scan-close")
        };
    }

    function setStatus(text) {
        elements().status.textContent = text;
    }

    async function startHtml5Qrcode(el, handle) {
        const scanner = new window.Html5Qrcode("cs-scan-reader");
        state.stop = function () { return scanner.stop().catch(function () { }); };

        await scanner.start(
            { facingMode: "environment" },
            { fps: 10, qrbox: 240 },
            function (text) { handle(text); },
            function () { /* ยังไม่เจอ QR ในเฟรมนี้ ไม่ต้องทำอะไร */ }
        );

        el.video.hidden = true;
        setStatus("ส่อง QR บนหน้าจอลูกค้าให้อยู่ในกรอบ");
    }

    async function startBarcodeDetector(el, handle) {
        const detector = new window.BarcodeDetector({ formats: ["qr_code"] });
        const stream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "environment" }
        });

        el.reader.hidden = true;
        el.video.hidden = false;
        el.video.srcObject = stream;
        await el.video.play();
        setStatus("ส่อง QR บนหน้าจอลูกค้าให้อยู่ในกรอบ");

        const timer = setInterval(async function () {
            if (!state) { return; }
            try {
                const codes = await detector.detect(el.video);
                if (codes && codes.length > 0) { handle(codes[0].rawValue); }
            } catch (err) {
                // เฟรมเสียเป็นครั้งคราวเป็นเรื่องปกติ ปล่อยให้รอบถัดไปทำต่อ
            }
        }, 200);

        state.stop = function () {
            clearInterval(timer);
            stream.getTracks().forEach(function (track) { track.stop(); });
            el.video.srcObject = null;
            return Promise.resolve();
        };
    }

    async function startCamera(el, handle) {
        try {
            if (window.Html5Qrcode) {
                await startHtml5Qrcode(el, handle);
                return;
            }

            if (window.BarcodeDetector && navigator.mediaDevices) {
                await startBarcodeDetector(el, handle);
                return;
            }

            el.video.hidden = true;
            el.reader.hidden = true;
            setStatus("เบราว์เซอร์นี้อ่าน QR เองไม่ได้ กรุณาพิมพ์โค้ดจากหน้าจอลูกค้า");
        } catch (err) {
            el.video.hidden = true;
            el.reader.hidden = true;
            setStatus("เปิดกล้องไม่ได้ (" + (err.message || err) + ") กรุณาพิมพ์โค้ดแทน");
        }
    }

    function close() {
        const el = elements();
        const stopping = state && state.stop ? state.stop() : Promise.resolve();
        state = null;

        return Promise.resolve(stopping).then(function () {
            el.modal.hidden = true;
            el.manual.value = "";
        });
    }

    // เปิดหน้าจอสแกน onToken จะถูกเรียกครั้งเดียวเท่านั้น
    function open(options) {
        const el = elements();
        const onToken = options.onToken;

        el.modal.hidden = false;
        el.order.textContent = options.label || "";
        el.manual.value = "";
        el.video.hidden = false;
        el.reader.hidden = false;
        setStatus("กำลังเปิดกล้อง…");

        state = { handled: false, stop: null };

        function handle(token) {
            if (!state || state.handled) { return; }
            state.handled = true;
            close().then(function () { onToken(token); });
        }

        el.submit.onclick = function () {
            const value = el.manual.value.trim();
            if (value) { handle(value); }
        };

        el.manual.onkeydown = function (event) {
            if (event.key === "Enter") {
                event.preventDefault();
                el.submit.click();
            }
        };

        el.close.onclick = function () { close(); };

        startCamera(el, handle);
    }

    window.CoffeeStampScan = { open: open, close: close };
})();
