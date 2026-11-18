using System.Buffers.Binary;

namespace FactoryMES.ModbusSimulator;

// Modbus TCP 요청 1건의 MBAP 헤더(7바이트).
// TransactionId: 응답에 그대로 돌려준다. ProtocolId: Modbus는 0.
// Length: 헤더 뒤 바이트 수(unit 1 + PDU). FC03 요청은 6(unit 1 + 기능 1 + 시작 2 + 개수 2).
public readonly record struct MbapHeader(
    ushort TransactionId,
    ushort ProtocolId,
    ushort Length,
    byte UnitId
);

// 응답 바이트와 Debug 로그용 분기 이름.
public readonly record struct FrameReply(
    byte[] Bytes,
    string Outcome
);

// MBAP·FC03 해석과 응답 생성(소켓과 분리한 순수 함수). 쓰기 기능은 없다.
// 예외 코드: 01 지원하지 않는 기능, 02 주소 범위 밖, 03 잘못된 요청 형식·개수.
public static class ModbusFrame
{
    public const int HeaderSize = 7;
    public const byte ReadHoldingRegisters = 0x03;
    public const int MaxReadCount = 125;

    private const byte IllegalFunction = 0x01;
    private const byte IllegalDataAddress = 0x02;
    private const byte IllegalDataValue = 0x03;

    public static MbapHeader ReadHeader(ReadOnlySpan<byte> header) =>
        new(
            BinaryPrimitives.ReadUInt16BigEndian(header[0..2]),
            BinaryPrimitives.ReadUInt16BigEndian(header[2..4]),
            BinaryPrimitives.ReadUInt16BigEndian(header[4..6]),
            header[6]
        );

    // 헤더만으로 요청 경계를 믿을 수 없으면 false다. 이때는 응답 없이 연결을 닫는다.
    // - ProtocolId가 0이 아님
    // - Length가 2(unit+기능) 미만이거나 254(unit 1 + PDU 최대 253) 초과
    // - unit id가 1..247 밖(0 브로드캐스트·248 이상 예약값은 받지 않는다)
    public static bool CanFrame(MbapHeader header) =>
        header.ProtocolId == 0
        && header.Length is >= 2 and <= 254
        && header.UnitId is >= 1 and <= 247;

    // PDU(Length - 1 바이트)를 처리해 응답을 만든다.
    // 입력: 헤더, PDU, unit별 레지스터 스냅샷을 만드는 함수(응답 1건에 한 번만 부른다).
    // 처리 순서: 기능 코드 → 요청 길이 → 개수 → 주소 범위.
    public static FrameReply Respond(
        MbapHeader header,
        ReadOnlySpan<byte> pdu,
        Func<byte, ushort[]> snapshot
    )
    {
        var function = pdu[0];
        if (function != ReadHoldingRegisters)
        {
            return Exception(header, function, IllegalFunction, "ILLEGAL_FUNCTION");
        }

        if (pdu.Length != 5)
        {
            return Exception(header, function, IllegalDataValue, "BAD_LENGTH");
        }

        var start = BinaryPrimitives.ReadUInt16BigEndian(pdu[1..3]);
        var count = BinaryPrimitives.ReadUInt16BigEndian(pdu[3..5]);
        if (count is < 1 or > MaxReadCount)
        {
            return Exception(header, function, IllegalDataValue, "BAD_COUNT");
        }

        if (start + count > RegisterBank.Count)
        {
            return Exception(header, function, IllegalDataAddress, "BAD_ADDRESS");
        }

        var registers = snapshot(header.UnitId);
        var bytes = new byte[HeaderSize + 2 + count * 2];
        WriteHeader(bytes, header, (ushort)(3 + count * 2));
        bytes[7] = ReadHoldingRegisters;
        bytes[8] = (byte)(count * 2);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(
                bytes.AsSpan(9 + i * 2, 2),
                registers[start + i]
            );
        }

        return new FrameReply(bytes, "OK");
    }

    // 예외 응답: 기능 코드에 0x80을 더하고 예외 코드 1바이트를 붙인다(Length = 3).
    private static FrameReply Exception(
        MbapHeader header,
        byte function,
        byte code,
        string outcome
    )
    {
        var bytes = new byte[HeaderSize + 2];
        WriteHeader(bytes, header, 3);
        bytes[7] = (byte)(function | 0x80);
        bytes[8] = code;

        return new FrameReply(bytes, outcome);
    }

    private static void WriteHeader(
        byte[] bytes,
        MbapHeader header,
        ushort length
    )
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0, 2), header.TransactionId);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), length);
        bytes[6] = header.UnitId;
    }
}
