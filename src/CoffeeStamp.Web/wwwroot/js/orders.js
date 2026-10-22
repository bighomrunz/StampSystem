// หน้าออเดอร์: ยืนยันรับเงิน ยกเลิกออเดอร์ที่ยังไม่จ่าย และยกเลิกออเดอร์ที่จ่ายแล้วด้วย PIN ผู้จัดการ
(function () {
    "use strict";

    const api = window.CoffeeStamp.api;
    const toast = window.CoffeeStamp.toast;

    const voidModal = document.getElementById("void-modal");
    const voidOrderNo = document.getElementById("void-order-no");
    const voidReason = document.getElementById("void-reason");
    const voidPin = document.getElementById("void-pin");
    let voidOrderId = null;

    function closeVoidModal() {
        voidModal.hidden = true;
        voidOrderId = null;
        voidReason.value = "";
        voidPin.value = "";
    }

    document.addEventListener("click", async function (event) {
        const confirmBtn = event.target.closest("[data-confirm-order]");
        if (confirmBtn) {
            confirmBtn.disabled = true;
            try {
                await api.post("/api/orders/" + confirmBtn.dataset.confirmOrder + "/confirm-payment");
                toast("ยืนยันรับเงินแล้ว", "success");
                window.location.reload();
            } catch (err) {
                toast(err.message, "error");
                confirmBtn.disabled = false;
            }
            return;
        }

        const cancelBtn = event.target.closest("[data-cancel-order]");
        if (cancelBtn) {
            if (!window.confirm("ยกเลิกออเดอร์ที่ยังไม่รับเงินนี้?")) { return; }
            cancelBtn.disabled = true;
            try {
                await api.post("/api/orders/" + cancelBtn.dataset.cancelOrder + "/cancel",
                    { reason: "ยกเลิกจากหน้าออเดอร์" });
                toast("ยกเลิกออเดอร์แล้ว");
                window.location.reload();
            } catch (err) {
                toast(err.message, "error");
                cancelBtn.disabled = false;
            }
            return;
        }

        const voidBtn = event.target.closest("[data-void-order]");
        if (voidBtn) {
            voidOrderId = voidBtn.dataset.voidOrder;
            voidOrderNo.textContent = voidBtn.dataset.orderNo || "";
            voidModal.hidden = false;
            voidReason.focus();
            return;
        }

        // สแตมป์ผิดคน: ย้ายแต้มของออเดอร์นี้ไปให้ลูกค้าที่ถูกต้อง ไม่ต้องออกบัตรใหม่
        const transferBtn = event.target.closest("[data-transfer-order]");
        if (transferBtn && window.CoffeeStampPicker) {
            const orderId = transferBtn.dataset.transferOrder;

            window.CoffeeStampPicker.open({
                label: "ออเดอร์ " + (transferBtn.dataset.orderNo || "") +
                    " · เลือกลูกค้าที่ควรได้แต้ม",
                onCustomer: async function (customerId) {
                    const reason = window.prompt("เหตุผลในการย้ายแต้ม", "สแตมป์ผิดคน");
                    if (!reason) { return; }

                    const pin = window.prompt("PIN ผู้จัดการ (การเพิ่มแต้มต้องมีผู้อนุมัติ)");
                    if (!pin) { return; }

                    try {
                        const result = await api.post("/api/stamp/transfer/" + orderId, {
                            toCustomerId: customerId,
                            reason: reason,
                            managerPin: pin
                        });
                        toast("ย้าย " + result.moved + " แต้มไปให้ " +
                            (result.toCustomerName || "ลูกค้า") + " แล้ว", "success");
                        window.location.reload();
                    } catch (err) {
                        toast(err.message, "error");
                    }
                }
            });
            return;
        }

        // ออกแต้มย้อนหลังให้ออเดอร์ของวันนี้ที่ยังไม่ได้ออกแต้ม
        const stampBtn = event.target.closest("[data-stamp-order]");
        if (stampBtn && window.CoffeeStampPicker) {
            const orderId = stampBtn.dataset.stampOrder;
            const label = "ออเดอร์ " + (stampBtn.dataset.orderNo || "") +
                " · " + (stampBtn.dataset.stamps || "0") + " แต้ม";

            async function issue(url, body) {
                try {
                    const result = await api.post(url, body);
                    toast("ได้รับ " + result.earned + " แต้ม (รวม " + result.balance +
                        "/" + result.stampsPerReward + ")", "success");
                    window.location.reload();
                } catch (err) {
                    toast(err.message, "error");
                }
            }

            window.CoffeeStampPicker.open({
                label: label,
                onCustomer: function (customerId) {
                    issue("/api/orders/" + orderId + "/stamps-by-customer",
                        { customerId: customerId });
                },
                onQrToken: function (token) {
                    issue("/api/orders/" + orderId + "/stamps", { qrToken: token });
                }
            });
        }
    });

    document.getElementById("void-close").addEventListener("click", closeVoidModal);

    document.getElementById("void-submit").addEventListener("click", async function () {
        if (voidOrderId === null) { return; }

        if (!voidReason.value.trim()) {
            toast("กรุณาระบุเหตุผล", "error");
            return;
        }

        if (!voidPin.value) {
            toast("กรุณากรอก PIN ผู้จัดการ", "error");
            return;
        }

        try {
            await api.post("/api/orders/" + voidOrderId + "/void", {
                reason: voidReason.value.trim(),
                managerPin: voidPin.value
            });
            toast("ยกเลิกออเดอร์แล้ว", "success");
            closeVoidModal();
            window.location.reload();
        } catch (err) {
            toast(err.message, "error");
            voidPin.value = "";
        }
    });
})();
