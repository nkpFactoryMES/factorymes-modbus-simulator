using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace FactoryMES.ModbusSimulator;

// 리스너 기동 여부와 기동 시각. /health/live가 TCP 리스너 시작을 확인하는 데 쓴다.
public sealed class ListenerState
{
    private readonly Stopwatch clock = new();

    public bool Listening { get; private set; }

    public TimeSpan Elapsed => clock.Elapsed;

    public void Started()
    {
        clock.Restart();
        Listening = true;
    }

    public void Stopped() => Listening = false;
}

// Modbus TCP FC03 서버(BCL TcpListener만 사용, 패키지 없음).
// 입력 설정: Modbus:Port(기본 1502), Modbus:MaxConnections(기본 16),
//            Modbus:IdleSeconds(기본 30, 다음 요청 바이트를 기다리는 최대 시간).
// 처리: 연결마다 요청을 하나씩 읽고 응답한다. TCP가 나눠 보낸 바이트는 ReadExactlyAsync로 모은다.
//       연결 수 상한을 넘으면 바로 닫고, 형식을 믿을 수 없는 헤더나 제한 시간 초과도 닫는다.
// 로그: 기동·종료 Information, 요청 분기 Debug(ID·분기만), 연결 오류 Warning(예외 형식만).
//       예외 메시지·주소 전체는 남기지 않는다.
public sealed class ModbusListener(
    IConfiguration configuration,
    ListenerState state,
    ILogger<ModbusListener> logger
) : BackgroundService
{
    private int open;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = configuration.GetValue("Modbus:Port", 1502);
        var maxConnections = configuration.GetValue("Modbus:MaxConnections", 16);
        var idle = TimeSpan.FromSeconds(configuration.GetValue("Modbus:IdleSeconds", 30));

        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        state.Started();
        logger.LogInformation(
            "Modbus listener started Port={Port} MaxConnections={MaxConnections}",
            port,
            maxConnections
        );

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                if (Interlocked.Increment(ref open) > maxConnections)
                {
                    Interlocked.Decrement(ref open);
                    logger.LogWarning("Modbus connection refused: limit {Limit}", maxConnections);
                    client.Dispose();
                    continue;
                }

                _ = ServeAsync(
                    client,
                    idle,
                    stoppingToken
                );
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            state.Stopped();
            listener.Stop();
            logger.LogInformation("Modbus listener stopped Port={Port}", port);
        }
    }

    // 연결 하나를 처리한다. 상대가 닫거나, 제한 시간이 지나거나, 서비스가 멈추면 끝난다.
    private async Task ServeAsync(
        TcpClient client,
        TimeSpan idle,
        CancellationToken stoppingToken
    )
    {
        var connection = Guid.NewGuid().ToString("N")[..8];
        logger.LogDebug("Modbus connection opened Connection={Connection}", connection);
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var header = new byte[ModbusFrame.HeaderSize];
                while (!stoppingToken.IsCancellationRequested)
                {
                    using var timeout = CancellationTokenSource
                        .CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(idle);

                    // 1. 헤더 7바이트를 모은다. 첫 바이트 전에 상대가 닫으면 정상 종료다.
                    if (!await ReadFrameStartAsync(stream, header, timeout.Token))
                    {
                        break;
                    }

                    var mbap = ModbusFrame.ReadHeader(header);
                    if (!ModbusFrame.CanFrame(mbap))
                    {
                        logger.LogDebug(
                            "Modbus frame rejected Connection={Connection}"
                                + " Transaction={Transaction} Unit={Unit} Length={Length}",
                            connection,
                            mbap.TransactionId,
                            mbap.UnitId,
                            mbap.Length
                        );
                        break;
                    }

                    // 2. 헤더가 알려 준 길이만큼 PDU를 모은다(같은 제한 시간 안에서).
                    var pdu = new byte[mbap.Length - 1];
                    await stream.ReadExactlyAsync(pdu, timeout.Token);

                    // 3. 응답 1건은 같은 경과 시간 스냅샷 하나로 만든다.
                    var elapsed = state.Elapsed;
                    var reply = ModbusFrame.Respond(
                        mbap,
                        pdu,
                        unit => RegisterBank.Snapshot(unit, elapsed)
                    );
                    await stream.WriteAsync(reply.Bytes, timeout.Token);
                    logger.LogDebug(
                        "Modbus request Connection={Connection} Transaction={Transaction}"
                            + " Unit={Unit} Function={Function} Outcome={Outcome}",
                        connection,
                        mbap.TransactionId,
                        mbap.UnitId,
                        pdu[0],
                        reply.Outcome
                    );
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Modbus connection idle timeout Connection={Connection}", connection);
        }
        catch (EndOfStreamException)
        {
            logger.LogDebug(
                "Modbus connection closed mid-frame Connection={Connection}",
                connection
            );
        }
        catch (Exception error) when (error is IOException or SocketException)
        {
            logger.LogWarning(
                "Modbus connection error Connection={Connection} Type={Type}",
                connection,
                error.GetType().Name
            );
        }
        finally
        {
            Interlocked.Decrement(ref open);
            logger.LogDebug("Modbus connection closed Connection={Connection}", connection);
        }
    }

    // 헤더를 읽는다. 아무 바이트도 오기 전에 상대가 닫으면 false.
    // 일부만 받고 닫히면 EndOfStreamException(요청 중간 끊김)이다.
    private static async Task<bool> ReadFrameStartAsync(
        NetworkStream stream,
        byte[] header,
        CancellationToken token
    )
    {
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0)
        {
            return false;
        }

        await stream.ReadExactlyAsync(header.AsMemory(1), token);
        return true;
    }
}
