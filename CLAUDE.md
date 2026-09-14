# Guía de trabajo compartida — ResidApp

Este documento recoge las reglas de trabajo que rigen cualquier colaboración sobre este repositorio, sea con Claude, con ChatGPT o con cualquier otra herramienta o persona. Vive en el repositorio (no en la configuración privada de una herramienta concreta) para que se comparta automáticamente entre todas las sesiones de trabajo de cualquier colaborador del proyecto.

## Reglas de trabajo

- Prioriza corrección sobre velocidad. Verifica siempre; nunca asumas que funciona.
- Usa el mínimo código necesario; evita abstracciones o complejidad especulativa.
- Cambia solo lo imprescindible; no refactorices código no relacionado. Sigue el estilo existente.
- Si detectas problemas fuera del alcance, menciónalos, no los corrijas.
- Ante ambigüedad relevante, declara tus suposiciones; pregunta solo si el error es difícil de revertir.

## Dónde está el resto del contexto del proyecto

- `docs\decisiones-arquitectura\` — directrices técnicas de la migración a .NET 10.
- `docs\producto\` — visión, objetivos y alcance del producto, y estado de la migración.
- `docs\flujos-clinicos\` — procesos asistenciales paso a paso.
- `docs\historias-usuarios\` — requisitos funcionales en formato de historia de usuario.
- `docs\legado-cloudflare\` — prototipo funcional de referencia (congelado), fuente original de las reglas de negocio del producto.
- `docs\bocetos-pantallas\guia-diseno-sistema-visual.md` — directriz vinculante de paleta de colores y estándares visuales (Bootstrap 5, WCAG 2.2 AA) para las vistas Razor. A diferencia del resto de `docs\bocetos-pantallas\` (propuestas de interfaz), este documento sí es de obligado cumplimiento al maquetar.
