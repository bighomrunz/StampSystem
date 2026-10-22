# Phase 1 — mini-POS (เสร็จแล้ว)

เอกสารนี้สรุปสิ่งที่ลงมือทำใน Phase 1 ตาม README รวมถึงจุดที่ตัดสินใจต่างจากร่างเดิมและเหตุผล

## ขอบเขตที่ทำเสร็จ

| หัวข้อใน README | สถานะ | อยู่ที่ไหน |
|---|---|---|
| ShopSettings (singleton) | เสร็จ | `Domain/ShopSettings.cs`, `Pages/Admin/Settings.cshtml` |
| Staff + PIN + role | เสร็จ | `Services/StaffAuthService.cs`, `Services/StaffAdminService.cs` |
| เมนู | เสร็จ | `Services/MenuService.cs`, `Pages/Admin/Menu.cshtml` |
| ออเดอร์ | เสร็จ | `Services/OrderService.cs`, `Pages/Index.cshtml` (หน้าขาย) |
| QR พร้อมเพย์ระบุยอด | เสร็จ | `Services/PromptPayPayload.cs`, `Services/PromptPayQrService.cs` |
| ยกเลิกออเดอร์ (void) | เสร็จ | `OrderService.VoidAsync`, `Pages/Orders.cshtml` |
| ปิดยอดรายวัน | เสร็จ | `Services/DailyCloseService.cs`, `Pages/DailyClose.cshtml` |
| Audit log | เสร็จ | `Services/AuditService.cs` (ทุก action ที่แตะเงิน/ตั้งค่า) |
| อุปกรณ์ที่ลงทะเบียน | เสร็จ | `Services/DeviceService.cs`, `Pages/Device/Register.cshtml` |
| PWA | เสร็จ | `wwwroot/manifest.webmanifest`, `wwwroot/sw.js` |

Phase 2-4 (สมาชิก LIFF, แต้ม, รางวัล, รายงาน) ยังไม่ได้เริ่ม ตาราง `customers`,
`stamp_transactions`, `stamp_allocations`, `redemptions` จะเพิ่มด้วย migration ของ Phase 2

## การตัดสินใจที่ต่างจาก README

1. **.NET 10 ไม่ใช่ ASP.NET Core 8** — เครื่องนี้มี SDK 10.0.401 และ runtime 10 เท่านั้น
   ไม่มี runtime 8 ให้รัน จึง target `net10.0`
2. **PostgreSQL + Npgsql ไม่ใช่ MySQL + Pomelo** — เปลี่ยนตามที่เจ้าของโปรเจกต์สั่งเมื่อ 16 ต.ค. 2026
   (README อัปเดตแล้ว) ผลที่ตามมาในโค้ด:
   - คอลัมน์เงินเป็น `numeric(10,2)` แทน `decimal(10,2)` (ความหมายเดียวกัน)
   - `audit_logs.data_json` เป็น `jsonb`
   - ตรวจการชน unique index ด้วย SQLSTATE `23505` แทน error 1062 ของ MySQL
   - ชื่อตารางและคอลัมน์เป็น snake_case ทั้งหมดผ่าน `UseSnakeCaseNamingConvention()`
     เพื่อให้เขียน SQL ตรวจยอดด้วยมือได้ไม่ต้องใส่เครื่องหมายคำพูด
   - `DateTime` ถูกเก็บเป็น `timestamp with time zone` ทุกค่าต้องเป็น UTC (Npgsql บังคับ)
3. **เพิ่มสถานะ `Pending` และ `Cancelled` ในออเดอร์** — README ระบุแค่ `Paid`/`Voided`
   แต่ flow พร้อมเพย์ต้องสร้างออเดอร์ก่อนเพื่อออก QR ตาม endpoint
   `GET /api/orders/{id}/promptpay-qr` แล้วค่อยให้พนักงานกดยืนยันว่าเงินเข้า
   - เงินสด: สร้างเป็น `Paid` ทันที
   - พร้อมเพย์/เป๋าตัง: สร้างเป็น `Pending` → กด "รับเงินแล้ว" → `Paid`
   - `Cancelled` = ยกเลิกตอนยังไม่รับเงิน ไม่ต้องใช้ PIN ผู้จัดการ ไม่นับยอดขาย
   - `Voided` = ยกเลิกหลังรับเงิน ต้องใช้ PIN ผู้จัดการ + เหตุผล ตาม README
   - ปิดยอดไม่ได้ถ้ายังมีออเดอร์ `Pending` ค้าง (กันยอดหาย)
4. **เลขออเดอร์** เพิ่มคอลัมน์ `OrderNo` รูปแบบ `yyyyMMdd-0001` (unique) เพื่อให้พนักงาน
   อ่านและกระทบยอดได้ ส่วน `Id` ใช้เป็น key ภายใน
5. **`EarnsStampSnapshot` ใน OrderItem** — เก็บค่า `EarnsStamp` ของเมนูตอนขาย
   ถ้าแอดมินแก้เมนูทีหลัง แต้มของออเดอร์เก่าต้องไม่เปลี่ยน
6. **ชื่อผู้รับเงินใน QR (tag 59)** ใส่ได้เฉพาะอักขระ ASCII เพราะ tag นี้นับความยาวเป็นไบต์
   ชื่อไทยจะทำให้ความยาวไม่ตรงและ QR เสีย ระบบจะข้าม tag นี้ไปถ้าชื่อเป็นภาษาไทย

## เรื่องความปลอดภัยที่ทำไว้แล้วใน Phase 1

- PIN เก็บเป็น hash ด้วย `PasswordHasher<Staff>` ของ ASP.NET Identity
- PIN ผิด 5 ครั้งล็อก 5 นาที และมี rate limit 10 ครั้ง/นาที/IP ที่ endpoint ล็อกอิน
- นโยบาย PIN: 4-6 หลัก ห้ามเลขซ้ำทั้งหมด ห้ามเลขเรียง
- บัญชีที่ถูก seed หรือรีเซ็ต PIN ถูกบังคับเปลี่ยน PIN ก่อนใช้งานอย่างอื่น (middleware)
- ล็อกอินได้เฉพาะอุปกรณ์ที่ลงทะเบียน (cookie `cs_device` + ตาราง `devices`)
  ปิดใช้งานอุปกรณ์แล้วเซสชันถูกตัดทันทีผ่าน `OnValidatePrincipal`
- cookie ทุกตัว `HttpOnly` + `SameSite=Strict` + `Secure`, บังคับ HTTPS, เปิด HSTS นอก Development
- ทุก request ที่เปลี่ยนข้อมูลผ่าน API ต้องมี antiforgery token (`X-CSRF-TOKEN`)
- ล็อกเอาต์อัตโนมัติเมื่อ idle (ค่าตั้งใน ShopSettings, ตรวจทั้งฝั่ง cookie และฝั่งหน้าจอ)
- logic เงินและแต้มอยู่ฝั่งเซิร์ฟเวอร์ทั้งหมด ฝั่งหน้าจอคำนวณไว้แค่ให้พนักงานเห็นระหว่างกด

## ผลการทดสอบกับ PostgreSQL จริง (16 ต.ค. 2026)

รันแอปต่อ PostgreSQL 18.1 แล้วไล่ flow จริงทั้งหมดผ่าน HTTP ผลที่ยืนยันแล้ว

- ล็อกอิน PIN ชั่วคราว → ถูกบังคับเปลี่ยน PIN (ขอหน้าอื่นถูก redirect มาที่ `/Staff/ChangePin`)
- ขายเงินสดได้สถานะ `Paid` ทันที, ขายพร้อมเพย์และเป๋าตังได้ `Pending`
- QR พร้อมเพย์ของออเดอร์: tag 01 = 12 (QR ระบุยอด), tag 54 = ยอดจริง, CRC ตรงกับที่คำนวณด้วย
  implementation อีกตัวที่เขียนแยกกัน, คืน PNG ได้
- ยกเลิกออเดอร์ที่จ่ายแล้ว: PIN ผู้จัดการผิด → ปฏิเสธ, ถูก → `Voided` พร้อมบันทึกผู้อนุมัติ
- ปิดยอดขณะมีออเดอร์ค้าง → ปฏิเสธ; ปิดยอดสำเร็จคิดส่วนต่างถูก (เงินสดขาด 5 → `-5.00`)
- หลังปิดยอด: ขายของวันนั้นไม่ได้ และปิดยอดซ้ำไม่ได้
- PIN ผิดครบ 5 ครั้ง → ล็อก 300 วินาที, ผู้จัดการปลดล็อกได้
- antiforgery token ผิด → ปฏิเสธ; ออกจากระบบแล้วเรียก API → 401
- พนักงานระดับ `Staff`: เข้าหน้าขายและหน้าออเดอร์ได้ แต่หน้าแอดมินถูก redirect
  และ API แอดมิน/ปิดยอดได้ 403
- `audit_logs` บันทึกครบทุก action รวมถึงครั้งที่กรอก PIN ผู้จัดการผิด
- ข้อความไทยอ่านเขียนผ่าน API ได้ถูกต้อง (ทดสอบด้วย body UTF-8)

### บั๊กที่เจอจากการรันจริงและแก้แล้ว

1. **enum ที่ส่งเป็นสตริงถูกปฏิเสธ** — หน้าจอส่ง `"paymentMethod":"Cash"` แต่ System.Text.Json
   ไม่รับ ต้องเพิ่ม `JsonStringEnumConverter` ใน `ConfigureHttpJsonOptions`
   (ถ้าไม่แก้ หน้าขายกดสั่งไม่ได้เลย)
2. **`EnableRetryOnFailure` ชนกับ transaction ที่เปิดเอง** — การเพิ่มเมนูและเพิ่มพนักงานพัง
   ต้องเรียกผ่าน `Database.CreateExecutionStrategy()`
3. **API ตอบ 302 ไปหน้าล็อกอินเมื่อสิทธิ์ไม่ถึง** — เพิ่ม `OnRedirectToAccessDenied`
   ให้ path `/api` ตอบ 403
4. **บัญชีพนักงานที่ผู้จัดการสร้างไม่ถูกบังคับเปลี่ยน PIN** — ตั้ง `MustChangePin = true` ตอนสร้าง
5. **connection string ค่าว่างไม่ถูกตรวจ** — แอปสตาร์ตแล้วพังตอน query ภายหลัง
   เปลี่ยนไปตรวจด้วย `IsNullOrWhiteSpace` ตอนบูต

## ยังไม่ได้ทำ (รอ Phase ถัดไปหรือรอคำตอบจากเจ้าของร้าน)

- การย้อนแต้มเมื่อ void ออเดอร์ที่ออกแต้มไปแล้ว — มี TODO ไว้ใน `OrderService.VoidAsync`
- Anomaly report (README หัวข้อความปลอดภัย) อยู่ใน Phase 4
  ข้อมูลที่ต้องใช้ถูกเก็บครบแล้วใน `orders` และ `audit_logs`
- คำถามที่ยังเปิดอยู่ท้าย README ทั้ง 4 ข้อ ยังไม่มีผลกับ Phase 1
  แต่ข้อ "ที่ host และโดเมน" ต้องตอบก่อนเริ่ม Phase 2 เพราะ LIFF ต้องใช้ HTTPS โดเมนจริง

## วิธีรัน

ดู [docs/SETUP.md](SETUP.md)
