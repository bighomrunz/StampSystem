# รันแอปพนักงานสำหรับการพัฒนา
# .NET SDK บนเครื่องนี้ติดตั้งแบบ per-user และไม่อยู่ใน PATH จึงต้อง set เองทุกครั้ง
$ErrorActionPreference = "Stop"

$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
$env:Path = "$env:DOTNET_ROOT;$env:Path"

Push-Location $PSScriptRoot
try {
    Write-Host "dotnet: $(dotnet --version)" -ForegroundColor DarkGray
    dotnet run --project src/CoffeeStamp.Web/CoffeeStamp.Web.csproj --launch-profile https
}
finally {
    Pop-Location
}
