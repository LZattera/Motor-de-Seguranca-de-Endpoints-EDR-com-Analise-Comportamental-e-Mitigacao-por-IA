using EdrGateway.Services;
using Confluent.Kafka;

var builder = WebApplication.CreateBuilder(args);

// Configuração do servidor Kafka (Redpanda no Ubuntu)
var producerConfig = new ProducerConfig
{
    BootstrapServers = "192.168.1.3:19092" // Verifique se colocou o IP do Ubuntu aqui e não localhost
};

// Injetar o produtor Kafka como Singleton (uma única instância partilhada)
builder.Services.AddSingleton<IProducer<Null, string>>(x =>
    new ProducerBuilder<Null, string>(producerConfig).Build());

builder.Services.AddGrpc();

var app = builder.Build();

//app.MapGrpcService<TelemetryService>();
app.MapGrpcService<AdvancedTelemetryService>();
app.MapGet("/", () => "O Gateway gRPC do EDR está a correr.");

app.Run();