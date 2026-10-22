// ตัวเลือกลูกค้าสำหรับออกแต้มช่วงคีย์มือ (ลูกค้ายังไม่มี QR ของตัวเอง)
// ใช้ร่วมกันทั้งหน้าขายและหน้าออเดอร์ ถ้าเปิด LIFF แล้วยังมีปุ่มสแกน QR ให้เลือกด้วย
(function () {
    "use strict";

    const MODAL_ID = "cs-picker-modal";
    let onPicked = null;
    let searchTimer = null;

    function build() {
        const wrap = document.createElement("div");
        wrap.id = MODAL_ID;
        wrap.className = "modal-backdrop";
        wrap.hidden = true;
        wrap.innerHTML =
            '<div class="modal" role="dialog" aria-modal="true" aria-labelledby="cs-picker-title">' +
            '<h2 id="cs-picker-title">สะสมแต้มให้ลูกค้า</h2>' +
            '<p class="hint" id="cs-picker-label"></p>' +
            '<div class="field">' +
            '<label for="cs-picker-search">ค้นชื่อหรือเบอร์โทร</label>' +
            '<input type="search" id="cs-picker-search" autocomplete="off" />' +
            "</div>" +
            '<div id="cs-picker-results" aria-live="polite"></div>' +
            '<div class="modal-actions">' +
            '<button type="button" class="btn btn-outline" id="cs-picker-scan">สแกน QR</button>' +
            '<button type="button" class="btn btn-outline" id="cs-picker-skip">ข้าม</button>' +
            "</div></div>";

        document.body.appendChild(wrap);
        return wrap;
    }

    function elements() {
        const modal = document.getElementById(MODAL_ID) || build();
        return {
            modal: modal,
            label: modal.querySelector("#cs-picker-label"),
            search: modal.querySelector("#cs-picker-search"),
            results: modal.querySelector("#cs-picker-results"),
            scan: modal.querySelector("#cs-picker-scan"),
            skip: modal.querySelector("#cs-picker-skip")
        };
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    async function search() {
        const el = elements();
        try {
            const rows = await window.CoffeeStamp.api.get(
                "/api/customers?q=" + encodeURIComponent(el.search.value));

            if (rows.length === 0) {
                el.results.innerHTML =
                    '<p class="text-muted">ไม่พบสมาชิก — เพิ่มสมาชิกใหม่ได้ที่หน้า "สมาชิก"</p>';
                return;
            }

            el.results.innerHTML = '<div class="staff-list">' + rows.map(function (c) {
                return '<button type="button" class="btn btn-lg btn-outline" data-pick="' + c.id +
                    '">' + escapeHtml(c.name) +
                    ' <span class="text-muted">' + escapeHtml(c.phoneDisplay) + "</span>" +
                    ' <span class="mono">' + c.balance + " แต้ม</span></button>";
            }).join("") + "</div>";
        } catch (err) {
            el.results.innerHTML = '<div class="alert alert-error">' +
                escapeHtml(err.message) + "</div>";
        }
    }

    function close() {
        const el = elements();
        el.modal.hidden = true;
        el.search.value = "";
        el.results.innerHTML = "";
        onPicked = null;
    }

    // options: { label, onCustomer(customerId), onQrToken(token) }
    function open(options) {
        const el = elements();
        onPicked = options;

        el.label.textContent = options.label || "";
        el.modal.hidden = false;
        el.results.innerHTML = "";
        el.search.value = "";
        el.scan.hidden = typeof options.onQrToken !== "function" || !window.CoffeeStampScan;
        search();
        el.search.focus();

        el.search.oninput = function () {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(search, 250);
        };

        el.skip.onclick = close;

        el.scan.onclick = function () {
            const handler = onPicked;
            close();
            window.CoffeeStampScan.open({
                label: options.label,
                onToken: function (token) { handler.onQrToken(token); }
            });
        };
    }

    document.addEventListener("click", function (event) {
        const button = event.target.closest("[data-pick]");
        if (!button || !onPicked) { return; }

        const handler = onPicked;
        const customerId = Number(button.dataset.pick);
        close();
        handler.onCustomer(customerId);
    });

    window.CoffeeStampPicker = { open: open, close: close };
})();
