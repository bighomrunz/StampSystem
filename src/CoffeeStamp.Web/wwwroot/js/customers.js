// หน้าสมาชิกฝั่งพนักงาน: ค้นหา เพิ่มสมาชิกที่เคาน์เตอร์ ย้ายบัตรกระดาษ และผูกบัญชี LINE
(function () {
    "use strict";

    const api = window.CoffeeStamp.api;
    const toast = window.CoffeeStamp.toast;

    const el = {
        search: document.getElementById("search"),
        results: document.getElementById("search-results"),
        name: document.getElementById("new-name"),
        phone: document.getElementById("new-phone"),
        consent: document.getElementById("new-consent"),
        create: document.getElementById("create-customer"),
        modal: document.getElementById("customer-modal"),
        title: document.getElementById("detail-title"),
        body: document.getElementById("detail-body"),
        close: document.getElementById("detail-close")
    };

    let searchTimer = null;

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    function renderResults(rows) {
        if (rows.length === 0) {
            el.results.innerHTML = '<p class="text-muted">ไม่พบสมาชิกที่ตรงกับคำค้น</p>';
            return;
        }

        el.results.innerHTML = '<ul class="plain-list">' + rows.map(function (c) {
            const badges =
                (c.isLineLinked ? '<span class="badge badge-paid">ผูก LINE แล้ว</span> ' : "") +
                (c.hasMigratedCard ? '<span class="badge badge-cancelled">ย้ายบัตรแล้ว</span>' : "");

            return "<li><span><strong>" + escapeHtml(c.name) + "</strong><br>" +
                '<span class="text-muted">' + escapeHtml(c.phoneDisplay) + "</span> " + badges +
                "</span>" +
                '<span><span class="mono" style="font-size:1.2rem;font-weight:800">' +
                c.balance + "</span> แต้ม " +
                '<button type="button" class="btn btn-sm btn-outline" data-detail="' + c.id +
                '">ดู</button></span></li>";
        }).join("") + "</ul>";
    }

    async function doSearch() {
        try {
            const rows = await api.get("/api/customers?q=" + encodeURIComponent(el.search.value));
            renderResults(rows);
        } catch (err) {
            el.results.innerHTML = '<div class="alert alert-error">' +
                escapeHtml(err.message) + "</div>";
        }
    }

    el.search.addEventListener("input", function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(doSearch, 250);
    });

    el.create.addEventListener("click", async function () {
        if (!el.consent.checked) {
            toast("ต้องแจ้งข้อความขอความยินยอมและได้รับความยินยอมก่อน", "error");
            return;
        }

        el.create.disabled = true;
        try {
            const created = await api.post("/api/customers", {
                name: el.name.value,
                phone: el.phone.value,
                consentGivenAtCounter: true
            });

            toast("เพิ่มสมาชิก " + created.name + " แล้ว", "success");
            el.name.value = "";
            el.phone.value = "";
            el.consent.checked = false;
            el.search.value = created.name;
            await doSearch();
        } catch (err) {
            toast(err.message, "error");
        } finally {
            el.create.disabled = false;
        }
    });

    function historyRows(history) {
        if (!history || history.length === 0) {
            return '<p class="text-muted">ยังไม่มีประวัติแต้ม</p>';
        }

        return '<ul class="plain-list">' + history.map(function (h) {
            const sign = h.quantity > 0 ? "+" : "";
            const cls = h.quantity > 0 ? "qty-plus" : "qty-minus";
            return "<li><span>" + escapeHtml(h.type) +
                (h.orderNo ? " " + escapeHtml(h.orderNo) : "") +
                '<br><span class="text-muted">' + escapeHtml(h.createdAt) +
                (h.expiresAt ? " · หมดอายุ " + escapeHtml(h.expiresAt) : "") + "</span></span>" +
                '<span class="' + cls + '">' + sign + h.quantity + "</span></li>";
        }).join("") + "</ul>";
    }

    async function openDetail(id) {
        try {
            const c = await api.get("/api/customers/" + id);
            el.title.textContent = c.name;

            let html =
                '<div class="summary-grid">' +
                '<div class="stat"><div class="label">แต้มคงเหลือ</div>' +
                '<div class="value mono">' + c.summary.balance + "/" + c.summary.stampsPerReward +
                "</div></div>" +
                '<div class="stat"><div class="label">แลกฟรีได้</div>' +
                '<div class="value mono">' + c.summary.rewardsAvailable + " แก้ว</div></div>" +
                "</div>" +
                '<p class="hint">เบอร์ ' + escapeHtml(c.phoneDisplay) +
                " · สมัครทาง " + (c.source === "Counter" ? "เคาน์เตอร์" : "LINE") +
                " · ยินยอม " + escapeHtml(c.consentAt || "-") +
                (c.consentSource ? " (" + (c.consentSource === "Counter" ? "เคาน์เตอร์" : "LIFF") + ")" : "") +
                "</p>";

            // ย้ายบัตรกระดาษได้ครั้งเดียวต่อคน ถ้าเคยย้ายแล้วจะไม่แสดงฟอร์มอีก
            if (!c.hasMigratedCard) {
                html +=
                    '<div class="card-title mt-2">ย้ายแต้มจากบัตรกระดาษ</div>' +
                    '<p class="hint">บัตรที่เต็มแล้วให้ลูกค้าแลกฟรีหน้าร้านแทนการย้าย ' +
                    "ย้ายได้ครั้งเดียวต่อคน และต้องใช้ PIN ของเจ้าของร้าน</p>" +
                    '<div class="field-row">' +
                    '<div class="field"><label for="mig-stamps">จำนวนแต้มบนบัตร</label>' +
                    '<input type="number" id="mig-stamps" min="1" max="' +
                    (c.summary.stampsPerReward - 1) + '" inputmode="numeric" /></div>' +
                    '<div class="field"><label for="mig-pin">PIN เจ้าของร้าน</label>' +
                    '<input type="password" id="mig-pin" inputmode="numeric" maxlength="6" /></div>' +
                    "</div>" +
                    '<div class="field"><label for="mig-note">หมายเหตุ (ไม่บังคับ)</label>' +
                    '<input type="text" id="mig-note" maxlength="300" /></div>' +
                    '<button type="button" class="btn btn-amber btn-block" data-migrate="' + c.id +
                    '">ย้ายแต้มเข้าระบบ</button>';
            }

            if (!c.isLineLinked) {
                html +=
                    '<div class="card-title mt-2">ผูกบัญชี LINE</div>' +
                    '<p class="hint">ให้ลูกค้าเปิดหน้าบัตรในแอป LINE แล้วสแกน QR ของลูกค้า ' +
                    "แต้มที่มีอยู่จะยังอยู่ครบ</p>" +
                    '<button type="button" class="btn btn-outline btn-block" data-link="' + c.id +
                    '">สแกน QR เพื่อผูกบัญชี</button>';
            }

            html +=


                '<div class="card-title mt-2">แก้แต้มที่สแตมป์ผิด</div>' +


                '<p class="hint">ไม่ต้องออกบัตรใหม่ ระบบเก็บทั้งรายการเดิมและรายการแก้ไว้ใน ledger<br>' +


                "ลดแต้มพนักงานทำได้เอง เพิ่มแต้มต้องใช้ PIN ผู้จัดการ</p>" +


                '<div class="field-row">' +


                '<div class="field"><label for="fix-delta">จำนวน (ใส่ลบเพื่อลด เช่น -1)</label>' +


                '<input type="number" id="fix-delta" inputmode="numeric" value="-1" /></div>' +


                '<div class="field"><label for="fix-pin">PIN ผู้จัดการ (เฉพาะตอนเพิ่มแต้ม)</label>' +


                '<input type="password" id="fix-pin" inputmode="numeric" maxlength="6" /></div>' +


                "</div>" +


                '<div class="field"><label for="fix-reason">เหตุผล</label>' +


                '<input type="text" id="fix-reason" maxlength="300" value="สแตมป์ผิด" /></div>' +


                '<button type="button" class="btn btn-red btn-block" data-fix="' + c.id +


                '">บันทึกการแก้แต้ม</button>';



            html += '<div class="card-title mt-2">ประวัติแต้ม</div>' + historyRows(c.history);

            el.body.innerHTML = html;
            el.modal.hidden = false;
        } catch (err) {
            toast(err.message, "error");
        }
    }

    el.close.addEventListener("click", function () { el.modal.hidden = true; });

    document.addEventListener("click", async function (event) {
        const detailBtn = event.target.closest("[data-detail]");
        if (detailBtn) {
            openDetail(detailBtn.dataset.detail);
            return;
        }

        const migrateBtn = event.target.closest("[data-migrate]");
        if (migrateBtn) {
            const stamps = Number(document.getElementById("mig-stamps").value || 0);
            const pin = document.getElementById("mig-pin").value;
            const note = document.getElementById("mig-note").value;

            if (!stamps) { toast("กรอกจำนวนแต้มบนบัตร", "error"); return; }
            if (!pin) { toast("ต้องใช้ PIN ของเจ้าของร้าน", "error"); return; }

            migrateBtn.disabled = true;
            try {
                const result = await api.post(
                    "/api/customers/" + migrateBtn.dataset.migrate + "/paper-card",
                    { stamps: stamps, ownerPin: pin, note: note });

                toast("ย้ายเข้าระบบ " + result.migrated + " แต้ม (รวม " + result.balance + ")",
                    "success");
                await openDetail(migrateBtn.dataset.migrate);
                await doSearch();
            } catch (err) {
                toast(err.message, "error");
                migrateBtn.disabled = false;
            }
            return;
        }

        // แก้แต้มที่สแตมป์ผิด ลดแต้มพนักงานทำได้เอง เพิ่มแต้มต้องใช้ PIN ผู้จัดการ
        const fixBtn = event.target.closest("[data-fix]");
        if (fixBtn) {
            const delta = Number(document.getElementById("fix-delta").value || 0);
            const reason = document.getElementById("fix-reason").value;
            const pin = document.getElementById("fix-pin").value;

            if (!delta) { toast("ใส่จำนวนแต้มที่ต้องการแก้", "error"); return; }
            if (!reason.trim()) { toast("ต้องระบุเหตุผล", "error"); return; }

            fixBtn.disabled = true;
            try {
                const result = await api.post("/api/stamp/correct/" + fixBtn.dataset.fix, {
                    delta: delta,
                    reason: reason,
                    managerPin: pin
                });

                toast("แก้แต้มแล้ว ยอดคงเหลือ " + result.balance, "success");
                await openDetail(fixBtn.dataset.fix);
                await doSearch();
            } catch (err) {
                toast(err.message, "error");
                fixBtn.disabled = false;
            }
            return;
        }

        const linkBtn = event.target.closest("[data-link]");
        if (linkBtn && window.CoffeeStampScan) {
            const id = linkBtn.dataset.link;
            window.CoffeeStampScan.open({
                label: "สแกน QR จากหน้าบัตรของลูกค้า",
                onToken: async function (token) {
                    try {
                        await api.post("/api/customers/" + id + "/link-line", { qrToken: token });
                        toast("ผูกบัญชี LINE แล้ว", "success");
                        await openDetail(id);
                    } catch (err) {
                        toast(err.message, "error");
                    }
                }
            });
        }
    });

    doSearch();
})();
