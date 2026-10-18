# Motor de Segurança de Endpoints (EDR) com Análise Comportamental e Mitigação por IA

Um sistema EDR (*Endpoint Detection and Response*) de nível *Enterprise*, construído do zero para monitorizar, analisar e mitigar ameaças em ambientes Windows em tempo real. A arquitetura combina captura de eventos a nível de Kernel, transporte gRPC de alta performance, mensageria assíncrona com Apache Kafka e orquestração de resposta a incidentes utilizando Inteligência Artificial local.

## 🚀 Arquitetura do Sistema

O projeto adota uma arquitetura de microsserviços distribuída, desenhada para garantir resiliência, baixa latência e processamento em lote (*batch processing*).

1. **Agente (Sensor & Executor):** monitoriza a criação de processos diretamente no Kernel do Windows via ETW (*Event Tracing for Windows*), sem bloquear o sistema operativo. Os eventos são colocados numa fila de memória de alta performance (`Channel`) e enviados em lotes.
2. **Gateway (Ingestão & Controlo):** serviço gRPC bidirecional que recebe a telemetria, injeta-a num cluster Kafka e mantém um canal seguro e persistente aberto com o Agente para enviar comandos de mitigação (*callbacks*).
3. **Infraestrutura de Mensageria:** Redpanda (compatível com Apache Kafka) a correr em contentores Docker para absorver picos de tráfego de telemetria de múltiplos endpoints.
4. **Cérebro (Automação & IA):** o n8n atua como motor de orquestração SOAR (*Security Orchestration, Automation, and Response*), consumindo ativamente o tópico Kafka e alimentando o modelo LLM (Ollama) para gerar scripts PowerShell cirúrgicos de resposta a ameaças.

### Fluxo de Orquestração SOAR (n8n + Ollama)

![Workflow n8n: Kafka Trigger → Message a model (Ollama)](docs/n8n-workflow.png)

*Fluxo atual no n8n: o **Kafka Trigger** consome continuamente a telemetria do tópico e encaminha cada evento para o nó **Message a model** (Ollama), que faz a análise comportamental e gera a resposta de mitigação. O retorno ao Gateway via webhook ainda está no roadmap.*

## 🛠️ Stack Tecnológico

| Área | Tecnologia |
|---|---|
| **Linguagem principal** | C# (.NET 8/9) |
| **Comunicação** | gRPC (streams bidirecionais), Protocol Buffers (Protobuf) |
| **Monitorização nativa** | ETW (`Microsoft.Diagnostics.Tracing.TraceEvent`) |
| **Mensageria/Fila** | Apache Kafka / Redpanda (`Confluent.Kafka`) |
| **Orquestração SOAR** | n8n |
| **Inteligência Artificial** | Ollama (LLM `phi3` para geração de código local e segura) |
| **Infraestrutura** | Docker, Ubuntu Linux |

## ✨ Funcionalidades Principais

* **Captura a nível de Kernel:** utilização de `KernelTraceEventParser` para intercetar o ciclo de vida de processos no exato milissegundo em que ocorrem.
* **Telemetria de alta velocidade:** sistema produtor/consumidor com `Channel<T>` para evitar bloqueios de I/O na máquina hospedeira.
* **Comunicação bidirecional contínua:** contratos gRPC avançados (`AsyncDuplexStreamingCall`) que permitem streaming de telemetria e receção instantânea de comandos de defesa no mesmo túnel.
* **Execução silenciosa de mitigações:** o Agente invoca respostas geradas pela IA diretamente em `powershell.exe`, operando de forma 100% invisível ao utilizador (`CreateNoWindow = true`).
* **Segurança e estabilidade:** mecanismos de salvaguarda, *whitelisting* e isolamento de processos vitais do sistema (ex.: prevenção de `NullReferenceException` em `svchost`).

## ⚙️ Como Executar o Projeto

### Pré-requisitos

* .NET SDK 8.0 ou superior
* Ambiente Linux (ou WSL) com Docker para correr a infraestrutura Kafka/n8n/Ollama
* Privilégios de Administrador no Windows (obrigatório para interagir com o ETW)
* [Npcap](https://npcap.com) instalado no Windows (necessário para o sniffer de rede via SharpPcap; sem ele o Agente mostra `DllNotFoundException: wpcap`)

### 1. Subir a infraestrutura (Ubuntu/Docker)

Inicia o cluster Redpanda, a base de dados vetorial, o n8n e o Ollama:

```bash
docker compose up -d
```

### 2. Iniciar o Gateway (Windows)

O Gateway fica à escuta na porta gRPC:

```powershell
cd EdrGateway
dotnet run
```

### 3. Iniciar o Agente (Windows)

Numa nova janela de terminal, **como Administrador**:

```powershell
cd EdrAgentMonitor
dotnet run
```

### Saída do Agente (EdrAgentMonitor)

![Saída do EdrAgentMonitor detetando e bloqueando um processo](saidaAgentMonitor.png)

*O Agente a correr no Windows: o evento de Kernel (ETW) deteta a criação do processo `CalculatorApp` (PID 25116) e a mitigação é executada de imediato com `Stop-Process -Name "CalculatorApp" -Force`, após o processo ser bloqueado por uma regra rígida do EDR. O erro `wpcap` no início da captura corresponde ao sniffer de rede, que exige o Npcap instalado (ver pré-requisitos).*

## 🗺️ Próximos Passos (Roadmap)

- [ ] **Assinaturas criptográficas:** implementar verificação ECDSA P-256 no Agente para garantir que os comandos de mitigação não podem ser forjados por atacantes na rede (Zero Trust).
- [ ] **Integração plena do webhook:** ligar o fluxo automatizado do n8n (pós-análise da IA) diretamente à API do Gateway, completando o ciclo de resposta 100% autónomo.
- [ ] **Análise heurística secundária:** deteção de injeção de DLLs e padrões de tráfego de rede anómalos via SharpPcap.

---

*Desenvolvido como demonstração de engenharia de software avançada, sistemas distribuídos e cibersegurança.*