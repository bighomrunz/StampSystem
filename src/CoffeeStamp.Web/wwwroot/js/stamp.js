// หน้าสแตมป์ — ทางหลักของช่วงที่เปิดใช้แค่การสแตมป์
// เลือกลูกค้า → จำนวนแก้ว → ช่องทางชำระ → กดสแตมป์ครั้งเดียวจบ
// ยอดแต้มทั้งหมดมาจากเซิร์ฟเวอร์ ฝั่งนี้ไม่คำนวณเอง
(function () {
    "use strict";

    const api = window.CoffeeStamp.api;
    const toast = window.CoffeeStamp.toast;

    const el = {
        chosen: document.getElementById("chosen"),
        chosenName: document.getElementById("chosen-name"),
        chosenSub: document.getElementById("chosen-sub"),
        change: document.getElementById("change-customer"),
        pickArea: document.getElementById("pick-area"),
        search: document.getElementById("cust-search"),
        results: document.getElementById("cust-results"),
        scan: document.getElementById("scan-qr"),
        cupGrid: document.getElementById("cup-grid"),
        amount: document.getElementById("amount"),
        payGrid: document.getElementById("pay-grid"),
        stamp: document.getElementById("do-stamp"),
        resultCard: document.getElementById("result-card"),
        resultName: document.getElementById("result-name"),
        resultBalance: document.getElementById("result-balance"),
        resultOf: document.getElementById("result-of"),
        resultNote: document.getElementById("result-note"),
        undo: document.getElementById("undo-stamp"),
        customerLink: document.getElementById("result-customer-link"),
        redeemCard: document.getElementById("redeem-card"),
        redeemSummary: document.getElementById("redeem-summary"),
        redeemOptions: document.getElementById("redeem-options")
    };

    let picked = null;       // { id, name, phoneDisplay, balance } หรือ { qrToken }
    let cups = 1;
    let payment = "Cash";
    let lastResult = null;   // ผลสแตมป์ครั้งล่าสุด ใช้กับปุ่มยกเลิก
    let searchTimer = null;

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    function refreshStampButton() {
        el.stamp.disabled = picked === null;
    }

    function showPicked(label, sub) {
        el.chosenName.textContent = label;
        el.chosenSub.textContent = sub || "";
        el.chosen.hidden = false;
        el.pickArea.hidden = true;
        refreshStampButton();
        loadRedeemOptions();
    }

    // แสดงปุ่มแลกรางวัลเฉพาะตอนที่ลูกค้ามีแต้มพอจริง ๆ
    // แต้มที่ใช้ได้นับจากฝั่งเซิร์ฟเวอร์ (ไม่รวมก้อนที่หมดอายุแล้ว)
    async function loadRedeemOptions() {
        el.redeemCard.hidden = true;
        if (!picked || !picked.id) { return; }

        try {
            const eligibility = await api.get("/api/rewards/eligibility/" + picked.id);
            if (eligibility.rewardsAvailable < 1) {
                el.redeemSummary.textContent = "มี " + eligibility.usableStamps + " แต้ม · อีก " +
                    eligibility.stampsToNextReward + " แต้มแลกได้ 1 แก้ว";
                el.redeemOptions.innerHTML = "";
                el.redeemCard.hidden = false;
                return;
            }

            const rewards = await api.get("/api/rewards/options");
            const usable = rewards.items.filter(function (i) { return i.canRedeem; });

            el.redeemSummary.textContent = "แลกได้ " + eligibility.rewardsAvailable +
                " แก้ว (ใช้ " + rewards.stampsPerReward + " แต้มต่อแก้ว)";

            el.redeemOptions.innerHTML = '<div class="staff-list">' + usable.map(function (i) {
                const extra = i.topUpAmount > 0
                    ? ' <span class="text-muted">จ่ายเพิ่ม ' + money(i.topUpAmount) + " ฿</span>"
                    : "";
                return '<button type="button" class="btn btn-lg btn-outline" data-redeem="' +
                    i.menuItemId + '" data-topup="' + i.topUpAmount + '">' +
                    escapeHtml(i.name) + extra + "</button>";
            }).join("") + "</div>";

            el.redeemCard.hidden = false;
        } catch (err) {
            toast(err.message, "error");
        }
    }

    document.addEventListener("click", async function (event) {
        const button = event.target.closest("[data-redeem]");
        if (!button || !picked || !picked.id) { return; }

        const topUp = Number(button.dataset.topup || 0);
        const name = button.textContent.trim();
        const confirmText = topUp > 0
            ? "แลก " + name + " โดยเก็บเงินเพิ่ม " + money(topUp) + " บาท?"
            : "แลก " + name + " ฟรี?";

        if (!window.confirm(confirmText)) { return; }

        button.disabled = true;
        try {
            const result = await api.post("/api/rewards/redeem", {
                customerId: picked.id,
                menuItemId: Number(button.dataset.redeem),
                topUpPaymentMethod: payment
            });

            let message = "แลก " + result.menuName + " แล้ว ใช้ " + result.stampsUsed + " แต้ม";
            if (result.topUpAmount > 0) {
                message += " · เก็บเพิ่ม " + money(result.topUpAmount) + " ฿";
            }
            toast(message, "success");

            el.resultCard.hidden = false;
            el.resultName.textContent = result.customerName || "สมาชิก";
            el.resultBalance.textContent = result.balance;
            el.resultOf.textContent = "/" + result.stampsPerReward;
            el.resultNote.textContent = "แลก " + result.menuName + " · ออเดอร์ " + result.orderNo;
            lastResult = null;

            await loadRedeemOptions();
        } catch (err) {
            toast(err.message, "error");
            button.disabled = false;
        }
    });

    function resetPick() {
        picked = null;
        el.chosen.hidden = true;
        el.pickArea.hidden = false;
        el.search.value = "";
        el.results.innerHTML = "";
        el.redeemCard.hidden = true;
        refreshStampButton();
        el.search.focus();
    }

    async function search() {
        try {
            const rows = await api.get("/api/customers?q=" + encodeURIComponent(el.search.value));
            if (rows.length === 0) {
                el.results.innerHTML =
                    '<p class="text-muted">ไม่พบสมาชิก — กด "เพิ่มสมาชิกใหม่" ได้เลย</p>';
                return;
            }

            el.results.innerHTML = '<div class="staff-list">' + rows.map(function (c) {
                return '<button type="button" class="btn btn-lg btn-outline" ' +
                    'data-pick="' + c.id + '" ' +
                    'data-name="' + escapeHtml(c.name) + '" ' +
                    'data-phone="' + escapeHtml(c.phoneDisplay) + '" ' +
                    'data-balance="' + c.balance + '">' +
                    escapeHtml(c.name) + ' <span class="text-muted">' +
                    escapeHtml(c.phoneDisplay) + '</span> <span class="mono">' +
                    c.balance + ' แต้ม</span></button>';
            }).join("") + "</div>";
        } catch (err) {
            el.results.innerHTML = '<div class="alert alert-error">' +
                escapeHtml(err.message) + "</div>";
        }
    }

    el.search.addEventListener("input", function () {
        clearTimeout(searchTimer);
        searchTimer = setTimeout(search, 250);
    });

    el.results.addEventListener("click", function (event) {
        const button = event.target.closest("[data-pick]");
        if (!button) { return; }

        picked = { id: Number(button.dataset.pick), name: button.dataset.name };
        showPicked(button.dataset.name,
            button.dataset.phone + " · มีอยู่ " + button.dataset.balance + " แต้ม");
    });

    el.change.addEventListener("click", resetPick);

    el.scan.addEventListener("click", function () {
        if (!window.CoffeeStampScan) {
            toast("เบราว์เซอร์นี้สแกนไม่ได้ ใช้การค้นชื่อแทน", "error");
            return;
        }

        window.CoffeeStampScan.open({
            label: "สแกน QR จากหน้าบัตรของลูกค้า",
            onToken: async function (token) {
                try {
                    // อ่าน QR เพื่อยืนยันว่าเป็นใคร การอ่านไม่ทำให้ QR ใช้ไม่ได้
                    const summary = await api.post("/api/customers/resolve", { qrToken: token });
                    picked = { qrToken: token, name: summary.displayName };
                    showPicked(summary.displayName || "สมาชิก",
                        "จาก QR · มีอยู่ " + summary.balance + " แต้ม");
                } catch (err) {
                    toast(err.message, "error");
                }
            }
        });
    });

    el.cupGrid.addEventListener("click", function (event) {
        const button = event.target.closest("[data-cups]");
        if (!button) { return; }

        cups = Number(button.dataset.cups);
        Array.from(el.cupGrid.children).forEach(function (b) {
            b.classList.toggle("cup-selected", b === button);
        });
    });

    el.payGrid.addEventListener("click", function (event) {
        const button = event.target.closest("[data-pay]");
        if (!button) { return; }

        payment = button.dataset.pay;
        Array.from(el.payGrid.children).forEach(function (b) {
            b.classList.toggle("pay-selected", b === button);
        });
    });

    el.stamp.addEventListener("click", async function () {
        if (picked === null) { return; }

        el.stamp.disabled = true;
        try {
            const body = {
                cups: cups,
                paymentMethod: payment,
                amount: el.amount.value === "" ? null : Number(el.amount.value)
            };
            if (picked.id) { body.customerId = picked.id; } else { body.qrToken = picked.qrToken; }

            const result = await api.post("/api/stamp", body);
            lastResult = result;

            el.resultCard.hidden = false;
            el.resultName.textContent = result.customerName || "สมาชิก";
            el.resultBalance.textContent = result.balance;
            el.resultOf.textContent = "/" + result.stampsPerReward;
            el.customerLink.href = "/Customers?focus=" + result.customerId;

            let note = "ได้รับ " + result.earned + " แต้ม · ออเดอร์ " + result.orderNo;
            if (result.rewardsAvailable > 0) {
                note += " · แลกฟรีได้ " + result.rewardsAvailable + " แก้ว";
            }
            if (result.expiresAt) { note += " · หมดอายุ " + result.expiresAt; }
            el.resultNote.textContent = note;

            toast("สแตมป์ " + result.earned + " แต้มแล้ว", "success");
            resetPick();
            el.amount.value = "";
        } catch (err) {
            toast(err.message, "error");
        } finally {
            refreshStampButton();
        }
    });

    // สแตมป์ผิดแก้ได้ทันทีโดยไม่ต้องออกบัตรใหม่ — ลดแต้มพนักงานทำได้เอง
    el.undo.addEventListener("click", async function () {
        if (!lastResult) { return; }

        const reason = window.prompt("ยกเลิกสแตมป์ครั้งล่าสุด เหตุผล?", "สแตมป์ผิด");
        if (!reason) { return; }

        el.undo.disabled = true;
        try {
            const result = await api.post("/api/stamp/correct/" + lastResult.customerId, {
                delta: -lastResult.earned,
                reason: reason
            });

            el.resultBalance.textContent = result.balance;
            el.resultNote.textContent = "ยกเลิกแล้ว " + lastResult.earned +
                " แต้ม · ยอดคงเหลือ " + result.balance;
            toast("ยกเลิกสแตมป์แล้ว", "success");
            lastResult = null;
        } catch (err) {
            toast(err.message, "error");
        } finally {
            el.undo.disabled = false;
        }
    });

    el.search.focus();
})();
