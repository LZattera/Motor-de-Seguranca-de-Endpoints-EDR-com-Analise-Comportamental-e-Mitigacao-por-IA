using EdrAgentMonitor.Infrastructure.Grpc; // Namespace do seu contrato avançado
using Google.Protobuf.WellKnownTypes; // Necessário para o Timestamp do Protobuf
using Grpc.Net.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System.Threading.Channels;
using Grpc.Core;

namespace EdrAgentMonitor.Infrastructure.Kernel
{
    public class EdrAgentMonitor
    {
        private TraceEventSession? _session;
        private GrpcChannel? _channel;

        // Novo tipo de chamada: Duplex (Bidirecional)
        private global::Grpc.Core.AsyncDuplexStreamingCall<TelemetryBatch, MitigationCommand>? _call;

        // Fila de alta performance (produtor/consumidor) para não bloquear o Kernel
        private readonly Channel<ProcessLog> _eventQueue = Channel.CreateUnbounded<ProcessLog>();
        private ulong _sequence = 0;

        public void StartMonitoring()
        {
            _channel = GrpcChannel.ForAddress("http://localhost:5195");
            var client = new EdrTelemetryService.EdrTelemetryServiceClient(_channel);
            _call = client.StreamTelemetry();

            // 1. Inicia a thread responsável por empacotar os lotes e enviar
            _ = Task.Run(SendBatchesAsync);

            // 2. Inicia a thread que fica À ESCUTA da IA (Onde a Fase 3 vai acontecer)
            _ = Task.Run(ReceiveCommandsAsync);

            using (var session = new TraceEventSession("MeuEdrSession"))
            {
                _session = session;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

                session.Source.Kernel.ProcessStart += data =>
                {
                    Console.WriteLine($"[KERNEL] Processo detetado: {data.ProcessName} (PID: {data.ProcessID})");

                    var safeProcessName = string.IsNullOrEmpty(data.ProcessName) ? "Desconhecido" : data.ProcessName;
                    var safeCommandLine = string.IsNullOrEmpty(data.CommandLine) ? "Sem linha de comando" : data.CommandLine;

                    var processLog = new ProcessLog
                    {
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                        ProcessId = data.ProcessID,
                        ParentProcessId = data.ParentID,
                        ProcessName = safeProcessName,
                        ImagePath = data.ImageFileName ?? "",
                        CommandLine = safeCommandLine
                    };

                    // Envia para a fila em memória (não bloqueia a rede nem o kernel)
                    _eventQueue.Writer.TryWrite(processLog);
                };

                session.Source.Process();
                _session = null;
            }
        }

        private async Task SendBatchesAsync()
        {
            if (_call == null) return;

            // Fica a ler da fila assim que os eventos chegam
            await foreach (var processLog in _eventQueue.Reader.ReadAllAsync())
            {
                _sequence++;
                var batch = new TelemetryBatch
                {
                    AgentId = Environment.MachineName,
                    Hostname = Environment.MachineName,
                    BatchSequence = _sequence,
                    SentAt = Timestamp.FromDateTime(DateTime.UtcNow)
                };

                batch.ProcessLogs.Add(processLog);

                try
                {
                    await _call.RequestStream.WriteAsync(batch);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERRO gRPC] Falha ao enviar lote: {ex.Message}");
                }
            }
        }

        private async Task ReceiveCommandsAsync()
        {
            if (_call == null) return;

            try
            {
                // Este loop fica eternamente aberto à espera que o Gateway devolva algo
                await foreach (var command in _call.ResponseStream.ReadAllAsync())
                {
                    Console.WriteLine("\n========================================================");
                    Console.WriteLine("[!!!] COMANDO DE MITIGAÇÃO RECEBIDO DA IA [!!!]");
                    Console.WriteLine("========================================================\n");

                    // Na Fase 3, vamos desencriptar e executar o payload que chega aqui!
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AVISO] Ligação de mitigação encerrada: {ex.Message}");
            }
        }

        public void Stop()
        {
            _session?.Stop();
            _eventQueue.Writer.Complete();
            _call?.RequestStream?.CompleteAsync().Wait();
            _channel?.Dispose();
        }
    }
}