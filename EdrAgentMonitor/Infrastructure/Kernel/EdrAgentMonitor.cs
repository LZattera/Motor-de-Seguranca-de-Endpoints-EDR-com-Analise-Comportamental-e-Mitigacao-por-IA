using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using Grpc.Net.Client;
using EdrGateway.Protos;
using System.Threading;

namespace EdrAgentMonitor.Infrastructure.Kernel
{
    public class EdrAgentMonitor
    {
        private TraceEventSession? _session;
        private GrpcChannel? _channel;
        private global::Grpc.Core.AsyncClientStreamingCall<TelemetryEvent, AckReply>? _call;

        // 1. INICIALIZAÇÃO OBRIGATÓRIA DO SEMÁFORO
        private static readonly SemaphoreSlim _streamLock = new SemaphoreSlim(1, 1);

        public void StartMonitoring()
        {
            _channel = GrpcChannel.ForAddress("http://localhost:5195");
            var client = new TelemetryIngestor.TelemetryIngestorClient(_channel);
            _call = client.StreamEvents();

            using (var session = new TraceEventSession("MeuEdrSession"))
            {
                _session = session;
                session.EnableKernelProvider(KernelTraceEventParser.Keywords.Process);

                session.Source.Kernel.ProcessStart += async data =>
                {
                    Console.WriteLine($"[ALERTA] Processo Iniciado: {data.ProcessName} (PID: {data.ProcessID})");

                    try
                    {
                        // 2. VERIFICAÇÃO SE A LIGAÇÃO E O FLUXO EXISTEM
                        if (_call != null && _call.RequestStream != null)
                        {
                            await _streamLock.WaitAsync();
                            try
                            {
                                // 3. BLINDAGEM CONTRA VALORES NULOS DO KERNEL
                                var safeAgentId = Environment.MachineName ?? "AgenteDesconhecido";
                                var safeProcessName = string.IsNullOrEmpty(data.ProcessName) ? "Desconhecido" : data.ProcessName;
                                var safeCommandLine = string.IsNullOrEmpty(data.CommandLine) ? "Sem linha de comando" : data.CommandLine;
                                var safeParentId = data.ParentID.ToString() ?? "0";

                                var evento = new TelemetryEvent
                                {
                                    AgentId = safeAgentId,
                                    Severity = "INFO",
                                    EventType = "ProcessExecution",
                                    ProcessName = safeProcessName,
                                    CommandLine = safeCommandLine,
                                    ParentProcess = safeParentId
                                };

                                // 4. ENVIO SEGURO
                                await _call.RequestStream.WriteAsync(evento);
                            }
                            finally
                            {
                                _streamLock.Release();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERRO gRPC] Falha ao enviar para o Gateway: {ex.Message}");
                    }
                };

                session.Source.Process();
                _session = null;
            }
        }

        public void Stop()
        {
            _session?.Stop();
            // Adicionado ?. para evitar erros ao fechar a aplicação
            _call?.RequestStream?.CompleteAsync().Wait();
            _channel?.Dispose();
        }
    }
}