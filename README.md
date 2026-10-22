# Coffee Stamp — ระบบสะสมแต้มร้านกาแฟ

ระบบสะสมแต้ม "ครบ N แก้ว ฟรี 1 แก้ว" สำหรับร้านกาแฟ **ร้านเดียว สาขาเดียว** ใช้แทนใบคะแนนกระดาษและแทน LINE OA reward card ซึ่งเลิกใช้ไปเพราะโดนโกง (ลูกค้าแคป QR ของร้านไปสแกนซ้ำหรือส่งต่อ)

## หลักการออกแบบ (ห้ามละเมิด)
1. **ร้านเป็นฝ่ายออกแต้มเสมอ** พนักงานสแกน QR ของลูกค้า ไม่มีกรณีที่ลูกค้าสแกน QR ของร้านแล้วได้แต้ม
2. **ทุกแต้มต้องผูกกับ Order จริง** ที่มียอดเงินและช่องทางชำระ ไม่มี endpoint สำหรับออกแต้มลอย ๆ
3. **Ledger เป็นแบบ append-only** ห้าม UPDATE หรือ DELETE รายการแต้ม การแก้ไขทำโดยเพิ่มรายการ Void หรือ Adjust เท่านั้น
4. **ทุก action มีผู้รับผิดชอบ** บันทึก StaffId, เวลา และอุปกรณ์ทุกครั้ง
5. **Logic สำคัญทั้งหมดอยู่ฝั่งเซิร์ฟเวอร์** ได้แก่ การคำนวณแต้ม, การแลก, การตรวจ token ฝั่ง client แค่แสดงผล

## Tech stack
- ASP.NET Core 8, C#
- PostgreSQL + Entity Framework Core (Npgsql provider) + Migrations
- โปรเจกต์เดียว:
  - Web API
  - Razor Pages สำหรับแอปพนักงานและแอดมิน ทำเป็น PWA ใช้บนแท็บเล็ตหรือมือถือ
  - หน้า LIFF สำหรับลูกค้า
- LINE: LIFF (LINE Login) + Messaging API (push แจ้งแต้ม)
- สแกน QR ฝั่งพนักงาน: html5-qrcode
- เงิน: `decimal(10,2)` เท่านั้น
- เวลา: เก็บเป็น UTC, แสดงผลเป็น Asia/Bangkok
- วันทำการ (business date) คำนวณตามเวลาไทย

## บริบทร้าน
- ไม่มี POS แอปพนักงานของระบบนี้ทำหน้าที่เป็น mini-POS
- ช่องทางชำระ: `Cash`, `PromptPay`, `PaoTang` (เป๋าตัง/ไทยช่วยไทย ผ่านแอปถุงเงิน)
- ไม่มี API ของเป๋าตังให้เชื่อม ใช้การกระทบยอดแบบกรอกมือตอนปิดร้าน

## กฎทางธุรกิจ (ทุกค่าตั้งได้ใน ShopSettings)
| Setting | Default | หมายเหตุ |
|---|---|---|
| `StampsPerReward` | 10 | ครบกี่แต้มแลก 1 แก้ว |
| `StampExpiryMonths` | 12 | 0 = ไม่หมดอายุ; การเปลี่ยนค่ามีผลกับแต้มที่ออกใหม่เท่านั้น |
| `RewardMode` | `AllItems` | `AllItems` / `SelectedItems` / `PriceCap` |
| `RewardPriceCap` | null | ใช้เมื่อ `RewardMode = PriceCap` |
| `AllowRewardTopUp` | false | ถ้าเมนูเกินเพดาน ลูกค้าจ่ายส่วนต่างได้ไหม |
| `MaxStampsPerOrder` | 6 | เกินต้องใช้ PIN ผู้จัดการ |
| `MaxStampsPerDayPerCustomer` | 6 | เกินต้องใช้ PIN ผู้จัดการ |

- แต้มคำนวณจาก `OrderItem.Quantity` ของเมนูที่ `EarnsStamp = true`
- แก้วที่แลกฟรีไม่ได้แต้ม
- เมนูที่แลกได้ในโหมด `SelectedItems` กำหนดผ่าน `MenuItem.IsRewardEligible`
- **การหมดอายุแบบ FIFO** แต่ละรายการ Earn มี `ExpiresAt` ของตัวเอง การแลกจะตัดแต้มที่ใกล้หมดอายุก่อน (บันทึกใน `StampAllocation`) และมี job รายวันเขียนรายการ `Expire` สำหรับแต้มที่หมดอายุแต่ยังไม่ถูกใช้

## Flow หลัก

### 1. ขาย + ออกแต้ม
1. พนักงานเลือกเมนูและจำนวน ระบบรวมยอด
2. เลือกช่องทางชำระ
   - ถ้าเป็น `PromptPay`: ระบบสร้าง QR พร้อมเพย์แบบระบุยอด (EMVCo / Thai QR Payment, ใส่ยอดใน tag 54, ตรวจด้วย CRC16-CCITT) จากเลขพร้อมเพย์ใน settings
   - พนักงานกดยืนยันว่ารับเงินแล้ว
3. (ถ้าลูกค้าเป็นสมาชิก) สแกน QR ลูกค้า ระบบออกแต้มเข้าออเดอร์นั้น
   - 1 ออเดอร์ออกแต้มได้ครั้งเดียว: ตั้ง unique index ที่ `StampTransaction(OrderId, Type=Earn)`
   - สแกนภายหลังได้ แต่ต้องอยู่ในวันทำการเดียวกันและออเดอร์ยังไม่ถูกปิดยอด
4. Push ข้อความ LINE: "ได้รับ X แต้ม (รวม Y/10)"

### 2. แลกรางวัล
1. สแกน QR ลูกค้า ระบบแสดงยอดแต้มและเมนูที่แลกได้ตาม `RewardMode`
2. สร้างออเดอร์ที่มีบรรทัดรางวัลราคา 0 (หรือส่วนต่างถ้า `AllowRewardTopUp`) แล้วสร้าง `Redemption`
3. ทำใน DB transaction เดียว และใช้ concurrency token บน `CustomerBalance` เพื่อกันการแลกซ้อน

### 3. ยกเลิกออเดอร์
- ต้องใช้ PIN ผู้จัดการ และต้องระบุเหตุผล
- เขียนรายการ `Void` ย้อนแต้มของออเดอร์นั้น ถ้าแต้มถูกใช้แลกไปแล้ว ให้ block และแจ้งผู้จัดการ

### 4. ปิดยอดรายวัน (`DailyClose`)
- ระบบแสดงยอดที่คาดไว้แยกตามช่องทาง
- ผู้จัดการกรอกยอดจริง: เงินสดที่นับได้, เงินเข้าพร้อมเพย์จากแอปธนาคาร, ยอดจากแอปถุงเงิน
- บันทึกส่วนต่าง พร้อมลิงก์ไปออเดอร์และพนักงานที่เกี่ยวข้อง
- หลังปิดยอดแล้ว ออเดอร์ของวันนั้นแก้ไขไม่ได้

### 5. ลูกค้าสมัครสมาชิก
- เปิด LIFF แล้วล็อกอินผ่าน LINE
- เซิร์ฟเวอร์ verify ID token กับ LINE ทุกครั้ง ห้ามเชื่อ userId ที่ส่งมาจาก client
- ขอความยินยอม PDPA: เก็บ `ConsentVersion` และ `ConsentAt`
- หน้าบัตรแสดง: แต้มคงเหลือ, แต้มที่ใกล้หมดอายุ, ประวัติ, QR

## ความปลอดภัย
- **QR ลูกค้าแบบหมุนเวียน**
  - token = `CustomerPublicId` + `timeStep` (30 วินาที) + `HMAC-SHA256(secret)`
  - ยอมรับ ±1 step
  - token เดียวกันใช้สำเร็จได้ครั้งเดียว: เก็บ step ล่าสุดที่ใช้ต่อลูกค้า
  - QR สร้างใหม่ใน LIFF จาก endpoint ที่ต้องยืนยันตัวตน
- **พนักงาน**
  - ล็อกอินด้วย PIN ส่วนตัว เก็บแบบ hash ด้วย ASP.NET Identity PasswordHasher
  - Role: `Staff`, `Manager`, `Owner`
  - ล็อกเอาต์อัตโนมัติเมื่อ idle
  - ใช้ได้เฉพาะอุปกรณ์ที่ลงทะเบียนแล้ว
- **AuditLog** ทุก action ที่เกี่ยวกับเงิน แต้ม การตั้งค่า และการ override
- **Anomaly report**
  - พนักงานคนเดียวออกแต้มให้ลูกค้าคนเดิมถี่ผิดปกติ
  - มีรายการนอกเวลาเปิดร้าน
  - Void บ่อย
  - ส่วนต่างตอนปิดยอดเกินเกณฑ์
- **ทั่วไป:** HTTPS เท่านั้น, rate limit ที่ endpoint สแกน, secret เก็บใน user-secrets หรือ environment variable ห้าม commit

## Entities (ร่าง)
- `ShopSettings` (singleton)
- `Staff`(Id, Name, PinHash, Role, IsActive)
- `Device`(Id, Name, RegisteredBy, IsActive)
- `Customer`(Id, PublicId, LineUserId [unique], DisplayName, ConsentVersion, ConsentAt, CreatedAt)
- `CustomerBalance`(CustomerId, Stamps, RowVersion)
- `MenuItem`(Id, Name, Price, EarnsStamp, IsRewardEligible, IsActive)
- `Order`(Id, BusinessDate, StaffId, DeviceId, Total, PaymentMethod, Status[Paid/Voided], CustomerId?, CreatedAt, VoidedBy?, VoidReason?)
- `OrderItem`(Id, OrderId, MenuItemId, NameSnapshot, UnitPrice, Quantity, IsReward)
- `StampTransaction`(Id, CustomerId, Type[Earn/Redeem/Void/Expire/Adjust], Quantity(+/-), OrderId?, RedemptionId?, ExpiresAt?, StaffId, CreatedAt)
- `StampAllocation`(Id, ConsumingTxId, EarnTxId, Quantity)
- `Redemption`(Id, CustomerId, OrderId, StampsUsed, StaffId, CreatedAt)
- `DailyClose`(BusinessDate [PK], Expected/Actual ต่อช่องทาง, Variance, ClosedBy, ClosedAt, Note)
- `AuditLog`(Id, StaffId?, Action, EntityType, EntityId, DataJson, CreatedAt)

## API (ร่าง)
- `POST /api/staff/login`
- `POST /api/staff/logout`
- `GET /api/menu`
- `POST /api/orders`
- `POST /api/orders/{id}/void`
- `GET /api/orders/{id}/promptpay-qr`
- `POST /api/orders/{id}/stamps` (body: customer QR token)
- `POST /api/customers/resolve` (QR token → ข้อมูลลูกค้าและสิทธิ์แลก)
- `POST /api/redemptions`
- `GET /api/daily-close/{date}`
- `POST /api/daily-close/{date}`
- `POST /api/liff/auth` (ID token → session)
- `GET /api/liff/me`
- `GET /api/liff/qr-token`
- `GET/PUT /api/admin/settings`
- `CRUD /api/admin/menu`
- `CRUD /api/admin/staff`
- `GET /api/admin/reports/...`

## ลำดับการพัฒนา
1. **Phase 1 – mini-POS:** settings, staff/PIN, เมนู, ออเดอร์, QR พร้อมเพย์, void, ปิดยอดรายวัน, audit log
2. **Phase 2 – สมาชิก:** LIFF login, PDPA, QR หมุนเวียน, ออกแต้มผูกออเดอร์, push แจ้งเตือน
3. **Phase 3 – รางวัล:** แลกแต้มทั้ง 3 โหมด, FIFO expiry + job รายวัน, เพดานแต้มพร้อม manager override
4. **Phase 4 – รายงาน:** ยอดขายรายเมนูและรายชั่วโมง, anomaly report, การย้ายข้อมูลจากใบคะแนนเดิม (Adjust พร้อมรูปถ่ายและการอนุมัติของ Owner)

## Conventions
- ไม่ hard delete: Order, StampTransaction, AuditLog, Customer (ใช้ soft delete หรือ status)
- Service layer แยกจาก controller
- Logic แต้มอยู่ใน `StampService` ที่เดียว และต้องมี unit test ครอบคลุม FIFO, expiry, double-issue, concurrent redeem
- QR พร้อมเพย์ต้องมี unit test ตรวจ CRC กับตัวอย่างที่ทราบค่า
- UI ภาษาไทย ปุ่มใหญ่ ใช้ง่ายบนแท็บเล็ตด้วยนิ้ว

## คำถามที่ยังเปิดอยู่ (ถามเจ้าของก่อน implement ส่วนที่เกี่ยวข้อง)
- ~~"ราคาไม่เกินแก้วแรก" ในโหมด `PriceCap` หมายถึงเพดานคงที่ที่ตั้งเอง หรืออ้างอิงราคาเมนูที่ลูกค้าซื้อบ่อยหรือแก้วมาตรฐาน?~~
  **ตอบแล้ว 22 ต.ค. 2026: เพดานคงที่ที่เจ้าของตั้งเอง** โดยดูจากราคาเมนูในร้านแล้วตัดสินใจเชิงการตลาด
  (ดู [docs/PHASE3.md](docs/PHASE3.md))
- ~~ลูกค้าที่ไม่ใช้ LINE ต้องรองรับไหม (เช่น เบอร์โทร + OTP)?~~
  **ตอบแล้ว 16 ต.ค. 2026: ไม่ทำ OTP แต่รองรับผ่านโหมดคีย์มือ**
  พนักงานสร้างสมาชิกให้ที่เคาน์เตอร์ด้วยชื่อ + เบอร์โทร แล้วค่อยผูกบัญชี LINE ทีหลังได้
  (ดู [docs/PHASE2.5-MANUAL-MODE.md](docs/PHASE2.5-MANUAL-MODE.md)
  และ [docs/MIGRATION-PAPER-CARDS.md](docs/MIGRATION-PAPER-CARDS.md))
- จำนวนพนักงาน และใครบ้างที่มีสิทธิ์ Manager
- ที่ host และโดเมน (LIFF ต้องใช้ HTTPS)
