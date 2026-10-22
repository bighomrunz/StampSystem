namespace CoffeeStamp.Web.Services;

/// <summary>
/// CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, ไม่ reflect, ไม่ xor ตอนออก)
/// ใช้เป็น tag 63 ของ EMVCo QR
/// </summary>
public static class Crc16Ccitt
{
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var bt in data)
        {
            crc ^= (ushort)(bt << 8);
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0
                    ? (ushort)((crc << 1) ^ 0x1021)
                    : (ushort)(crc << 1);
            }
        }
        return crc;
    }

    public static ushort Compute(string ascii) =>
        Compute(System.Text.Encoding.ASCII.GetBytes(ascii));

    public static string ComputeHex(string ascii) => Compute(ascii).ToString("X4");
}
