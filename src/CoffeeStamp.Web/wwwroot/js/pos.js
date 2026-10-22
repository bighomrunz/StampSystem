// หน้าขาย: เลือกเมนู รวมยอด เลือกช่องทางชำระ และยืนยันการรับเงิน
// ยอดเงินที่ใช้จริงมาจากเซิร์ฟเวอร์เสมอ ฝั่งนี้คำนวณไว้แค่ให้พนักงานเห็นระหว่างกด
(function () {
    "use strict";

    const api = window.CoffeeStamp.api;
    const toast = window.CoffeeStamp.toast;
    const money = window.CoffeeStamp.money;

    const cart = new Map(); // menuItemId -> { item, quantity }
    let menuItems = [];
    let pendingOrder = null;

    const el = {
        menuRoot: document.getElementById("menu-root"),
        lines: document.getElementById("cart-lines"),
        empty: document.getElementById("cart-empty"),
        total: document.getElementById("cart-total"),
        note: document.getElementById("order-note"),
        clear: document.getElementById("cart-clear"),
        payButtons: Array.from(document.querySelectorAll("[data-pay]")),
        qrModal: document.getElementById("qr-modal"),
        qrImage: document.getElementById("qr-image"),
        qrAmount: document.getElementById("qr-amount"),
        qrOrderNo: document.getElementById("qr-order-no"),
        qrConfirm: document.getElementById("qr-confirm"),
        qrCancel: document.getElementById("qr-cancel"),
        paoModal: document.getElementById("paotang-modal"),
        paoAmount: document.getElementById("paotang-amount"),
        paoOrderNo: document.getElementById("paotang-order-no"),
        paoConfirm: document.getElementById("paotang-confirm"),
        paoCancel: document.getElementById("paotang-cancel")
    };

    async function loadMenu() {
        try {
            menuItems = await api.get("/api/menu");
            renderMenu();
        } catch (err) {
            el.menuRoot.innerHTML =
                '<div class="alert alert-error">โหลดเมนูไม่สำเร็จ: ' + escapeHtml(err.message) + "</div>";
        }
    }

    function renderMenu() {
        if (menuItems.length === 0) {
            el.menuRoot.innerHTML =
                '<div class="alert alert-warn">ยังไม่มีเมนู ให้ผู้จัดการเพิ่มเมนูที่หน้าตั้งค่าก่อน</div>';
            return;
        }

        const groups = new Map();
        menuItems.forEach(function (item) {
            const key = item.category || "อื่น ๆ";
            if (!groups.has(key)) { groups.set(key, []); }
            groups.get(key).push(item);
        });

        let html = "";
        groups.forEach(function (items, category) {
            html += '<div class="menu-cat">' + escapeHtml(category) + "</div>";
            html += '<div class="menu-grid">';
            items.forEach(function (item) {
                html += '<button type="button" class="menu-btn" data-menu-id="' + item.id + '">' +
                    '<span class="name">' + escapeHtml(item.name) + "</span>" +
                    (item.earnsStamp ? "" : '<span class="no-stamp">ไม่ได้แต้ม</span>') +
                    '<span class="price mono">' + money(item.price) + " ฿</span>" +
                    "</button>";
            });
            html += "</div>";
        });

        el.menuRoot.innerHTML = html;
    }

    function addToCart(menuItemId) {
        const item = menuItems.find(function (m) { return m.id === menuItemId; });
        if (!item) { return; }

        const line = cart.get(menuItemId);
        if (line) {
            if (line.quantity >= 99) {
                toast("จำนวนต่อเมนูต้องไม่เกิน 99 แก้ว", "error");
                return;
            }
            line.quantity += 1;
        } else {
            cart.set(menuItemId, { item: item, quantity: 1 });
        }
        renderCart();
    }

    function changeQuantity(menuItemId, delta) {
        const line = cart.get(menuItemId);
        if (!line) { return; }

        line.quantity += delta;
        if (line.quantity <= 0) { cart.delete(menuItemId); }
        renderCart();
    }

    function cartTotal() {
        let total = 0;
        cart.forEach(function (line) { total += line.item.price * line.quantity; });
        return Math.round(total * 100) / 100;
    }

    function renderCart() {
        el.lines.innerHTML = "";

        cart.forEach(function (line, menuItemId) {
            const li = document.createElement("li");
            li.className = "cart-line";
            li.innerHTML =
                "<span>" + escapeHtml(line.item.name) + "</span>" +
                '<span class="qty-group">' +
                '<button type="button" class="qty-btn" data-qty="-1" data-menu-id="' + menuItemId + '" aria-label="ลดจำนวน">−</button>' +
                '<span class="qty mono">' + line.quantity + "</span>" +
                '<button type="button" class="qty-btn" data-qty="1" data-menu-id="' + menuItemId + '" aria-label="เพิ่มจำนวน">+</button>' +
                "</span>" +
                '<span class="line-total mono">' + money(line.item.price * line.quantity) + "</span>";
            el.lines.appendChild(li);
        });

        const hasItems = cart.size > 0;
        el.empty.hidden = hasItems;
        el.total.textContent = money(cartTotal());
        el.clear.disabled = !hasItems;
        el.payButtons.forEach(function (btn) { btn.disabled = !hasItems; });
    }

    function cartPayload(paymentMethod) {
        const items = [];
        cart.forEach(function (line, menuItemId) {
            items.push({ menuItemId: menuItemId, quantity: line.quantity });
        });

        return {
            items: items,
            paymentMethod: paymentMethod,
            note: el.note.value ? el.note.value.trim() : null
        };
    }

    function resetCart() {
        cart.clear();
        el.note.value = "";
        renderCart();
    }

    function setBusy(busy) {
        el.payButtons.forEach(function (btn) { btn.disabled = busy || cart.size === 0; });
    }

    function stampMessage(result) {
        let message = "ได้รับ " + result.earned + " แต้ม (รวม " +
            result.balance + "/" + result.stampsPerReward + ")";
        if (result.customerName) { message = result.customerName + ": " + message; }
        return message;
    }

    // ออกแต้มได้หลังรับเงินแล้วเท่านั้น และต้องผูกกับออเดอร์นั้นเสมอ
    // เลือกลูกค้าจากรายชื่อ (ช่วงคีย์มือ) หรือสแกน QR ถ้าลูกค้าผูก LINE แล้ว
    function offerStamps(order) {
        if (!window.CoffeeStampPicker) { return; }
        if (!order.eligibleStampCount || order.eligibleStampCount <= 0) { return; }

        const label = "ออเดอร์ " + order.orderNo + " · " + order.eligibleStampCount + " แต้ม";

        async function issue(url, body) {
            try {
                toast(stampMessage(await api.post(url, body)), "success");
            } catch (err) {
                toast(err.message, "error");
            }
        }

        window.CoffeeStampPicker.open({
            label: label,
            onCustomer: function (customerId) {
                issue("/api/orders/" + order.id + "/stamps-by-customer",
                    { customerId: customerId });
            },
            onQrToken: function (token) {
                issue("/api/orders/" + order.id + "/stamps", { qrToken: token });
            }
        });
    }

    async function pay(paymentMethod) {
        if (cart.size === 0) { return; }
        setBusy(true);

        try {
            const order = await api.post("/api/orders", cartPayload(paymentMethod));
            pendingOrder = order;

            if (paymentMethod === "Cash") {
                toast("รับเงินสด " + money(order.total) + " ฿ · ออเดอร์ " + order.orderNo, "success");
                resetCart();
                offerStamps(order);
                return;
            }

            if (paymentMethod === "PromptPay") {
                el.qrAmount.textContent = money(order.total);
                el.qrOrderNo.textContent = order.orderNo;
                el.qrImage.src = "/api/orders/" + order.id + "/promptpay-qr?t=" + Date.now();
                el.qrModal.hidden = false;
            } else {
                el.paoAmount.textContent = money(order.total);
                el.paoOrderNo.textContent = order.orderNo;
                el.paoModal.hidden = false;
            }
        } catch (err) {
            toast(err.message, "error");
        } finally {
            setBusy(false);
        }
    }

    async function confirmPayment() {
        if (!pendingOrder) { return; }
        const order = pendingOrder;

        try {
            await api.post("/api/orders/" + order.id + "/confirm-payment");
            toast("รับเงินแล้ว " + money(order.total) + " ฿ · ออเดอร์ " + order.orderNo, "success");
            closeModals();
            resetCart();
            offerStamps(order);
        } catch (err) {
            toast(err.message, "error");
        }
    }

    async function cancelPendingOrder() {
        if (!pendingOrder) { return; }

        try {
            await api.post("/api/orders/" + pendingOrder.id + "/cancel", { reason: "ลูกค้ายกเลิกก่อนชำระ" });
            toast("ยกเลิกออเดอร์ " + pendingOrder.orderNo + " แล้ว");
            closeModals();
        } catch (err) {
            toast(err.message, "error");
        }
    }

    function closeModals() {
        el.qrModal.hidden = true;
        el.paoModal.hidden = true;
        el.qrImage.removeAttribute("src");
        pendingOrder = null;
    }

    function escapeHtml(text) {
        const div = document.createElement("div");
        div.textContent = text == null ? "" : String(text);
        return div.innerHTML;
    }

    el.menuRoot.addEventListener("click", function (event) {
        const button = event.target.closest("[data-menu-id]");
        if (button) { addToCart(Number(button.dataset.menuId)); }
    });

    el.lines.addEventListener("click", function (event) {
        const button = event.target.closest("[data-qty]");
        if (button) { changeQuantity(Number(button.dataset.menuId), Number(button.dataset.qty)); }
    });

    el.payButtons.forEach(function (btn) {
        btn.addEventListener("click", function () { pay(btn.dataset.pay); });
    });

    el.clear.addEventListener("click", resetCart);
    el.qrConfirm.addEventListener("click", confirmPayment);
    el.paoConfirm.addEventListener("click", confirmPayment);
    el.qrCancel.addEventListener("click", cancelPendingOrder);
    el.paoCancel.addEventListener("click", cancelPendingOrder);

    renderCart();
    loadMenu();
})();
