namespace FactoryMES.ModbusSimulator;

// 시뮬레이터의 holding register 16개(0기반 0..15)를 만든다.
// 입력: unit id(1..247), 리스너 기동 뒤 지난 시간.
// 처리: 한 응답의 모든 값은 같은 경과 시간 한 번으로 계산한다(값이 중간에 섞이지 않게).
//       읽을 때마다 늘리지 않고 시간으로만 늘리므로 polling 횟수와 무관하다.
// 레지스터 배치(모두 big-endian word):
//   [0,1] UINT32 ABCD 누적 카운터 = unit*1000 + floor(경과 초)
//   [2]   INT16 원시 온도 = 250 + (floor(경과 초) % 10) * 10 (scale 0.1 → 25.0~34.0℃)
//   [3]   UINT16 운전 상태 1
//   [4,5] FLOAT32 ABCD 123.5
//   [6,7] UINT32 ABCD 0x12345678 (word·byte 순서 해석 확인용)
//   [8]   INT16 -123 (부호 해석 확인용)
//   나머지 0
public static class RegisterBank
{
    public const int Count = 16;

    public static ushort[] Snapshot(
        byte unitId,
        TimeSpan elapsed
    )
    {
        var seconds = (long)Math.Floor(elapsed.TotalSeconds);
        var registers = new ushort[Count];

        var counter = (uint)(unitId * 1000L + seconds);
        registers[0] = (ushort)(counter >> 16);
        registers[1] = (ushort)(counter & 0xFFFF);

        registers[2] = (ushort)(short)(250 + seconds % 10 * 10);
        registers[3] = 1;

        var real = BitConverter.SingleToUInt32Bits(123.5f);
        registers[4] = (ushort)(real >> 16);
        registers[5] = (ushort)(real & 0xFFFF);

        registers[6] = 0x1234;
        registers[7] = 0x5678;
        registers[8] = unchecked((ushort)(short)-123);

        return registers;
    }
}
