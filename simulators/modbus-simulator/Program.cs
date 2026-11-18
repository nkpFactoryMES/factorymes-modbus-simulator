using FactoryMES.Foundation;
using FactoryMES.ModbusSimulator;

// Modbus TCP 설비 시뮬레이터: 실제 PLC 대신 TCP 1502에서 FC03(holding register 읽기)만 응답한다.
// Equipment Connector의 Modbus 수집기가 승인된 주소(modbus.tcp://modbus-simulator:1502/unit/N)로 읽는다.
// - 쓰기 기능·시험용 리셋 API는 없다. 카운터는 프로세스 기동 뒤 경과 시간으로만 늘어난다.
//   (정지·재기동 장애 검증은 Compose에서 컨테이너를 다시 시작해 확인한다.)
// - HTTP 8080은 GET /health/live 하나다. TCP 리스너가 시작됐을 때만 200을 돌려준다.
// - 외부 장비와 통신하지 않는다(CostPolicy로 로컬 전용 설정을 확인한다).
if (await HealthProbe.TryRunAsync(args)) return;

var builder = WebApplication.CreateBuilder(args);
CostPolicy.EnsureLocalOnly(builder.Configuration);
builder.Services.AddSingleton<ListenerState>();
builder.Services.AddHostedService<ModbusListener>();

var app = builder.Build();
app.MapGet(
    "/health/live",
    (ListenerState state) => state.Listening
        ? Results.Ok(new { status = "live" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.Run();
