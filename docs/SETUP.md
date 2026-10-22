# ติดตั้งและรัน

## สิ่งที่ต้องมี

- .NET SDK 10 — เครื่องนี้ติดตั้งไว้ที่ `%LOCALAPPDATA%\Microsoft\dotnet` และ **ไม่อยู่ใน PATH**
  สคริปต์ `start-dev.ps1` set ให้เองแล้ว ถ้าสั่ง `dotnet` เองต้อง set ก่อน:

  ```powershell
  $env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
  $env:Path = "$env:DOTNET_ROOT;$env:Path"
  ```

- PostgreSQL — **เครื่องนี้มีสองตัว** ต้องรู้ว่ากำลังคุยกับตัวไหน

  | ตัว | พอร์ต | สถานะ |
  |---|---|---|
  | service `postgresql-x64-18` (`C:\Program Files\PostgreSQL\18`) | 5432 | เจ้าของเครื่องติดตั้งเอง **ยังเข้าไม่ได้** เพราะรหัส `postgres` ที่จำไว้ใช้ไม่ผ่าน |
  | portable (`C:\Users\bigho\pgsql`) | **5433** | **ตัวที่โปรเจกต์ใช้อยู่จริง** มีข้อมูลและ schema ครบ |

  `ConnectionStrings:Default` ใน user-secrets ชี้ไปที่ **พอร์ต 5433** แล้ว
  ถ้าวันหลังเข้า service ได้ ให้ย้ายข้อมูลตามหัวข้อ "ย้ายไปใช้ service" ท้ายไฟล์นี้

- PostgreSQL 18.1 แบบ zip (ตัวที่ใช้อยู่) — ไม่ได้เป็น Windows service ต้องสตาร์ตเอง

  | สิ่งที่เก็บ | ที่อยู่ |
  |---|---|
  | binaries | `C:\Users\bigho\pgsql\bin` |
  | data directory | `C:\Users\bigho\pgdata` |
  | log ของเซิร์ฟเวอร์ | `C:\Users\bigho\pgdata\server.log` |
  | รหัสผ่าน superuser (`postgres`) | `C:\Users\bigho\pgdata-superuser-password.txt` |
  | รหัสผ่าน user แอป (`coffeestamp`) | `C:\Users\bigho\coffeestamp-db-password.txt` |

  สตาร์ตและหยุดเซิร์ฟเวอร์ (ต้องระบุพอร์ต 5433 เพราะ 5432 ถูก service ยึดไว้):

  ```powershell
  & "$env:USERPROFILE\pgsql\bin\pg_ctl.exe" -D "$env:USERPROFILE\pgdata" -l "$env:USERPROFILE\pgdata\server.log" -o "-p 5433" start
  & "$env:USERPROFILE\pgsql\bin\pg_ctl.exe" -D "$env:USERPROFILE\pgdata" status
  & "$env:USERPROFILE\pgsql\bin\pg_ctl.exe" -D "$env:USERPROFILE\pgdata" stop
  ```

  ต่อด้วย psql: `psql -U coffeestamp -h 127.0.0.1 -p 5433 -d coffeestamp`

  `pg_hba.conf` ตั้งให้ connection ผ่าน TCP (127.0.0.1 และ ::1) ต้องใช้รหัสผ่านแบบ scram-sha-256
  **ข้อควรระวัง:** ถ้าแก้ไฟล์ `.conf` ด้วย `Set-Content -Encoding utf8` ของ PowerShell 5.1
  จะได้ BOM ติดมาและ PostgreSQL จะสตาร์ตไม่ขึ้น ให้ใช้ `[System.IO.File]::WriteAllLines` แทน

  ทางเลือกอื่นถ้าจะย้ายไปแบบติดตั้งจริง: `winget install PostgreSQL.PostgreSQL.18` (ต้องยอม UAC),
  หรือ PostgreSQL ใน WSL หรือ Docker

## ตั้งค่า connection string

ห้าม commit รหัสผ่าน ใช้ user-secrets:

```powershell
cd src/CoffeeStamp.Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=coffeestamp;Username=coffeestamp;Password=รหัสผ่านจริง"
```

ถ้าต้องการกำหนด PIN ของบัญชีเจ้าของร้านตัวแรกเอง (ไม่ตั้งก็ได้ ระบบจะสุ่มแล้ว log ให้ครั้งเดียว):

```powershell
dotnet user-secrets set "Bootstrap:OwnerPin" "907213"
```

> ฐานข้อมูล `coffeestamp`, user `coffeestamp` และ schema ของ Phase 1 ถูกสร้างไว้แล้ว
> และ `ConnectionStrings:Default` ถูกตั้งใน user-secrets ของโปรเจกต์แล้ว
> หัวข้อถัดไปเก็บไว้สำหรับติดตั้งเครื่องใหม่

## ตั้งค่า secret ของ QR ลูกค้า (Phase 2)

QR ของลูกค้าเซ็นด้วย HMAC-SHA256 ต้องมี secret ที่คงที่ ไม่งั้น QR ที่ลูกค้าเปิดค้างไว้
จะใช้ไม่ได้ทุกครั้งที่รีสตาร์ต (ตั้งไว้แล้วบนเครื่องนี้)

```powershell
dotnet user-secrets set "Qr:Secret" "<base64 อย่างน้อย 32 ไบต์>"
```

ถ้าไม่ตั้งค่านี้บน production แอปจะไม่บูต ส่วนใน Development จะสุ่มให้ชั่วคราวและ log เตือน

## ตั้งค่า LINE (Phase 2)

ต้องมี 2 channel ใน LINE Developers Console: **LINE Login** (สำหรับ LIFF) และ **Messaging API**
(สำหรับ push แจ้งแต้ม) จากนั้น

```powershell
dotnet user-secrets set "Line:LiffId" "1234567890-abcdefgh"
dotnet user-secrets set "Line:LoginChannelId" "1234567890"
dotnet user-secrets set "Line:MessagingChannelAccessToken" "<channel access token>"
```

- LIFF endpoint URL ให้ตั้งเป็น `https://<โดเมนจริงของร้าน>/liff`
  (LIFF ต้องใช้ HTTPS โดเมนจริง ใช้ `localhost` ไม่ได้)
- `Line:LoginChannelId` ใช้เป็น `client_id` ตอน verify ID token ถ้าไม่ตรง ระบบจะปฏิเสธ token นั้น
- ถ้าไม่ตั้ง `MessagingChannelAccessToken` ระบบจะยังออกแต้มได้ปกติ แต่ไม่ push แจ้งลูกค้า
  (คำตอบของ API จะมี `notified: false`)

### โหมดทดสอบในเครื่องโดยไม่มี LINE

`appsettings.Development.json` ตั้ง `Line:AllowDevMockLogin: true` ไว้
ทำให้เปิด `https://localhost:7111/liff` แล้วล็อกอินด้วยรหัสลูกค้าสมมติได้
โดยไม่ต้องมี LIFF จริง

**ค่านี้เปิดได้เฉพาะ Development เท่านั้น** ถ้าเผลอเปิดบน environment อื่น แอปจะ throw
ตอนบูตทันที เพราะถ้าเปิดบนเครื่องจริงใครก็ปลอมเป็นสมาชิกคนไหนก็ได้

## สร้างฐานข้อมูล

ใน psql (ล็อกอินเป็น superuser):

```sql
CREATE DATABASE coffeestamp ENCODING 'UTF8';
CREATE USER coffeestamp WITH PASSWORD 'รหัสผ่านจริง';
GRANT ALL PRIVILEGES ON DATABASE coffeestamp TO coffeestamp;
\connect coffeestamp
GRANT ALL ON SCHEMA public TO coffeestamp;
```

แล้วสร้างตาราง:

```powershell
dotnet dotnet-ef database update --project src/CoffeeStamp.Web/CoffeeStamp.Web.csproj
```

ใน Development มี `Database:AutoMigrate = true` อยู่แล้ว แอปจะรัน migration ให้ตอนสตาร์ต
ส่วน production ตั้งเป็น `false` ไว้ ให้รัน migration ด้วยมือก่อน deploy

## รัน

```powershell
./start-dev.ps1
```

เปิด `https://localhost:7111`

### ครั้งแรกที่เปิด

1. ระบบสร้างบัญชี **เจ้าของร้าน** พร้อม PIN ชั่วคราว ดู PIN ได้ที่ console
   (บรรทัด `สร้างบัญชีเจ้าของร้าน ... พร้อม PIN ชั่วคราว`) หรือกำหนดเองผ่าน `Bootstrap:OwnerPin`
2. ล็อกอิน แล้วระบบจะบังคับเปลี่ยน PIN ทันที
3. ไป **ตั้งค่า → อุปกรณ์** สร้างอุปกรณ์ แล้วเอารหัส 6 หลักไปกรอกที่ `/Device/Register`
   บนแท็บเล็ตที่ใช้หน้าร้าน — **ต้องทำก่อนจึงจะขายได้** เพราะทุกออเดอร์ต้องบันทึกว่าทำจากเครื่องไหน
   (ก่อนลงทะเบียนอุปกรณ์เครื่องแรก ระบบอยู่ในโหมดติดตั้งและยอมให้ล็อกอินจากเครื่องไหนก็ได้)
4. ไป **ตั้งค่า** กรอกเลขพร้อมเพย์ของร้าน ไม่กรอกจะสร้าง QR ไม่ได้
5. ไป **ตั้งค่า → เมนู** ใส่เมนูจริงของร้าน
   (ใน Development มี `Bootstrap:SeedSampleMenu = true` ใส่เมนูตัวอย่างไว้ให้ลองก่อน)

### หน้าจอที่มี

| หน้า | ใคร | หมายเหตุ |
|---|---|---|
| `/` | พนักงาน | หน้าขาย (mini-POS) สแกน QR ลูกค้าหลังรับเงิน |
| `/Orders` | พนักงาน | ออเดอร์ของวัน ยืนยันรับเงิน ยกเลิก ออกแต้มย้อนหลังในวันเดียวกัน |
| `/DailyClose` | ผู้จัดการ | ปิดยอดรายวัน |
| `/Admin/Settings`, `/Admin/Menu`, `/Admin/Staff`, `/Admin/Devices` | ผู้จัดการ | ตั้งค่า |
| `/Device/Register` | พนักงาน | ลงทะเบียนอุปกรณ์ด้วยรหัส 6 หลัก |
| `/liff` | ลูกค้า | บัตรสะสมแต้ม + QR หมุนเวียน (ตั้งเป็น LIFF endpoint URL) |
| `/liff/privacy` | ลูกค้า | ข้อความ PDPA |

## รันเทส

```powershell
dotnet test
```

ครอบคลุม: CRC16 ของ QR พร้อมเพย์เทียบค่าที่ทราบ, โครงสร้าง EMVCo payload,
การแปลงเบอร์พร้อมเพย์, วันทำการตามเวลาไทย, การปัดเงิน, นโยบาย PIN, การนับแต้มของออเดอร์,
QR ลูกค้า (ลายเซ็น หน้าต่างเวลา การปลอม token), PDPA, วันหมดอายุแต้ม,
การ validate ID token ของ LINE

## คำสั่งที่ใช้บ่อย

```powershell
# เพิ่ม migration ใหม่
dotnet dotnet-ef migrations add ชื่อ --project src/CoffeeStamp.Web/CoffeeStamp.Web.csproj --output-dir Data/Migrations

# ดู SQL ที่ migration จะรัน โดยยังไม่แตะ DB
dotnet dotnet-ef migrations script --project src/CoffeeStamp.Web/CoffeeStamp.Web.csproj
```

## หมายเหตุเรื่องเวอร์ชันแพ็กเกจ

`Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 ผูกกับ EF Core 10.0.4 จึงตรึง
`Microsoft.EntityFrameworkCore.Design` ไว้ที่ 10.0.4 ด้วย ถ้าอัปเวอร์ชันใดต้องขยับให้ตรงกัน
ไม่งั้นจะเจอ warning MSB3277 เรื่องเวอร์ชัน assembly ชนกัน

## ย้ายไปใช้ service (ทำเมื่อเข้า postgres ของ service ได้)

ตอนนี้โปรเจกต์รันบน PostgreSQL แบบ portable ที่พอร์ต 5433 เพราะเข้า service ที่พอร์ต 5432 ไม่ได้
(รหัส `postgres` ที่จำไว้ใช้ไม่ผ่าน ลองแล้วทั้งแบบ env var, connection URI และผ่านชื่อโฮสต์ localhost)

### ถ้าจำรหัส `postgres` ได้ หรือรีเซ็ตแล้ว

1. สร้าง role และ database บน service

   ```powershell
   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U postgres -h 127.0.0.1 -p 5432 -f <ไฟล์ create-coffeestamp.sql>
   ```

   เนื้อหาไฟล์คือ `CREATE DATABASE coffeestamp` + `CREATE USER coffeestamp` + ให้สิทธิ์ schema public
   (รหัสของ role `coffeestamp` อยู่ที่ `C:\Users\bigho\coffeestamp-db-password.txt`)

2. ดัมป์จาก portable แล้วกู้เข้า service

   ```powershell
   & "$env:USERPROFILE\pgsql\bin\pg_dump.exe" -U coffeestamp -h 127.0.0.1 -p 5433 -d coffeestamp --no-owner --no-acl -f coffeestamp.sql
   & "C:\Program Files\PostgreSQL\18\bin\psql.exe" -U coffeestamp -h 127.0.0.1 -p 5432 -d coffeestamp -f coffeestamp.sql
   ```

3. ชี้ connection string กลับไปพอร์ต 5432

   ```powershell
   cd src/CoffeeStamp.Web
   dotnet user-secrets set "ConnectionStrings:Default" "Host=127.0.0.1;Port=5432;Database=coffeestamp;Username=coffeestamp;Password=<รหัสจากไฟล์ข้างบน>"
   ```

4. หยุด portable แล้วลบทิ้งเมื่อมั่นใจว่าข้อมูลครบ (`C:\Users\bigho\pgsql` และ `C:\Users\bigho\pgdata`)
   ช่วยคืนพื้นที่ไดรฟ์ C: ได้ด้วย

### วิธีรีเซ็ตรหัส postgres ของ service (ต้องสิทธิ์แอดมิน)

1. แก้ `C:\Program Files\PostgreSQL\18\data\pg_hba.conf` เปลี่ยนบรรทัด
   `host all all 127.0.0.1/32 scram-sha-256` เป็น `trust` **ชั่วคราว**
   (ต้องเขียนไฟล์แบบไม่มี BOM ไม่งั้น PostgreSQL จะไม่สตาร์ต)
2. `Restart-Service postgresql-x64-18`
3. `psql -U postgres -h 127.0.0.1 -p 5432 -c "ALTER USER postgres PASSWORD '<รหัสใหม่>';"`
4. **เปลี่ยน `pg_hba.conf` กลับเป็น `scram-sha-256` แล้ว restart service อีกครั้ง** ห้ามลืมขั้นนี้

> หมายเหตุความปลอดภัย: service ตัวนี้ฟังที่ `0.0.0.0:5432` คือรับการเชื่อมต่อจากทุก interface
> ไม่ใช่แค่ในเครื่อง ถ้าไม่ได้ต้องการให้เครื่องอื่นในวงต่อเข้ามา ควรตั้ง
> `listen_addresses = 'localhost'` ใน `postgresql.conf` แล้ว restart service

## PostgreSQL สตาร์ตอัตโนมัติ (Scheduled Task)

ตั้งไว้แล้วเมื่อ 22 ต.ค. 2026 เพราะ PostgreSQL แบบ portable ไม่ใช่ Windows service
จึงไม่สตาร์ตเองตอนเปิดเครื่อง (ระหว่างพัฒนาเจอว่าดับไปเอง 3 ครั้ง)

| หัวข้อ | ค่า |
|---|---|
| ชื่อ task | `CoffeeStamp PostgreSQL` |
| สคริปต์ | `scripts/start-postgres.ps1` |
| ทริกเกอร์ | ตอนล็อกอินของผู้ใช้ **และ** ซ้ำทุก 10 นาที |
| สิทธิ์ | รันในสิทธิ์ผู้ใช้ปกติ ไม่ต้องใช้แอดมิน |
| log ของ task | `C:\Users\bigho\pgdata\autostart.log` |

ทริกเกอร์ทุก 10 นาทีมีไว้กู้คืนกรณีฐานข้อมูลดับกลางวัน ไม่ใช่แค่ตอนเปิดเครื่อง
สคริปต์เช็กก่อนเสมอว่ารันอยู่หรือยัง ถ้ารันอยู่จะจบทันทีโดยไม่ทำอะไร (ทดสอบแล้ว PID ไม่เปลี่ยน)

### คำสั่งที่ใช้บ่อย

```powershell
Get-ScheduledTaskInfo -TaskName "CoffeeStamp PostgreSQL"   # ดูผลรอบล่าสุด (0 = สำเร็จ)
Start-ScheduledTask   -TaskName "CoffeeStamp PostgreSQL"   # สั่งให้ทำงานเดี๋ยวนี้
Disable-ScheduledTask -TaskName "CoffeeStamp PostgreSQL"   # ปิดชั่วคราว
Unregister-ScheduledTask -TaskName "CoffeeStamp PostgreSQL" -Confirm:$false   # ลบทิ้ง
```

ถ้าย้ายไปใช้ PostgreSQL แบบ service (หัวข้อ "ย้ายไปใช้ service") ให้ลบ task นี้ทิ้ง

### สองกับดักที่เจอตอนตั้ง task (อย่าทำซ้ำ)

1. **ไฟล์ `.ps1` ที่มีภาษาไทยต้องบันทึกเป็น UTF-8 พร้อม BOM**
   PowerShell 5.1 อ่านไฟล์ที่ไม่มี BOM เป็น ANSI ทำให้สตริงไทยพังและ parse ไม่ผ่านทั้งไฟล์
   (กลับด้านกับ `pg_hba.conf` ที่ **ห้าม** มี BOM)
2. **`Start-Process -ArgumentList` ของ PowerShell 5.1 ตัดเครื่องหมายคำพูดในสมาชิกของ array ทิ้ง**
   ทำให้ `-o "-p 5433"` กลายเป็นสองอาร์กิวเมนต์ แล้ว pg_ctl ฟ้อง
   `unrecognized operation mode "5433"` ต้องส่ง argument เป็นสตริงเดียวและใส่เครื่องหมายเอง
   และต้อง redirect stdout/stderr ลงไฟล์ ไม่งั้นสคริปต์จะค้างเพราะ postgres ถือ handle ของ console
