# ResidApp - Arquitectura de Inteligencia Artificial (Especificación Final)

> **Documento de referencia, adaptado por el [ADR 0011](decisiones-arquitectura/0011-arquitectura-ia.md) (2026-10-23).** Lo que no
> recoja ese ADR no está aprobado. En particular, los casos de uso 1, 3 y 4, el tool calling sobre el residente y el roadmap de proveedores
> no se siguen: chocan con el alcance del producto y con el ADR 0009.

## Visión General
ResidApp incorpora capacidades de Inteligencia Artificial mediante una arquitectura **AI-Orchestrated** y **AI-Agnostic**, diseñada para evitar dependencias tecnológicas con cualquier proveedor específico de modelos de lenguaje (LLM).

La plataforma debe poder utilizar indistintamente OpenAI, Azure OpenAI, Anthropic Claude, Google Gemini, Mistral, Ollama o modelos locales sin modificar la lógica de negocio. La IA se considera una infraestructura reemplazable, no una dependencia del dominio.

---

# Objetivos
- Desacoplar la lógica de negocio de cualquier proveedor de IA.
- Permitir cambiar de modelo mediante configuración en caliente.
- Reducir costes futuros de migración.
- Facilitar despliegues cloud y on-premise.
- Centralizar control, auditoría y observabilidad.
- Preparar la aplicación para RAG, agentes y MCP.

---

# Principios Arquitectónicos

## AI-First pero AI-Agnostic
- Ninguna funcionalidad dependerá de un proveedor o modelo concreto.
- Los proveedores podrán sustituirse mediante configuración sin necesidad de recompilar.
- La lógica de negocio permanecerá completamente inalterada ante cambios en las APIs de IA.

---

# Arquitectura de Alto Nivel

```text
Frontend ASP.NET MVC
        |
Application Services
        |
   AI Orchestrator
        |
 -----------------------
 |         |          |
Prompt     RAG      Tools
Manager
        |
    AI Gateway
        |
 -------------------------
 |      |      |        |
Claude OpenAI Gemini Ollama
```

---

# Estructura de la Solución

```text
ResidApp
│
├── Domain
├── Application
├── Infrastructure
├── AI
│   ├── Abstractions
│   ├── Gateway
│   ├── Providers
│   ├── Prompts (Archivos .md/.txt embebidos y versionados en Git)
│   ├── RAG
│   └── Tools
│
└── Web
```

---

# Componentes Principales

## AI Orchestrator
Componente responsable de coordinar todas las operaciones relacionadas con IA y el flujo de negocio.
- Construcción de contexto dinámico.
- Selección y formateo de prompts.
- Invocación de estrategias RAG.
- Orquestación y ejecución de herramientas (Tool Calling).
- Selección del proveedor de IA idóneo según la tarea.
- Control de auditoría, trazabilidad y observabilidad de alto nivel.

## AI Gateway
Punto único de acceso técnico a cualquier modelo (infraestructura).
- Gestión e intercambio dinámico de proveedores.
- Políticas de resiliencia (Gestión de errores, reintentos y Circuit Breaker).
- Logging centralizado, telemetría y control de costes por tokens.
- Capa de caché para respuestas repetitivas.

---

# Gestión de Prompts
Los prompts son artefactos estáticos versionados de forma nativa en el repositorio de código.
- Se almacenan como archivos `.txt` o `.md` embebidos en el ensamblado.
- Permite a Claude Code e ingenieros iterar sobre ellos de forma segura mediante Pull Requests y Git.

---

# Abstracciones IA (Contratos C#)

## Chat
```csharp
public interface IChatProvider
{
    Task<string> GenerateTextAsync(string prompt, CancellationToken cancellationToken = default);
}
```

## Structured Output
```csharp
public interface IStructuredGenerationProvider
{
    // Garantiza compatibilidad con OpenAI Structured Outputs o esquemas estrictos de Anthropic
    Task<T> GenerateStructuredAsync<T>(string prompt, object jsonSchema = null, CancellationToken cancellationToken = default) where T : class;
}
```

## Embeddings
```csharp
public interface IEmbeddingProvider
{
    Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
```

---

# Arquitectura RAG (Retrieval-Augmented Generation)

## Flujo de Datos
```text
Usuario -> Pregunta -> Retriever -> Embedding Service -> Vector Store -> Context Builder -> LLM -> Respuesta
```

## Fuentes de Datos Integradas
- **SQL Server:** Datos relacionales (Residentes, diagnósticos, escalas clínicas, medicación).
- **Documentación:** PDFs estáticos, informes clínicos institucionales, protocolos del centro.
- **Texto Libre:** Notas de evolución diaria, observaciones manuales de enfermería y anotaciones de CJ.

## Motor Vectorial
- **Recomendación Inicial:** **SQL Server Vector Search** (evita añadir infraestructura externa en fases tempranas).
- **Nota Técnica:** Las consultas de similitud del coseno se ejecutarán mediante SQL Nativo (Raw SQL) o extensiones específicas, previniendo limitaciones temporales en el mapeo nativo de ORMs como Entity Framework Core.

---

# Tool Calling (Ejecución de Herramientas)
ResidApp habilita al LLM a consumir datos en tiempo real bajo demanda mediante definiciones de herramientas nativas.

## Herramientas Base:
- `ObtenerResidente(int id)`
- `ObtenerMedicacionActual(int residenteId)`
- `ObtenerUltimasEscalas(int residenteId)`

---

# Casos de Uso Críticos
1. **Resumen Clínico:** Consolidación automatizada de anotaciones libres en informes semanales estructurados.
2. **Extracción de Datos Estructurados:** Conversión de texto libre médico a formatos estructurados relacionales.
3. **Asistente Inteligente (RAG):** Resolución de consultas complejas sobre el histórico del residente.
4. **Detección de Riesgos:** Alertas preventivas sobre polimedicación, interacciones de fármacos y riesgo latente de caídas. *La decisión final siempre corresponderá a un profesional sanitario.*

---

# Roadmap Tecnológico
- **Fase 1 (MVP):** Claude Sonnet (Razonamiento) + OpenAI Embeddings + SQL Server Vector Search. Prompts en Git.
- **Fase 2:** Integración total de RAG + Tool Calling para datos clínicos dinámicos.
- **Fase 3:** Compatibilidad con Model Context Protocol (MCP) y agentes autónomos.
- **Fase 4:** Arquitectura Multiagente especializada (Clínico, Documental, Farmacológico).

---

# Seguridad y RGPD
- **Minimización de Datos:** Filtrado y anonimización de datos sensibles de carácter identificativo directo (DNI, nombres completos, teléfonos) antes de su envío a APIs externas.
- **Privacidad por Diseño:** Trazabilidad completa de qué datos se procesan y almacenamiento local (On-Premise / Ollama) mapeado para datos extremadamente confidenciales de ser requerido.