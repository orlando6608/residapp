# ADR 0011 — Arquitectura de la IA: independiente del proveedor, con un solo caso de uso aprobado

Estado: aceptada el 2026-10-23 (decisión del usuario). Implementada ese mismo día en el tramo 1 de la Fase B del dictado (ADR 0009): los dos puertos
(`Application/Ports/IVoiceDictation.cs`), el adaptador de Mistral (`Infrastructure/IA/MistralClient.cs`, con un reintento y registro sin contenido), la
lista de proveedores autorizados (`IaOptions.Validate`, que impide arrancar con otro), el prompt incrustado y el esquema generado desde el dominio
(`DailyChangeDictationSchema`). El uso se audita antes de enviar el audio (`VOICE_DICTATION_USED`) y, sin `IA:Mistral:ApiKey`, el dictado queda desactivado.

## Contexto

Orlando hizo en algún momento un documento de propuesta de arquitectura de IA, antes de que se decidiera nada sobre ella en el proyecto. Este
ADR recoge lo aprovechable y se eliminó el documento; sigue en el historial de git (se añadió en el commit `42dc740`, como
`docs/RESIDAPP_AI_SPEC_FINAL.md`). La propuesta planteaba:

- que el código de la aplicación no dependa de ningún proveedor de modelos de lenguaje (en lenguaje llano, *proveedor* es la empresa que
  ofrece el modelo por internet, como Mistral u OpenAI);
- una pasarela única hacia los modelos;
- prompts versionados en git (el *prompt* es el texto de instrucciones que se le da al modelo);
- RAG, tool calling, MCP y agentes;
- cuatro casos de uso: resumen clínico, extracción estructurada, asistente sobre el historial y detección de riesgos.

Al contrastarla con lo construido y lo decidido, la parte de **estructura** encaja y mejora el plan del dictado. La parte de **producto**
(los casos de uso y el roadmap de proveedores) choca con el alcance que fija CJ y con los ADR 0009 y 0010. Este ADR adopta lo primero y deja
lo segundo descartado o aplazado, con su motivo. **Lo que no recoja este ADR no está aprobado.**

## Decisión

### Se adopta

| Pauta | Cómo se aplica en ResidApp |
|---|---|
| La lógica de negocio no depende del proveedor | Puertos en `Application/Ports` (un *puerto* es una interfaz que dice qué se necesita sin decir quién lo hace). Los casos de uso solo conocen los puertos; el adaptador de cada proveedor vive en `Infrastructure/IA/`. |
| Modelo elegible por configuración | Sección `IA` de la configuración: proveedor y modelo por tarea (transcripción, salida estructurada). |
| Pasarela única hacia los modelos | Un `HttpClient` tipado por proveedor con tiempo de espera y **un** reintento solo ante errores transitorios. Registra modelo, tokens, segundos de audio y duración, **nunca el contenido**. |
| Prompts versionados en git | Un fichero `.md` por tarea en `Infrastructure/IA/Prompts/`, incrustado en el ensamblado. Se cambian por pull request como el código. |
| Salida estructurada con esquema | El modelo contesta con un esquema estricto (una plantilla que solo admite los campos y valores permitidos). El esquema se genera desde los enums del dominio, no a mano. Lo que devuelve es una **sugerencia**: se valida en servidor como cualquier entrada. |
| Auditoría y trazabilidad central | Cada uso se audita sin datos (para el dictado, `VOICE_DICTATION_USED`, ADR 0009). |
| Minimización de datos | Al proveedor no va nada que identifique al residente. Ni el audio ni la transcripción se guardan. |
| Caso de uso «extracción estructurada de texto libre» | Es el dictado del ADR 0009 y **el único caso de uso aprobado**. |

**Contratos.** Las firmas de la propuesta se ajustan al proyecto: identificadores `Guid`, tipos que admiten nulo marcados como tales y
un esquema obligatorio.

```csharp
public interface ISpeechTranscriber
{
    Task<string> TranscribeAsync(Stream audio, string contentType, string language, CancellationToken cancellationToken);
}

public interface IStructuredGenerator
{
    // promptName: fichero de Infrastructure/IA/Prompts. Devuelve null si el modelo no contesta con el esquema.
    Task<T?> GenerateAsync<T>(string promptName, string input, JsonObject schema, CancellationToken cancellationToken) where T : class;
}
```

`IChatProvider` (texto libre) e `IEmbeddingProvider` no se crean: no hay ningún caso de uso aprobado que los use.

**Proveedores autorizados.** Ser independiente del proveedor **no significa tener varios**. Se construye un solo adaptador (Mistral, ADR
0009). La configuración solo admite proveedores de una lista autorizada, y la aplicación **no arranca** si el configurado no está en ella.
Añadir uno a la lista es un cambio legal (contrato de encargado de tratamiento, región donde procesa, EIPD) y exige un ADR: no basta con
cambiar un ajuste.

**Sin proyecto `AI` aparte por ahora.** La IA sigue el patrón de `Pdf/` e `Intercambio/` dentro de `Infrastructure`. Se separará en un
proyecto propio si llega un segundo caso de uso.

### Se descarta (decisión de producto: solo CJ puede reabrirlo)

- **Resumen clínico, asistente sobre el historial (RAG) y detección de riesgos** (polimedicación, interacciones, caídas). Chocan con:
  - `docs/producto/vision-general.md`: la plataforma no diagnostica ni decide prioridades clínicas;
  - `docs/producto/alcance.md`: dejan fuera de alcance el triaje y las recomendaciones automáticas;
  - el criterio de 2026-10-21: la IA no lee datos de residentes ni redacta, resume, prioriza o hace triaje.

  Un sistema que detecta riesgos clínicos podría además ser producto sanitario y, con ello, IA de alto riesgo en el Reglamento de IA. Está
  sin verificar: habría que consultarlo antes de plantearlo.
- **Tool calling sobre el residente** (`ObtenerResidente`, `ObtenerMedicacionActual`, `ObtenerUltimasEscalas`). *Tool calling* es dejar que el
  modelo pida datos a la aplicación. Enviaría datos identificativos y clínicos al proveedor. Además, ResidApp no guarda medicación (su
  administración está fuera de alcance).
- **«AI-First».** Sigue vigente el criterio del ADR 0010: la IA solo se usa donde las reglas deterministas no bastan.
- **Caché de respuestas.** Guardaría datos de salud, y en el dictado no hay dos peticiones iguales.
- **Anonimizar antes de enviar como garantía principal.** La voz ya es un dato personal y no se puede anonimizar. Lo que la protege es el
  contrato y la configuración del proveedor (ADR 0009).

### Se aplaza (puertas abiertas)

1. **Otros proveedores** (Claude, OpenAI, Gemini, modelos locales con Ollama). Cada uno necesita su ADR, contrato y EIPD. Un modelo local, sin
   tercero en el tratamiento, sería el candidato natural si algún día hiciera falta.
2. **Embeddings, búsqueda vectorial y RAG.** En lenguaje llano: *embedding* es convertir un texto en una lista de números para buscar textos
   parecidos; *RAG* es dar al modelo documentos encontrados así antes de que conteste. Si se usaran para algo no clínico (por ejemplo, buscar
   en los protocolos del centro o en el manual), el motor sería la búsqueda vectorial de SQL Server y las consultas irían en SQL con Dapper.
3. **MCP, agentes y multiagente.** *MCP* es un estándar para conectar modelos con herramientas externas; un *agente* es un modelo que
   encadena acciones por su cuenta. No tienen caso de uso: el alcance excluye automatizar decisiones.

Abrir una puerta exige, en este orden: un caso de uso que lo necesite y que las reglas no resuelvan; el visto bueno de CJ si toca datos o
decisiones clínicas; el contrato y la EIPD del proveedor; y un ADR nuevo.

## Consecuencias

- El dictado (ADR 0009, Fase B) se construye con `ISpeechTranscriber` e `IStructuredGenerator`, el adaptador de Mistral en
  `Infrastructure/IA/`, prompts incrustados, la lista de proveedores autorizados y una pasarela con resiliencia y registro sin contenido.
- La prueba de precisión (`tools/PruebaDictado`) sigue siendo válida. Lee el prompt de un fichero en lugar de tenerlo en el código.
- El roadmap de la propuesta (Fase 1 con Claude Sonnet y embeddings de OpenAI) no se sigue: el proveedor es Mistral.
