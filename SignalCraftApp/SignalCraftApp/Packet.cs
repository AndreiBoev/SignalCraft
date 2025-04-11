using System;
using System.Text;
using System.Windows.Markup;

public class Packet
{
    public byte StartByte { get; private set; } = 0xAA;
    public byte Length { get; private set; }
    public byte SignalType { get; private set; }
    public byte Direction { get; private set; }
    public byte BusNumber { get; private set; }
    public byte[] Data { get; private set; }
    public ushort CRC { get; private set; }
    public byte StopByte { get; private set; } = 0xFF;

    public Packet(byte signalType, byte direction, byte busNumber, byte[] data)
    {
        SignalType = signalType;
        Direction = direction;
        BusNumber = busNumber;
        Data = data;
        Length = (byte)(3 + Data.Length); // Тип сигнала + Направление + Номер шины + Данные.
        CRC = CalculateCRC();
    }

    public Packet(string packetString)
    {
        byte[] bytes = ParseHexString(packetString);

        if (bytes.Length < 6 || bytes[0] != 0xAA || bytes[bytes.Length - 1] != 0xFF)
            throw new ArgumentException("Invalid packet format");

        StartByte = bytes[0];
        Length = bytes[1];
        SignalType = bytes[2];
        Direction = (byte)(bytes[3] & 0x01); // Направление - 1 бит
        BusNumber = bytes[4];

        int dataLength = Length - 3;
        Data = new byte[dataLength];
        Array.Copy(bytes, 5, Data, 0, dataLength);

        CRC = BitConverter.ToUInt16(bytes, bytes.Length - 3);
        StopByte = bytes[bytes.Length - 1];

        if (CRC != CalculateCRC())
            throw new ArgumentException("CRC validation failed");
    }

    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("{0:X2} {1:X2} {2:X2} {3:X2} {4:X2} ", StartByte, Length, SignalType, Direction, BusNumber);
        foreach (var b in Data)
            sb.AppendFormat("{0:X2} ", b);
        sb.AppendFormat("{0:X2} {1:X2} {2:X2}", (CRC >> 8) & 0xFF, CRC & 0xFF, StopByte);
        return sb.ToString();
    }

    private ushort CalculateCRC()
    {
        ushort crc = 0xFFFF;  // Инициализация CRC значением 0xFFFF
        ushort polynomial = 0x8005; // Полином для CRC-16-ANSI

        // Include the length byte, signal type, direction, and bus number in CRC calculation.
        crc = CalculateByteCRC(crc, polynomial, Length);
        crc = CalculateByteCRC(crc, polynomial, SignalType);
        crc = CalculateByteCRC(crc, polynomial, (byte)((Direction << 7) | BusNumber)); // Combine Direction and bus number

        // Include the data bytes in the CRC calculation.
        foreach (byte b in Data)
        {
            crc = CalculateByteCRC(crc, polynomial, b);
        }

        return crc;
    }

    private ushort CalculateByteCRC(ushort crc, ushort polynomial, byte dataByte)
    {
        crc ^= (ushort)(dataByte);

        for (int i = 0; i < 8; i++)
        {
            if ((crc & 0x0001) != 0)  // Check the least significant bit.
                crc = (ushort)((crc >> 1) ^ polynomial); // Right shift and XOR with polynomial
            else
                crc >>= 1;  // Right Shift
        }
        return crc;
    }


    private byte[] ParseHexString(string hexString)
    {
        string[] hexValues = hexString.Split(' ', (char)StringSplitOptions.RemoveEmptyEntries);
        byte[] bytes = new byte[hexValues.Length];
        for (int i = 0; i < hexValues.Length; i++)
            bytes[i] = Convert.ToByte(hexValues[i], 16);
        return bytes;
    }
}
