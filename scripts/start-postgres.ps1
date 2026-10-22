# สตาร์ต PostgreSQL แบบ portable ของ Coffee Stamp ถ้ายังไม่ได้รันอยู่
#
# ตัว portable ไม่ได้เป็น Windows service จึงไม่สตาร์ตเองตอนเปิดเครื่อง
# สคริปต์นี้ถูกเรียกโดย Scheduled Task "CoffeeStamp PostgreSQL"
# ทั้งตอนล็อกอินและทุก ๆ 10 นาที จึงต้องรันซ้ำได้โดยไม่เกิดผลข้างเคียง
#
# ข้อควรระวังสองข้อที่เคยทำให้พังมาแล้ว
# 1. ไฟล์นี้ต้องบันทึกเป็น UTF-8 "พร้อม BOM" ไม่งั้น PowerShell 5.1 อ่านข้อความไทยเป็น ANSI
#    แล้ว parse ไม่ผ่านทั้งไฟล์
# 2. ห้ามเรียก pg_ctl start ตรง ๆ เพราะ process ลูกจะถือ handle ของ console ไว้
#    ทำให้สคริปต์ค้างจน task ถูก timeout ฆ่า ต้องใช้ Start-Process พร้อม redirect ลงไฟล์

$ErrorActionPreference = "Continue"

$pgBin = Join-Path $env:USERPROFILE "pgsql\bin"
$pgData = Join-Path $env:USERPROFILE "pgdata"
$port = 5433
$serverLog = Join-Path $pgData "server.log"
$taskLog = Join-Path $pgData "autostart.log"
$pgCtl = Join-Path $pgBin "pg_ctl.exe"

function Write-TaskLog([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $message
    Add-Content -Path $taskLog -Value $line -Encoding utf8
}

function Test-ServerRunning {
    # pg_ctl status คืน 0 เมื่อรันอยู่ และ 3 เมื่อไม่ได้รัน
    # ส่ง argument เป็นสตริงเดียวเพราะ Start-Process ของ PowerShell 5.1
    # ตัดเครื่องหมายคำพูดในสมาชิกของ array ทิ้ง ทำให้ค่าที่มีช่องว่างถูกแยกผิด
    $arguments = '-D "{0}" status' -f $pgData

    $p = Start-Process -FilePath $pgCtl -ArgumentList $arguments `
        -NoNewWindow -PassThru -Wait `
        -RedirectStandardOutput "$env:TEMP\cs-pg-status.txt" `
        -RedirectStandardError "$env:TEMP\cs-pg-status-err.txt"
    return $p.ExitCode -eq 0
}

if (-not (Test-Path $pgCtl) -or -not (Test-Path (Join-Path $pgData "PG_VERSION"))) {
    Write-TaskLog "ไม่พบ PostgreSQL ที่ $pgBin หรือ data ที่ $pgData จึงข้ามการสตาร์ต"
    exit 0
}

if (Test-ServerRunning) {
    exit 0
}

# redirect ลงไฟล์เพื่อไม่ให้ postgres สืบทอด handle ของ console แล้วทำให้สคริปต์ค้าง
# และต้องคุมเครื่องหมายคำพูดเอง ไม่งั้น -o "-p 5433" จะถูกแยกเป็นสองอาร์กิวเมนต์
$startArguments = '-D "{0}" -l "{1}" -o "-p {2}" start' -f $pgData, $serverLog, $port

Start-Process -FilePath $pgCtl -ArgumentList $startArguments `
    -NoNewWindow -PassThru `
    -RedirectStandardOutput "$env:TEMP\cs-pg-start.txt" `
    -RedirectStandardError "$env:TEMP\cs-pg-start-err.txt" | Out-Null

# รอให้พร้อมรับการเชื่อมต่อ ไม่เกิน 30 วินาที
for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 2
    if (Test-ServerRunning) {
        Write-TaskLog "สตาร์ต PostgreSQL ที่พอร์ต $port สำเร็จ"
        exit 0
    }
}

Write-TaskLog "สตาร์ต PostgreSQL ไม่สำเร็จ ดูรายละเอียดใน $serverLog"
exit 1
