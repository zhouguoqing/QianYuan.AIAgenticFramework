

# QianYuan · 乾元 Framework Agéntico

Un framework agéntico escrito en C# .NET 10, inspirado en el paradigma ReAct, que soporta carga progresiva de habilidades (Skills), múltiples proveedores de modelos de lenguaje (LLM), servidores MCP, reconocimiento de imágenes, WebAPI con streaming, interfaz web React y la integración de DingTalk.

## Características

| Dimensión | Implementación |
|------|------|
| Lenguaje/Plataforma | C# 13 / .NET 10 |
| Patrón de Agente | Ciclo ReAct (Thought-Action-Observation); Loop Engineering; Invocación anidada Agente-como-herramienta (Agent-as-Tool) |
| Sistema de Skills | Carga progresiva con `ISkill` abstracto + `SkillManager`; selección topK mediante puntuación por intención de usuario |
| Markdown Skill | Carga recursiva de `Skill.md` / `SKILL.md` comunes de la industria desde un directorio especificado, mapeando frontmatter a un manifiesto de Skills y el cuerpo de texto como sistema prompt al activarse |
| Proveedor de Modelos | Compatible con OpenAI (GPT/Kimi/MiniMax/Qwen-compat/DeepSeek/OpenRouter/NEWAPI), Azure OpenAI, Anthropic Claude, Google Gemini, Qwen DashScope nativo |
| Multimodal | Texto + Imagen (URL / base64) + Llamadas a herramientas |
| Salida con streaming | SSE (`/api/chat/stream`) + SignalR Hub (`/hubs/chat`) |
| Búsqueda web | DuckDuckGo (sin clave) / Tavily / Bing / Brave |
| Habilidad de Visión | Herramienta `image_describe`, enrutada a cualquier proveedor compatible con visión |
| MCP | Cliente JSON-RPC 2.0 (stdio) + Servidor (HTTP/SSE + expon de Skills locales a externos) |
| Registro de Agentes | `IAgentRegistry`, los agentes pueden invocarse entre sí (herramienta `agent.<id>`) |
| Tienda de Agentes | Creación visual, edición, orquestación y prueba de agentes corporativos; soporta montaje de Skills, servidores MCP y CLI Services |
| WebUI | React 19 + Vite + TS, renderizado de stream SSE, Markdown, pegado de imágenes |
| Escritorio | Contenedor Electron, inicio automático de la API local, soporte para selección nativa de archivos,读写/escritura en área de trabajo local y depuración de escritorio |
| DingTalk | Validación de firma de bot personalizado + actualización de tarjetas Markdown por fragmentos |

## Estructura del Proyecto

```
QianYuan.AgenticFramework/
├── QianYuan.AgenticFramework.sln
├── nuget.config                # Bloquea a nuget.org
├── Directory.Build.props       # net10.0, nullable, C# más reciente
├── src/
│   ├── QianYuan.Core/                    # Abstracciones + Modelos + Chunk de streaming + Excepciones
│   ├── QianYuan.Kernel/                  # Motor ReAct, SkillManager, Registros de Agentes/Proveedores
│   ├── QianYuan.Providers.OpenAICompat/  # Protocolo OpenAI (GPT/Kimi/MiniMax/Qwen-compat/NEWAPI)
│   ├── QianYuan.Providers.AzureOpenAI/   # Azure OpenAI Service (deployment + api-version)
│   ├── QianYuan.Providers.Anthropic/     # API de Mensajes Claude
│   ├── QianYuan.Providers.Gemini/        # Gemini v1beta
│   ├── QianYuan.Providers.QwenNative/    # DashScope nativo
│   ├── QianYuan.Skills.Builtin/          # WebSearch / Vision / FileSystem / Code
│   ├── QianYuan.Mcp/                     # MCP Client (stdio) + Server core
│   ├── QianYuan.UnifyCli/                # Marco de envoltura de servicio HTTPS unificado (REST API → CLI → Skill)
│   ├── QianYuan.Integrations.DingTalk/   # Envío y recepción de webhooks de DingTalk
│   ├── QianYuan.Api/                     # Host ASP.NET Core 10 (SSE + SignalR + Swagger + Agent Store API)
│   ├── QianYuan.Web/                     # React + Vite WebUI (incluye interfaz de gestión de Agent Store)
│   └── QianYuan.Desktop/                 # Contenedor Electron Desktop (preload IPC + depuración local Api/Web)
├── samples/QianYuan.Sample.Console/
└── tests/QianYuan.Core.Tests/            # xUnit + FluentAssertions
```

## Inicio Rápido

### 0. Script de inicio rápido (recomendado)

El repositorio incluye scripts de inicio rápido para tres plataformas que ejecutan automáticamente `restore` + `build` + inician la API y WebUI.
Los logs se escriben en `.runtime/logs/` y el PID del proceso en `.runtime/*.pid`.

```bash
# macOS / Linux
./scripts/start.sh        # Iniciar
./scripts/start.sh --stop # Detener

# Windows (cmd / PowerShell cualquiera)
scripts\start.cmd
scripts\stop.cmd
# o directamente
pwsh -File scripts\start.ps1
pwsh -File scripts\start.ps1 -Stop
```

El script detecta `.NET 10 SDK` y `Node.js (>=18)`; si falta Node, solo inicia la API.
URL por defecto: API `http://localhost:5050` (Swagger `/swagger`), WebUI `http://localhost:5173`.
Se pueden sobrescribir con las variables de entorno `QIANYUAN_API_URL` / `QIANYUAN_WEB_URL`.

La depuración de escritorio se puede iniciar directamente con los scripts rápidos que inician el dev-server web + Electron; el proceso principal de Electron iniciará automáticamente la API local:

```bash
# macOS / Linux
./scripts/desktop-dev.sh

# Windows (cmd / PowerShell cualquiera)
scripts\desktop-dev.cmd
pwsh -File scripts\desktop-dev.ps1
```

### 1. Compilación

```bash
cd QianYuan.AgenticFramework
dotnet build
```

### 2. Configurar la Clave de la API

Edita `src/QianYuan.Api/appsettings.json` o usa user-secrets / variables de entorno.
Basta con configurar un ApiKey para cualquier proveedor para iniciar.

#### NEWAPI / One-Hub / Agregadores de terceros

NEWAPI es completamente compatible con el protocolo OpenAI Chat Completions, por lo que se configura configura como un `OpenAICompatProviders`:

```json
{
  "ProviderId": "newapi",
  "BaseUrl": "https://your-newapi-host/v1",
  "ApiKey": "sk-...",
  "DefaultModel": "gpt-4o-mini",
  "SupportsVision": true
}
```

`ProviderId` es arbitrario, el Kernel lo usa para enrutamiento; `BaseUrl` es la URL pública de tu implementación de NEWAPI.

#### Azure OpenAI Service

La URL de Azure se determina por el nombre de deployment (no el nombre del modelo), y requiere el parámetro de consulta `api-version` y el encabezado `api-key`. Configúralo en el array `AzureOpenAIProviders`:

```json
{
  "ProviderId": "azure-openai",
  "Endpoint": "https://your-resource.openai.azure.com",
  "ApiKey": "<your-key>",
  "DefaultDeployment": "gpt-4o",
  "ApiVersion": "2024-10-21",
  "SupportsVision": true,
  "ModelToDeployment": {
    "gpt-4o": "gpt-4o-prod",
    "gpt-4o-mini": "gpt-4o-mini-prod"
  }
}
```

- `Endpoint` no debe llevar rutas como `/openai`; el marco las adjuntará automáticamente.
- `ModelToDeployment` es opcional; mapea los "nombres lógicos de modelo" a los nombres realesde los despl 部署 en Azure.
  Si no se configura, el `Model` proporcionado en la solicitud se usará directamente como deployment.
- Se pueden有多配置 diferentes `ProviderId` en el mismo array, por ejemplo, para conectar con recursos de Suecia y East-US respectivamente.

### 3. Iniciar WebAPI

```bash
dotnet run --project src/QianYuan.Api
# Escuchando en http://localhost:5050 (Swagger: /swagger)
```

### 4. Iniciar WebUI

```bash
cd src/QianYuan.Web
npm install
npm run dev
# Abrir en http://localhost:5173
```

El dev-server de Vite ya está configurado con proxy inverso: `/api` y `/hubs` se reenvían automáticamente al 5050.

### 4.1 Iniciar el contenedor de escritorio WorkPartner

Se recomienda usar los scripts rápidos del directorio raíz, que verificaran automáticamente las dependencias, inician el dev-server web y luego abren Electron:

```bash
./scripts/desktop-dev.sh
```

También se puede iniciar manualmente:

```bash
cd src/QianYuan.Desktop
npm install
npm run dev
```

El proceso principal de Electron inicia `QianYuan.Api`, y abrir la WebUI existente. El modo de lectura lee por defecto `http://127.0.0.1:5173`; para especificar la dirección frontend, establece `WORKPARTNER_RENDERER_URL`.

La capa de escritorio expone `window.workpartner` a través de preload: contiene información de ejecución, dirección de la API y una API de sistema de archivos local controlada.
Se permite el acceso por defecto al proyecto actual, al escritorio y al directorio de documentos; otros directorios requieren autorización explícita mediante el selector nativo de directorios. La API iniciada en escritorio 配置内置 FileSystem Skill 的沙箱根目录为当前仓库根目录，以便 Agent 像 Codex 一样读写本地工作区文件。
Más 的说明见 [docs/WORKPARTNER_DESKTOP.md](docs/WORKPARTNER_DESKTOP.md)。

### 5. Ejecutar ejemplo de consola

```bash
export QIANYUAN_APIKEY=sk-...
export QIANYUAN_BASEURL=https://api.openai.com/v1
export QIANYUAN_MODEL=gpt-4o-mini
dotnet run --project samples/QianYuan.Sample.Console
```

### 6. Ejecutar pruebas unitarias

```bash
dotnet test
```

## Abstracciones Principales (Conjunto Mínimo)

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    string DefaultModel { get; }
    LlmCapabilities Capabilities { get; }
    Task<ChatResponse> CompleteAsync(ChatRequest req, CancellationToken ct);
    IAsyncEnumerable<StreamingChunk> StreamAsync(ChatRequest req, CancellationToken ct);
}

public interface ISkill
{
    string Id { get; }
    ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct);
    ValueTask<SkillInvocationResult> InvokeAsync(string toolName, string argsJson, SkillInvocationContext ctx, CancellationToken ct);
}

public interface IAgent
{
    string Id { get; }
    IAsyncEnumerable<StreamingChunk> RunAsync(AgentRunRequest req, CancellationToken ct);
}
```

`StreamingChunk` es un evento de streaming unificado (TextDelta / ThinkingDelta / ToolCallStart / ToolCallArgsDelta /
ToolCallEnd / ToolObservation / Usage / End / Error / Warning),
los cuatro proveedores lo normalizan en este formato.

## Puntos Clave del Ciclo ReAct

`QianYuan.Kernel.ReAct.ReActEngine` en cada ronda:

1. Usa `ISkillManager.SelectRelevantAsync(intent, topK)` para seleccionar habilidadesivamente Skills.
2. Inyecta un harness estable y el estado del bucle a través de `LoopEngineeringRuntime`, y comprime mensajes antiguos cuando el contexto es muy grande.
3. Envía al LLM combinando las herramientas de los Skills seleccionados + otros Agentes registrados (como herramientas `agent.<id>`).
4. Recibe el salida del LLM:
   - Texto/Pensamiento → se reenvía directamente a la capa superior.
   - ToolCall (args en streaming) → se acumula y enruta a través de `IToolDispatcher` al Skill o Agente correspondiente.
   - Resultado de la herramienta → se agrega al historial como mensaje `ChatRole.Tool`, continúa a la siguiente ronda.
5. No hay ToolCall nuevo → termina, emite `End`.

En cada ronda se recalcula el conjunto de Skills activos, por lo que la "expansión progresiva" ocurre automáticamente.
El número máximo de iteraciones ReAct del Agente por defecto está controlado por `QianYuan.DefaultAgentMaxIterations`, por defecto `100`; una sola solicitud aún puede sobrescribirselo con `MaxIterations`.

### Loop Engineering

`QianYuan.Kernel.ReAct.LoopEngineeringOptions` se inspira en el enfoque de bucle/harness de Claude Code, elevando ReAct de un "simple bucle while tool-call" a un bucle backend controlado:

- **Harnes Prompt**: El prompt por defecto instruye al modelo para trabajar en inspeccionar → planificar → actuar → observar → verificar; antes de cada llamada a herramienta indica claramente el beneficio, y después de cada observación actualiza la estrategia basada证据.
- **Límite de Inyección de Prompt**: Define explícitamente las salidas de herramientas, páginas web, archivos y retornos de MCP como datos, no como nuevas instrucciones del sistema/desarrollador.
- **Estado del Bucle**: En cada prompt de sistema se inyecta la iteración actual, el máximo de iteraciones, el total de llamadas a herramientas y el uso de cada herramienta, para que el modelo sepa en qué ronda está y qué presupuesto le queda.
- **Compresión de Contexto**: Cuando el transcript excede `MaxTranscriptCharacters`, se comprime a un resumen continuo, conservando las últimas `MinRecentMessagesToKeep` mensajes, para reducir  de explosión de contexto en tareas largas.
- **Guardias de Herramientas**: Soporta un presupuesto total `MaxToolCalls` e intercepta llamadas repetidas a la misma herramienta, evitando reintentos ciegos, bucles infinitos o desperdicio de tokens.
- **Límites de Observación**: Limita la longitud del relleno de resultados de herramientas, evitando que una sola observación colapse el contexto; la UI aún ve un resumen legible truncado.
- **Sobrescritura a Nivel de Agente**: `ReActAgentDefinition.LoopEngineering` permite configurar diferentes estrategias de bucle para distintos Agentes, por ejemplo, Agentes de investigación con presupuesto presupuesto presupuesto presupuestos Agente de ejecución con 1 阈值.

Ruta completa del bucle:

```text
Mensajes del Usuario
  └─► SelectRelevantAsync(intent) Selección progresiva de Skills
       └─► LoopEngineeringRuntime.PrepareMessages
            ├─ 合并业务 SystemPrompt
            ├─ 注入 Loop harness 与循环状态
            ├─ 注入 active skill instructions
            └─ 必要时压缩旧上下文
                 └─► ILlmProvider.StreamAsync
                      ├─ TextDelta / ThinkingDelta → 透传给上层
                      └─ ToolCall → duplicate/budget guard
                           └─► IToolDispatcher.InvokeAsync
                                └─► bounded observation → ChatRole.Tool → 下一轮
```

Está habilitado por defecto; se puede sobrescribir al construir `ReActEngineOptions`:

```csharp
new ReActEngineOptions
{
    MaxIterations = 100,
    LoopEngineering = new LoopEngineeringOptions
    {
        MaxTranscriptCharacters = 80_000,
        MinRecentMessagesToKeep = 12,
        MaxObservationCharacters = 12_000,
        MaxToolCalls = 40,
        MaxConsecutiveIdenticalToolCalls = 1,
        HarnessPrompt = "your custom loop harness"
    }
}
```

También se puede sobrescribir a nivel de definición de Agente:

```csharp
new ReActAgentDefinition
{
    Id = "researcher",
    Name = "Research Agent",
    Description = "Long-running research agent",
    LoopEngineering = new LoopEngineeringOptions
    {
        MaxToolCalls = 80,
        MaxTranscriptCharacters = 120_000,
        MaxConsecutiveIdenticalToolCalls = 2
    }
}
```

Parámetros comunes:

| Parámetro | Valor por defecto | Propósito |
|------|--------|------|
| `Enabled` | `true` | Interruptor general; al desactivarse, vuelve a la construcción de mensajes ReAct estándar. |
| `AddHarnessPrompt` | `true` | Si inyecta el loop harness por defecto. |
| `IncludeLoopStateInPrompt` | `true` | Si inyecta el conteo de iteraciones y uso de herramientas. |
| `MaxTranscriptCharacters` | `80_000` | Si se superan estos caracteres, comprime transcript anterior. |
| `MinRecentMessagesToKeep` | `12` | Número de mensajes recientes a conservar al comprimir contexto. |
| `MaxObservationCharacters` | `12_000` | Longitud máxima de relleno de una sola observación de herramienta. |
| `MaxConsecutiveIdenticalToolCalls` | `1` | Número de veces permitidas para nombres de herramienta + parámetros JSON idénticos consecutivos. |
| `MaxToolCalls` | `null` | Presupuesto total de llamadas a herramientas en una sola ejecución; `null` significa sin límite adicional. |

Recomendaciones:

- **Agente por defecto**: Mantén la configuración por defecto; previene la mayoría de llamadas repetidas e inflación de contexto.
- **Agentes de Investigación/Recuperación**: Aumenta `MaxToolCalls` y `MaxTranscriptCharacters` para dejar más espacio de exploración.
- **Agentes de Ejecución en Producción**: Establece `MaxToolCalls` explícito y mantén `MaxConsecutiveIdenticalToolCalls = 1` para evitar ejecuciones repetidas e incontrolables.
- **Herramientas de Alto Riesgo**: Continúa implementando control de permisos e idempotencia en la capa de Skill/Dispatcher; Loop Engineering es gobernanza de bucles, no reemplaza la validación de seguridad a nivel de herramienta.

```json
{
  "QianYuan": {
    "DefaultAgentMaxIterations": 100
  }
}
```

## MCP

- **Como Cliente**: `services.AddMcpStdioServer(new McpStdioServerConfig { ServerId="fs", Command="npx", Arguments=["-y","@modelcontextprotocol/server-filesystem","/tmp"] })`.
  Después el inicio, `sp.MountMcpSkills()` monta todas las herramientas del Servidor MCP externo como un Skill llamado `mcp.fs`.
- **Como Servidor**: La WebAPI expone `POST /api/mcp`, proporcionando todas las herramientas de tu `SkillManager` a clientes MCP externos (Claude Desktop / Cursor, etc.) según el protocolo MCP.

## Búsqueda Web

```json
"WebSearch": {
  "Provider": "duckduckgo",
  "ApiKey": ""
}
```

- `duckduckgo` / `ddg` (por defecto): Rastrea el servicio sin clave de `html.duckduckgo.com`, listo para usar,
  ideal para desarrollo local y escenarios ligeros; DDG impone 限速，recomendado usar servicios de pago para cargas 负载.
- `tavily` / `bing` / `brave`: Propor相应平台的 `ApiKey`.
- Si `ApiKey` de cualquier proveedor está vacío, el marco recae automáticamente a DuckDuckGo.

## Sistema de Archivos de Skills y Registro de Extensiones

QianYuan soporta tres fuentes 来源: `ISkill` 代码实现的 `ISkill`, directorio de Markdown Skills, y herramientas expuestas por servidores MCP externos.
Todas estas entradas terminan en `ISkillManager`, participando en la selección progresiva con un manifiesto unificado; cuando se selecciona un Skill, sus herramientas entran en las herramientas del LLM,
y su `SystemPromptFragment` también se inyecta en el system prompt de la ronda actual.

Niveles de capacidad de Skills:

| Tipo | Capacidad | Uso Típico | Expone Herramientas |
|------|------|----------|--------------|
| Markdown Skill | Inyecta prompts de dominio según `SKILL.md` | code review, análisis de requisitos, especificaciones de API | No |
| Skill Integrado | Búsqueda web, visión, sistema de archivos, ejecución de scripts | Consulta en línea, comprensión de imágenes,读写沙箱, ejecución 代码片段 | Sí |
| `ISkill` Personalizado | Cualquier herramienta de negocio o integración | Llamada a servicios internos, orquestación de flujos, 专有数据查询 | Sí |
| Skill MCP | Llama a herramientas de servidores MCP externos | filesystem, browser, database,生态 de terceros de terceros | Sí |

La ejecución de scripts es proporcionada por el Skill integrado `qianyuan.code`, y las llamadas a servidores MCP se adaptan como un grupo de herramientas con nombre `McpSkill`.

### Sistema de Archivos Markdown Skill

Puedes montar directorios de `Skill.md` / `SKILL.md` comunes de Claude/Copilot/Cursor en QianYuan. El marco lee los campos
`name`, `description`, `tags`, `id`, etc. en el frontmatter YAML, se registran como Skills progresivos; cuando se selecciona ese Skill, el cuerpo Markdown se inyecta como sistema prompt.

Convención de directorios:

- Cada Skill 一 目录，put `SKILL.md` o `Skill.md` inside.
- `Recursive = true` escanea subdirectorios recursivamente, ideal para montar repositorios de skills de agent ya existentes.
- `id` se puede declarar explícitamente en frontmatter; si no se declara, se genera un ID estable usando `IdPrefix + 相对目录`.
- Cuando hay IDs duplicados en el mismo directorio de montaje, los duplicados posteriores se saltan y registran un warning.
- Los Skills Markdown son Skills de tipo "prompt", `ApproximateToolCount = 0`, no exponen llamadas a herramientas directamente.

Campos de frontmatter soportados:

| Campo | Propósito | Notas |
|------|------|------|
| `id` | Identificador único del Skill | Opcional; se normaliza a ID con puntos minúsculas |
| `name` / `title` | Nombre de visualización del Skill | Recurre al nombre del directorio si falta |
| `description` / `summary` | Descripción para selección progresiva | Recurre a la primera línea del cuerpo si falta |
| `tags` / `keywords` / `categories` | Etiquetas de recuperación检索 | Soporta `[a, b]` o lista YAML |

Estructura de ejemplo:

```text
skills/
  code-review/
    SKILL.md
  pdf/
    Skill.md
```

Ejemplo `SKILL.md`:

```markdown
---
id: sample.code-review
name: code-review
description: Review code for bugs, regressions, and missing tests
tags: [review, testing]
---

# Code Review

Prioritize correctness issues before style comments.
```

### Carga dinámica动态加载目录

Declare los directorios de Skills a montar en `QianYuan.SkillDirectories`:

```json
{
  "QianYuan": {
    "SkillDirectories": [
      {
        "Path": "./.agents/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "agent"
      },
      {
        "Path": "./samples/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "sample"
      },
      {
        "Path": "/Users/you/.agents/skills",
        "Recursive": true,
        "Enabled": true,
        "IdPrefix": "agent"
      }
    ]
  }
}
```

El repositorio incluye un directorio de Skills a nivel de proyecto descargado de [skills.sh](https://skills.sh/), y un directorio de ejemplo que se puede cargar dinámicamente:

```text
.agents/skills/
  brainstorming/SKILL.md
  brainstorm/SKILL.md
  find-skills/SKILL.md
  pdf/SKILL.md
  skill-creator/SKILL.md
  summarize/SKILL.md
  using-superpowers/SKILL.md
```

Estas habilidades se instalan vía 官方 Skills CLI, y se registran en `skills-lock.json` con la fuente y el hash de contenido; se pueden restaurar o actualizar con:

```bash
npx skills experimental_install
npx skills update -p -y
```

Fuentes de descarga integradas actualmente:

| Skill | Fuente | Descripción |
|-------|------|------|
| `using-superpowers` / `brainstorming` | `archieindian/openclaw-superpowers` | Flujos de Superpowers y proceso de brainstorming |
| `brainstorm` | `buiducnhat/agent-skills` | Flujos ligeros de brainstorming y convergencia de soluciones |
| `find-skills` | `vercel-labs/skills` | Descubrir e instalar habilidades desde skills.sh |
| `skill-creator` / `pdf` | `anthropics/skills` | Crear/optinizar Skills, y lectura/procesar PDFs |
| `summarize` | `sjunepark/custom-skills` | Resumen de URL, archivos locales, media, etc. |

Estructura de ejemplo:

```text
samples/skills/
  api-design/SKILL.md
  code-review/SKILL.md
  debugging/SKILL.md
  docs-writing/SKILL.md
  requirements-analysis/SKILL.md
```

`appsettings.json` por defecto ya habilita `./.agents/skills` y `./samples/skills`. Tras iniciar la API, puedes verificar si estos Skills se registraron correctamente mediante `GET /api/skills`.

Al iniciar la API, se ejecuta:

```csharp
app.Services.RegisterMarkdownSkillsFromDirectories(qy.SkillDirectories.Select(d => new MarkdownSkillDirectoryOptions
{
  Path = d.Path,
  Recursive = d.Recursive,
  Enabled = d.Enabled,
  IdPrefix = d.IdPrefix,
}));
```

### Registro de Skills de Código

Cuando se necesitan capacidad real de herramienta, implementa `ISkill`, proporcionando propiedades de manifiesto, definiciones de herramientas y lógica de llamadaación:

```csharp
public sealed class MySkill : ISkill
{
  public string Id => "my.skill";
  public string Name => "My Skill";
  public string Description => "Does one focused job.";
  public IReadOnlyList<string> Tags => ["custom"];
  public string? SystemPromptFragment => "Use this skill only when the task matches its description.";

  public ValueTask<IReadOnlyList<ToolDefinition>> GetToolsAsync(CancellationToken ct = default)
    => ValueTask.FromResult<IReadOnlyList<ToolDefinition>>([
      new ToolDefinition(
        "my_tool",
        "Run my custom operation.",
        "{\"type\":\"object\",\"properties\":{}}")
    ]);

  public ValueTask<SkillInvocationResult> InvokeAsync(
    string toolName,
    string argumentsJson,
    SkillInvocationContext context,
    CancellationToken ct = default)
    => ValueTask.FromResult(SkillInvocationResult.Ok("{\"ok\":true}"));
}
```

Hay dos formas de registrarlo.

Registro a través de DI, ideal para el inicio de aplicaciones estándar:

```csharp
builder.Services.AddSingleton<ISkill, MySkill>();
// Después app.Build(), monta todos en SkillManager
app.Services.RegisterSkillsFromServices();
```

Registro directo en `ISkillManager`, ideal para gestión en tiempo de ejecución, descubrimiento de plugins o pruebas:

```csharp
var manager = app.Services.GetRequiredService<ISkillManager>();
manager.Register(new MySkill());
```

Si la inicialización del Skill es costosa, también se puede registrar solo un manifiesto liviano + factory, materializando al primer hit:

```csharp
manager.Register(
  new SkillManifest(
    "my.lazy-skill",
    "Lazy Skill",
    "Loads resources only when selected.",
    ["custom", "lazy"],
    ApproximateToolCount: 1,
    RequiresNetwork: false,
    RequiresFilesystem: false),
  sp => new MySkill());
```

### Skill de Ejecución de Scripts

El Skill de Ejecución de Código integrado expone la herramienta `code_run` para ejecutar scripts cortos en un directorio sandbox. Desactivado por defecto, necesita habilitación explícita:

```json
{
  "QianYuan": {
    "CodeExecution": {
      "Enabled": true,
      "SandboxDirectory": "./_sandbox/code",
      "AllowedRuntimes": ["python", "node"],
      "TimeoutSeconds": 20
    }
  }
}
```

Tras habilitarlo, el proceso de inicio registra:

```csharp
builder.Services.AddCodeExecutionSkill(new CodeExecutionOptions
{
    SandboxDirectory = cx.SandboxDirectory,
    AllowedRuntimes = new HashSet<string>(cx.AllowedRuntimes, StringComparer.OrdinalIgnoreCase),
    PerCallTimeout = TimeSpan.FromSeconds(cx.TimeoutSeconds),
});
```

Protocolo de la herramienta:

```json
{
  "runtime": "python",
  "code": "print(1 + 1)"
}
```

Los runtimes soportados actualmente están controlados por `AllowedRuntimes`, las implementaciones integradas soportan `python`, `node`, `bash`; se recomienda en entornos de producción solo permitir 允许必要的 runtimes y pointing `SandboxDirectory` a un directorio aislado.

### Registro de MCP Skills

Un servidor MCP externo puede montarse como Skill. Tras configurar un servidor stdio, invocar `MountMcpSkills()` al inicio adapta cada cliente MCP a un `McpSkill`:

```json
{
  "QianYuan": {
    "McpServers": [
      {
        "ServerId": "fs",
        "Command": "npx",
        "Arguments": ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
        "Environment": {}
      }
    ]
  }
}
```

```csharp
builder.Services.AddMcpStdioServer(new McpStdioServerConfig
{
  ServerId = "fs",
  Command = "npx",
  Arguments = ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
});

app.Services.MountMcpSkills();
```

Tras el montaje, el ID del Skill es `mcp.<serverId>`, los nombres de herramientas se prefijan uniformemente como `mcp.<serverId>.<toolName>` para evitar conflictos entre servidores servidores MCP. La lista de herramientas se obtiene bajo demanda a través de `ListTools` MCP cuando el Skill se requiere 需要, y las llamadasaciones se reenvían al `CallTool` correspondiente del servidor MCP.

### Ciclo de Vida del Registro

En el flujo de inicio, el orden de registro de Skills es:

1. `RegisterSkillsFromServices()`: Monta Skills integrados y `ISkill` personalizados registrados vía DI.
2. `RegisterMarkdownSkillsFromDirectories(...)`: Carga dinámicamente `SKILL.md` / `Skill.md` según directorios configurados.
3. `MountMcpSkills()`: Monta herramientas de servidores MCP externos como Skills.

Tras el registro, `GET /api/skills` muestra el catálogo; ReAct llamar 每轮调用 `SelectRelevantAsync(intent, topK)` para seleccionar los Skills más relevantes actualmente 当前轮次.

Estos Markdown Skills son de "tipo prompt", no ejecutan comandos externos ni exponen llamadas a herramientas; se recomienda implementar `ISkill` o montar vía MCP para capacidades reales 真实工具能力.

## Agent Store: Tienda de Agentes Empresariales

Agent Store es la entrada de orquestación visual y ejecución para la落地 de Agentes empresariales de QianYuan, diseñada para consolidar "capacidades reutilizables" en Agentes gestionables, testeables y listos para producción.
No es una simple lista de prompts, sino que combina Perfil de Agente, Proveedor de Modelos, System Prompt, Skills, Servidores MCP y CLI Services en un Agente completo.

### Capacidades Principales

- **Gestión de Perfiles de Agente**: Crear, editar, eliminar Agentes, manteniendo `id`, nombre, descripción, Proveedor por defecto, Modelo por defecto y system prompt.
- **Orquestación de Skills**: Seleccionar capacidades de Skills Markdown, Skills integrados, `ISkill` personalizados ya registrados en `ISkillManager`, y montarlos en un Agente específico con prioridad.
- **Integración de Servidores MCP**: Asociar servidores 单个 Agente con servidores MCP externos, incorporando capacidades de sistema de archivos, navegador, base de datos, herramientas de terceros, etc. a la lista de herramientas.
- **Integración de CLI Services**: Envolver servicios HTTP internos o APIs de terceros como herramientas de Agente a través de UnifyCli, soportando autenticación, mapeo de parámetros y transformación de respuestas.
- **Inventario de Herramientas y Prueba Individual**: Ver todas las herramientas agregadas de un Agente, y probar individualmente con parámetros JSON.
- **Prueba de Interacción de Agente**: Iniciar conversación directamente con un Agente específico en la WebUI, verificando la sinergia de modelo, prompt y cadena de herramientas.

### Uso de WebUI

Tras iniciar la WebUI, accede a **Agent Store** en la interfaz:

1. Haz clic en "Nuevo Agente", completa ID único, nombre, descripción, Proveedor, modelo y system prompt.
2. En la pestaña **Skills**, monta Skills existentes, establece 设置 priority 控制优先级.
3. En la pestaña **MCP**, añade Servidores MCP, como servicios de herramientas de filesystem, browser, database, etc.
4. En la pestaña **CLI**, agrega servicios HTTP expuestos a través de UnifyCli.
5. En la pestaña **Test**, verifica la lista de herramientas, prueba una herramienta individual, o conversa conversación una conversación con el Agente.

### API de Agent Store

Exponga uniformemente en `/api/agent-store`:

| Método | Ruta | Descripción |
|------|------|------|
| `GET` | `/api/agent-store` | Obtener lista de Agentes |
| `GET` | `/api/agent-store/{agentId}` | Obtener detalles de Agente especifico |
| `POST` | `/api/agent-store` | Crear Agente |
| `PUT` | `/api/agent-store/{agentId}` | Actualizar Agente |
| `DELETE` | `/api/agent-store/{agentId}` | Eliminar Agente |
| `POST` | `/api/agent-store/{agentId}/skills` | Montar Skill en Agente |
| `DELETE` | `/api/agent-store/{agentId}/skills/{skillRowId}` | Quitar Skill montado |
| `POST` | `/api/agent-store/{agentId}/mcp-servers` | Asociar Servidor MCP |
| `DELETE` | `/api/agent-store/{agentId}/mcp-servers/{serverRowId}` | Quitar Servidor MCP |
| `POST` | `/api/agent-store/{agentId}/cli-services` | Asociar CLI Service |
| `DELETE` | `/api/agent-store/{agentId}/cli-services/{serviceRowId}` | Quitar CLI Service |
| `GET` | `/api/agent-store/{agentId}/tools` | Obtener inventario de herramientas agregadas |
| `POST` | `/api/agent-store/{agentId}/test-tool` | Prueba individual de herramienta a herramienta |
| `POST` | `/api/agent-store/{agentId}/interact` | Prueba de interacción con Agente |

Ejemplo mínimo de solicitud para crear Agente:

```json
{
  "id": "sales-assistant",
  "name": "Asistente de Ventas",
  "description": "面向售前方案、客户问答和商机跟进的企业智能体",
  "defaultProviderId": "openai",
  "defaultModel": "gpt-4o-mini",
  "systemPrompt": "Eres un asistente de ventas empresarial 专业、稳健的企业销售助手。"
}
```

### Posicionamiento de Diseño

Agent Store es ideal para alojar un "mercado interno 企业内部智能体市场”: I+D, Ventas, Operaciones, Soporte, Finanzas, etc. pueden consolidarse en Agentes independientes;
cada Agente combina sus límites de herramientas a través de Skills/MCP/CLI, facilitando reutilización y permitiendo posteriores posterior de permisos, auditoría, estado de lanzamiento, gestión de versiones y aislamiento multi-tenancy.

## UnifyCli: Envoltorio Unificado para Servicios HTTPS

### Resumen

`QianYuan.UnifyCli` es un framework genérico para envolver cualquier servicio HTTPS o REST API como métodos CLI,
integrándolos sin fisuras con el sistema de Skills en Agentes. Resuelve el problema "¿cómo 如何让 Agent 快速调用第三方 API"?

**Escenarios Clave**:
- Integración de APIs de terceros como GitHub / Slack / OpenAI
- Envolver microservicios internos para que los Agentes los llamen
- Agregación de datos (múltiples APIs → interfaz unificada)
- Implementación de API Gateways y proxies genéricos

### Arquitectura

```
Solicitud del Agente
    ↓
CliServiceSkill (Adaptador de Skill)
    ↓
CliService (Registro de Métodos)
    ↓
UnifyHttpClient (Ejecutor HTTP)
    ├─ Interpolación de Parámetros (path, query, body)
    ├─ Autenticación (Basic/Bearer/ApiKey/Custom)
    ├─ Retry & Timeout
    └─ Transformación de Respuesta
    ↓
Servicio HTTPS Externo / REST API
```

### Inicio Rápido

#### 1. Configuración DI

```csharp
builder.Services.AddUnifyCli();
```

#### 2. Definir Servicio CLI

```csharp
using QianYuan.UnifyCli.Implementation;
using System.Text.Json;

var userService = new CliServiceDefinition
{
    Id = "user.api",
    Name = "User Service",
    Description = "API for user management",
    BaseUri = "https://api.example.com"
};
```

#### 3. Definir Método CLI

```csharp
var getUserMethod = new CliMethodDefinition
{
    Id = "get_user",
    Name = "Get User",
    Description = "Get user info by ID",
    HttpMethod = "GET",
    PathTemplate = "/v1/users/{userId}",
    ParametersSchema = JsonSerializer.Serialize(new
    {
        type = "object",
        properties = new
        {
            userId = new { type = "string", description = "User ID" }
        },
        required = new[] { "userId" }
    }),
    Tags = new[] { "user", "profile" }
};

userService.RegisterMethod(getUserMethod);
```

#### 4. Registrar Servicio

```csharp
builder.Services.AddCliService(userService);
```

#### 5. UsoAgent 中使用

```csharp
// 自动作为 Skill 暴露给 Agent
var skillFactory = sp.GetRequiredService<CliServiceSkillFactory>();
var skill = await skillFactory.CreateSkillAsync("user.api");

// 现在 Agent 可以调用 "get_user" 工具
```

### Soporte de Autenticación

UnifyCli soporta 5 tipos de autenticación listos para usar:

| Método | Propósito | Configuración |
|------|------|------|
| **No Auth** | APIs públicas | `type: "none"` |
| **Basic** | Usuario/Contraseña | `type: "basic"`, `username`, `password` |
| **Bearer** | JWT / Token OAuth2 | `type: "bearer"`, `token` |
| **API Key** | Header o Query param | `type: "api_key"`, `token`, `headerName` o `queryParamName` |
| **Custom** | Header personalizado | `type: "custom"`, `headers: {...}` |

Ejemplo:

```csharp
var authFactory = new AuthenticationProviderFactory();
var authOptions = new AuthenticationOptions
{
    Type = "bearer",
    Token = "eyJhbGciOiJIUzI1NiIs..."
};

service.DefaultAuthenticationProvider = authFactory.Create(authOptions);
```

### Manejo de Parámetros

UnifyCli maneja automáticamente tres mapeos de parámetros:

#### Parámetros de Ruta

```csharp
PathTemplate = "/v1/users/{userId}/posts/{postId}"
// Al invocar: InvokeAsync("...", @"{""userId"":""123"",""postId"":""456""}")
// Convertido a: GET /v1/users/123/posts/456
```

#### Parámetros de Query

```csharp
QueryParams = new Dictionary<string, string>
{
    { "limit", "$limit" },      // Toma limit de los parámetros
    { "sort", "date" }          // Valor fijo
}
// Al invocar: InvokeAsync("...", @"{""limit"":10}")
// Convertido a: GET /v1/items?limit=10&sort=date
```

#### Body de Solicitud

```csharp
RequestBodyTemplate = "$."  // Todo el parámetro como body JSON
// o
RequestBodyTemplate = "$.user"  // Solo toma el campo user de los parámetros
```

### Transformación de Respuesta

Si una API externa devuelve estructuras complejas, puedes extraer campos específicos con JsonPath:

```csharp
ResponseTransformer = new JsonPathResponseTransformer("$.data.items")
// Respuesta original: { "data": { "items": [...] }, "meta": {...} }
// Transformada: [...]
```

### Ejemplo Completo

Ver [samples/QianYuan.Sample.Console/UnifyCliIntegrationExample.cs](samples/QianYuan.Sample.Console/UnifyCliIntegrationExample.cs),
contiene 7 escenarios: uso básico, autenticación, descubrimiento de registro, integración DI, integración de Skills, manejo de errores, configuración de aplicación completa.

También se pueden ver los servicios de ejemplo integrados:

- `WeatherServiceExample`: API OpenWeatherMap (GET + autenticación)
- `GitHubServiceExample`: API REST de GitHub (múltiples endpoints + auth Bearer)
- `SlackServiceExample`: API de Slack (POST + body JSON)

### Documentación Detallada

| Documento | Descripción |
|------|------|
| [README.md](src/QianYuan.UnifyCli/README.md) | Referencia completa de API, opciones de configuración, mejores prácticas, consideraciones de seguridad |
| [QUICKSTART.md](src/QianYuan.UnifyCli/QUICKSTART.md) | Inicio rápido en 5 minutos, patrones comunes, FAQ |
| [ARCHITECTURE.md](src/QianYuan.UnifyCli/ARCHITECTURE.md) | Arquitectura del sistema, flujodata flow, diseño de extensiones |

## DingTalk

1. Crear un bot personalizado, obtener URL de webhook outgoing + secret de firma.
2. Configurar campos como `QianYuan.DingTalk.Enabled = true` en `appsettings.json`.
3. Configurar la dirección de callback `https://<your-host>/api/dingtalk/webhook` en el bot de DingTalk.
4. El marco validará la firma, pasará al Agente por defecto, y empujará el texto en streaming por Markdown cíclicamente de vuelta.

## Licencia

Este proyecto se abre bajo la [Apache License 2.0](./LICENSE). Copyright © 2026 QianYuan Team.
