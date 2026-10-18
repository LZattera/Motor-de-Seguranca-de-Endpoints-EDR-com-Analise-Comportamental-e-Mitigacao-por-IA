using Grpc.Core;
using Confluent.Kafka;
using System.Text.Json;
using Google.Protobuf; // Necessário para o ToByteString e ByteString
using EdrAgentMonitor.Infrastructure.Grpc;

namespace EdrGateway.Services;

public class AdvancedTelemetryService : EdrTelemetryService.EdrTelemetryServiceBase
{
    private readonly ILogger<AdvancedTelemetryService> _logger;
    private readonly IProducer<Null, string> _kafkaProducer;

    public AdvancedTelemetryService(ILogger<AdvancedTelemetryService> logger, IProducer<Null, string> kafkaProducer)
    {
        _logger = logger;
        _kafkaProducer = kafkaProducer;
    }

    // Canal bidirecional: lê os lotes (request) e permite devolver comandos (response)
    public override async Task StreamTelemetry(
        IAsyncStreamReader<TelemetryBatch> requestStream,
        IServerStreamWriter<MitigationCommand> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("[GATEWAY] Nova ligação bidirecional estabelecida com um Agente.");

        await foreach (var batch in requestStream.ReadAllAsync())
        {
            foreach (var process in batch.ProcessLogs)
            {
                _logger.LogWarning($"[GATEWAY] Processo detetado: {process.ProcessName}");

                // 1. REGISTAR NO N8N (KAFKA)
                var kafkaPayload = JsonSerializer.Serialize(new
                {
                    agent_id = batch.AgentId,
                    severity = "INFO",
                    event_type = "ProcessExecution",
                    details = new
                    {
                        process_name = process.ProcessName,
                        command_line = process.CommandLine,
                        parent_process = process.ParentProcessId.ToString()
                    }
                });

                await _kafkaProducer.ProduceAsync("edr-telemetry", new Message<Null, string> { Value = kafkaPayload });

                // 2. DISPARAR MITIGAÇÃO (CALCULADORA)
                if (process.ProcessName.Contains("CalculatorApp", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogCritical($"[GATEWAY] AMEAÇA DETETADA: {process.ProcessName}. A emitir ordem de mitigação...");

                    var payloadObj = new MitigationPayload
                    {
                        CommandId = Guid.NewGuid().ToString(),
                        TargetAgentId = batch.AgentId,
                        PowershellScript = $"Stop-Process -Name \"{process.ProcessName}\" -Force",
                        Reason = "Processo bloqueado por regra rígida do EDR."
                    };

                    var command = new MitigationCommand
                    {
                        Payload = payloadObj.ToByteString(),
                        Signature = ByteString.CopyFromUtf8("ASSINATURA_ECDSA_SIMULADA"),
                        KeyId = "chave-publica-01"
                    };

                    await responseStream.WriteAsync(command);
                }
            }
        }
    }
}