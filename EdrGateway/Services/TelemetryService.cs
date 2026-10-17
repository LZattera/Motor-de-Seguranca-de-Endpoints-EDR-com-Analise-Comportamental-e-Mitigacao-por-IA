using Grpc.Core;
using Confluent.Kafka;
using System.Text.Json;
using EdrAgentMonitor.Infrastructure.Grpc; // O namespace definido no seu proto avançado

namespace EdrGateway.Services;

// Agora herdamos do EdrTelemetryService (do contrato avançado)
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
        _logger.LogInformation("[GATEWAY] Nova ligação bidirecional encriptada estabelecida com um Agente.");

        // Loop contínuo para ler os lotes de telemetria à medida que chegam
        await foreach (var batch in requestStream.ReadAllAsync())
        {
            // O batch pode conter dezenas de processos. Vamos iterar sobre eles:
            foreach (var process in batch.ProcessLogs)
            {
                _logger.LogWarning($"[GATEWAY] Processo do lote {batch.BatchSequence} recebido de {batch.Hostname}: {process.ProcessName}");

                var payload = JsonSerializer.Serialize(new
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

                // Envia para o Redpanda (Ubuntu)
                await _kafkaProducer.ProduceAsync("edr-telemetry", new Message<Null, string> { Value = payload });
            }
        }
    }
}