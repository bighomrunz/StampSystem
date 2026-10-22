// หน้าล็อกอิน: เลือกชื่อพนักงาน แล้วกรอก PIN บนแป้นตัวเลขขนาดใหญ่
(function () {
    "use strict";

    const api = window.CoffeeStamp.api;
    const toast = window.CoffeeStamp.toast;

    const stepStaff = document.getElementById("step-staff");
    const stepPin = document.getElementById("step-pin");
    const pinDisplay = document.getElementById("pin-display");
    const pinStaffName = document.getElementById("pin-staff-name");

    let staffId = null;
    let pin = "";
    let submitting = false;

    function renderPin() {
        pinDisplay.textContent = "●".repeat(pin.length);
    }

    function reset() {
        pin = "";
        renderPin();
    }

    function showStaffStep() {
        staffId = null;
        reset();
        stepPin.hidden = true;
        stepStaff.hidden = false;
    }

    async function submit() {
        if (submitting || staffId === null) { return; }

        if (pin.length < 4) {
            toast("PIN ต้องมีอย่างน้อย 4 หลัก", "error");
            return;
        }

        submitting = true;
        try {
            const result = await api.post("/api/staff/login", { staffId: staffId, pin: pin });
            window.location.href = result.redirectTo || "/";
        } catch (err) {
            toast(err.message, "error");
            reset();
        } finally {
            submitting = false;
        }
    }

    stepStaff.addEventListener("click", function (event) {
        const button = event.target.closest("[data-staff-id]");
        if (!button) { return; }

        staffId = Number(button.dataset.staffId);
        pinStaffName.textContent = button.textContent.trim();
        reset();
        stepStaff.hidden = true;
        stepPin.hidden = false;
    });

    stepPin.addEventListener("click", function (event) {
        const button = event.target.closest("button");
        if (!button) { return; }

        if (button.dataset.digit !== undefined) {
            if (pin.length < 6) {
                pin += button.dataset.digit;
                renderPin();
            }
            return;
        }

        const action = button.dataset.action;
        if (action === "back") {
            pin = pin.slice(0, -1);
            renderPin();
        } else if (action === "submit") {
            submit();
        } else if (action === "change-staff") {
            showStaffStep();
        }
    });

    // รองรับการพิมพ์จากคีย์บอร์ดภายนอกด้วย บางร้านต่อคีย์บอร์ดกับแท็บเล็ต
    document.addEventListener("keydown", function (event) {
        if (stepPin.hidden) { return; }

        if (event.key >= "0" && event.key <= "9") {
            if (pin.length < 6) {
                pin += event.key;
                renderPin();
            }
        } else if (event.key === "Backspace") {
            pin = pin.slice(0, -1);
            renderPin();
        } else if (event.key === "Enter") {
            submit();
        }
    });

    renderPin();
})();
