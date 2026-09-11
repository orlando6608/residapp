# AGENTS.md

> Instrucciones para agentes de desarrollo

- **Versión:** 1.4
- **Fecha:** 6 de septiembre de 2026
- **Estado:** Vigente para el prototipo con datos ficticios
- **Alineación:** línea base funcional `LBF-CONNECT-2026-09-06-V1.1` (PRD v0.5 + matriz v0.2.1 + seis wireframes canónicos + trazabilidad v0.2)
- **Finalidad:** Definir cómo debe trabajar un agente de programación sobre el repositorio sin alterar implícitamente decisiones funcionales, clínicas, de privacidad, permisos o arquitectura.

> **Cambio central de la versión 1.4:** incorpora las cuatro reglas aprobadas para `D1-P01` y `D1-P08`, mantiene bloqueados `D1-P04`–`D1-P06` y limita `0001`, ya integrada e inmutable, a aplicación local con datos sintéticos.

## 1. Proyecto y alcance

Plataforma web asistencial y de comunicación para residencias geriátricas. Organiza registro cotidiano, eventos observados, revisión profesional, continuidad asistencial, publicaciones familiares aprobadas, citas estructuradas, administración del centro y supervisión clínica.

- La versión actual no sustituye la historia clínica oficial ni el software integral del centro.
- El prototipo utiliza exclusivamente datos ficticios. No usar datos reales en código, fixtures, tests, capturas, logs o demostraciones.
- La plataforma no es canal de emergencias ni sistema de triaje autónomo.
- No convertir una posible evolución futura en alcance presente.
- No declarar la arquitectura actual apta para producción sanitaria sin una revisión específica de seguridad, protección de datos, disponibilidad, backups y operación.

## 2. Fuentes de verdad y conflictos

Antes de implementar, identificar los documentos que gobiernan la funcionalidad afectada. La línea base vigente es `LBF-CONNECT-2026-09-06-V1.1`, declarada en `docs/product/baselines/2026-09-06-declaracion-linea-base-funcional-v1.1.md`.

1. Decisión explícita posterior, aprobada y registrada mediante el procedimiento de cambio.
2. PRD v0.5 consolidado.
3. Matriz de permisos de seis perfiles v0.2.1.
4. Wireframe Markdown canónico del perfil para detalle de pantalla e interacción.
5. Este `AGENTS.md` v1.4.
6. Implementación y pruebas existentes como evidencia del estado real.

La matriz de trazabilidad v0.2 enlaza las fuentes anteriores, pero no introduce requisitos. Los PDF y DOCX anteriores a la línea base son evidencia histórica y no gobiernan nuevas implementaciones.

Si existe contradicción, no decidirla silenciosamente. Describir los documentos implicados, impacto, alternativas y parte que puede ejecutarse sin cambiar producto. Una ampliación de alcance o permiso requiere decisión explícita y actualización documental.
## 3. Stack actual del repositorio

- **Framework:** Next.js 16 con App Router.
- **Frontend:** React 19.
- **Lenguaje:** TypeScript 5.9.
- **Runtime/despliegue:** Cloudflare Workers mediante vinext, Vite y Wrangler.
- **Estilos:** CSS propio; Tailwind está instalado, pero no es el sistema visual principal.
- **Backend:** lógica server-side de Next.js sobre Cloudflare Workers; no existe backend independiente.
- **Base de datos:** Cloudflare D1.
- **ORM:** Drizzle ORM.
- **Persistencia:** D1 y Drizzle configurados; `0001` integrada en `main` como base física inicial, sujeta al PRD, a revisión local y a migraciones posteriores para cualquier cambio.
- **Autenticación del prototipo:** identidad de ChatGPT/SIWC y helpers existentes; no equivale a autenticación productiva.
- **Testing:** `node:test` y scripts de validación/renderizado existentes.

No asumir versiones exactas distintas sin comprobar `package.json` y archivos de configuración. Si el repositorio ha cambiado, informar antes de actualizar este bloque o tomar decisiones arquitectónicas.

## 4. Arquitectura y separación de capas

Mantener separadas presentación, dominio, autorización y persistencia.

| Capa o ruta | Responsabilidad |
| --- | --- |
| `app/` | Páginas, layouts y rutas |
| `app/auxiliar/` | Vistas de Auxiliar, si existe o se crea según patrón vigente |
| `app/enfermeria/` | Vistas de Enfermería |
| `app/medico/` | Vistas de Medicina |
| `app/familia/` | Portal Familiar, si existe o se crea según patrón vigente |
| `app/administracion/` | Vistas administrativas, si existe o se crea según patrón vigente |
| `app/direccion/` | Vistas de Dirección Clínica, si existe o se crea según patrón vigente |
| `components/clinical/` | Componentes asistenciales reutilizables |
| `components/` | Componentes comunes de UI |
| `lib/` | Dominio, estados, permisos y reglas de negocio |
| `db/` | Persistencia D1/Drizzle |
| `worker/` | Entrada Cloudflare Workers |
| `tests/` | Pruebas de dominio, permisos, estados y artefacto |

- No introducir lógica clínica o de autorización solo en componentes visuales.
- Reutilizar el módulo común de derivación para Enfermería y Medicina.
- Reutilizar el mismo dominio de citas para Portal Familiar y Administración, con vistas distintas.
- No crear API REST/GraphQL, backend independiente o nueva capa de servicios salvo necesidad justificada y explícita.
- No hacer una refactorización transversal para resolver una tarea local sin exponer antes su alcance.

## 5. Modelo de autorización obligatorio

Implementar autorización server-side combinando roles y atributos. La interfaz puede ocultar acciones, pero el servidor debe denegarlas igualmente.

### 5.1. Atributos mínimos

- Usuario autenticado y cuenta activa.
- Centro y unidad autorizados.
- Residente asignado o vinculado.
- Perfil activo en cuentas multirol.
- Permiso específico, cuando proceda.
- Autoría y ventana temporal para correcciones.
- Estado del recurso y transición solicitada.
- Autorización familiar Activa y audiencia de la publicación.
- Configuración de citas del centro.
- Finalidad de acceso y nivel de supervisión de Dirección.

### 5.2. Reglas multirol

- Una cuenta puede tener varios roles, pero solo uno está activo en cada contexto de actuación.
- Cambiar de perfil debe ser explícito y visible.
- No unir permisos de varios roles en una misma operación.
- Toda escritura conserva usuario, rol activo, centro, unidad/residente cuando proceda y fecha/hora.
- Una actuación clínica de un directivo se realiza desde Enfermería o Medicina, nunca desde Dirección Clínica.
- Cargo, organigrama, turno y rol del sistema no son equivalentes.

### 5.3. Denegación por defecto

Si falta ámbito, relación, autorización, permiso o estado válido, denegar. No inventar permisos. No usar la existencia de una ruta, componente, identificador o enlace como evidencia de autorización.

## 6. Invariantes clínicas y funcionales

- No implementar diagnóstico, prescripción, solicitud de pruebas, triaje, recomendación terapéutica o decisión clínica automática.
- No derivar automáticamente ni sustituir avisos presenciales, telefónicos, emergencias o protocolos.
- Mantener separados observación, valoración, conducta, ejecución y comunicación familiar.
- Quien observa registra; quien valora, indica, ejecuta, corrige o cierra firma su actuación.
- Un registro firmado no se elimina ni sobrescribe silenciosamente.
- Un vencimiento no cierra, borra ni oculta eventos o seguimientos.
- Un evento abierto no queda ligado permanentemente a la cuenta que inicia la valoración.
- Lectura de una indicación no equivale a realización.
- No introducir prioridad clínica automática para indicaciones.
- Un resultado pendiente puede mantener el evento en seguimiento solo por decisión médica documentada.
- El cierre médico no genera una tarea redundante de cierre para Enfermería.
- La publicación familiar permanece separada y exige aprobación humana previa.
- La programación automática publica solo contenido ya aprobado.
- No usar `localStorage` como persistencia clínica, asistencial, familiar o de citas.
- Sexo documentado admite solo `male`, `female`, `other` y `unknown`; procede de documentación administrativa, no se infiere y no admite texto libre.
- En ayuda técnica, `NINGUNA` significa ausencia conocida y `NO_DOCUMENTADO` desconocimiento; `NO_APLICA` se rechaza.
- Textura y consistencia de líquidos admiten `NO_APLICA` solo con vía exclusivamente `ENTERAL`; `MIXTA` exige dato oral o `NO_DOCUMENTADO`.
- Toda opción estructurada `OTRO/OTRA` exige texto recortado no vacío; sin la opción correspondiente, el texto debe quedar vacío.

## 7. Reglas por perfil

### 7.1. Auxiliar

- Ve residentes asignados y el basal vigente en solo lectura.
- Registra Sin cambios, No valorable o Cambio, y puede iniciar eventos observados.
- Un evento prioritario recuerda protocolo y aviso directo; no realiza triaje automático.
- No modifica basal, valora clínicamente, escala directamente a Medicina, cierra eventos ni aprueba publicaciones.

### 7.2. Enfermería

- Trabaja en bandejas compartidas, seguimientos y continuidad de unidad.
- Puede completar/reevaluar basal únicamente con permiso del centro.
- Puede registrar evento propio, valorar, seguir, escalar, documentar urgencia, derivar y cerrar dentro de su competencia.
- Confirma lectura y ejecución de indicaciones como estados separados.
- Si cierra, puede decidir, redactar y aprobar publicación familiar.
- No gestiona autorizaciones familiares ni altera observaciones de otros.

### 7.3. Medicina

- Recibe escalados, inicia eventos propios y realiza valoración, conducta, indicaciones y seguimiento.
- Puede mantener un evento abierto por resultados pendientes sin automatización de criterio.
- Puede generar/finalizar derivación y cerrar; si es punto final, decide y aprueba publicación.
- No altera observaciones o valoraciones previas de otros perfiles.

### 7.4. Familiar

- Solo accede con autorización Activa al residente concreto.
- Solo consulta publicaciones aprobadas y publicadas para su audiencia.
- No accede a registro clínico, basal, constantes, historial, línea temporal o informe de derivación.
- No hay chat, mensajería libre, descarga específica ni notificaciones externas en el piloto.
- Puede operar citas únicamente conforme a la configuración del centro.

### 7.5. Administración

- Gestiona estructura, identidades, usuarios multirol, asignaciones, turnos, familiares, autorizaciones, programación, citas y auditoría administrativa.
- Alta administrativa y basal profesional permanecen separados.
- Consulta estado técnico/administrativo de publicaciones sin contenido clínico completo por defecto.
- No registra, valora, indica, cierra, aprueba contenido familiar ni accede por defecto al historial clínico.

### 7.6. Dirección/Coordinación Clínica

- Supervisa pendientes, continuidad, indicadores, derivaciones y proceso de comunicación.
- El nivel operativo no abre automáticamente contenido clínico completo.
- El detalle clínico exige permiso específico, es solo lectura y cada acceso queda auditado.
- No registra, modifica, indica, ejecuta, corrige, cierra ni aprueba desde este perfil.
- No crear rankings individuales ni juicios automáticos de calidad clínica.

## 8. Citas: bifurcación por configuración

Esta sección es vinculante para Portal Familiar y Administración.

### 8.1. Configuración del centro

- `appointments.enabled = false`: no mostrar acciones de cita al familiar.
- `appointments.enabled = true` y `bookingMode = DIRECT`: usar Reserva directa.
- `appointments.enabled = true` y `bookingMode = REQUEST`: usar Solicitud previa.
- No mostrar ambos recorridos simultáneamente.
- Equipos y modalidades visibles proceden de configuración server-side.
- El familiar elige equipo y modalidad, no profesional concreto.

### 8.2. Reserva directa

Flujo: equipo -> modalidad -> huecos disponibles -> selección -> revisión -> confirmación -> Cita confirmada.

- Disponibilidad calculada en servidor.
- Restricción de unicidad y transacción para impedir doble reserva.
- Confirmación y reintentos idempotentes.
- Si el hueco ya no está disponible, devolver conflicto controlado y alternativas; no confirmar parcialmente.
- Reprogramación vuelve a disponibilidad; cancelación requiere confirmación.

### 8.3. Solicitud previa

Flujo: equipo -> modalidad -> motivo estructurado -> comentario breve opcional -> disponibilidad aproximada -> enviar -> Pendiente de gestión -> propuesta del centro -> aceptar/cambiar/cancelar.

- No convertir el comentario en chat.
- Solo la aceptación de una propuesta produce Cita confirmada.
- El profesional concreto puede asignarse internamente y no se ofrece como elección familiar.

### 8.4. Cambio de configuración

No migrar, cancelar ni reescribir automáticamente citas o solicitudes existentes al cambiar el modo. Aplicar el nuevo modo a nuevas operaciones y conservar los objetos anteriores hasta resolución, con auditoría.

## 9. Publicaciones familiares

- Tipos: resumen diario, resumen semanal y actualización relevante.
- Una publicación aprobada puede programarse; nunca generar texto para cubrir una ausencia.
- Ordinarias visibles 30 días; relevantes 6 meses en Portal Familiar.
- Publicada significa inmutable. Corregir mediante nueva publicación vinculada.
- Retirada excepcional solo con permiso, motivo y auditoría; no para mejorar redacción.
- Derivación urgente genera actualización relevante y registro de intento de llamada, sin retrasar asistencia.
- Administración y Dirección pueden ver estados según matriz, nunca aprobar.

## 10. Correcciones, historial y trazabilidad

- Corrección ordinaria: referencia de 6 horas, configurable, limitada al autor y solo para cursos/notas clínicas expresamente habilitados; nunca para el basal.
- Fuera de ventana: rectificación trazable.
- Original, corrección y rectificación conservan autor, motivo y fecha/hora.
- Cerrados salen de bandejas y permanecen en Historial.
- Línea temporal completa plegada por defecto y protegida por permisos.
- Acceso clínico detallado de Dirección siempre auditado.
- No ampliar historial de Auxiliar, Administración o Familia.

## 11. Privacidad y seguridad

- Mínimo privilegio y aislamiento por centro, unidad y residente.
- Autorización en servidor en cada lectura y escritura relevante.
- Sin datos sanitarios reales durante el prototipo.
- Minimizar datos, accesos y retención.
- No registrar contraseñas, tokens, secretos ni texto clínico completo.
- No enviar contenido sanitario por email, SMS, WhatsApp o enlaces públicos.
- Sin publicidad, píxeles de marketing, session replay o analítica invasiva en el portal.
- La autenticación del prototipo no es solución productiva.
- No ampliar o rediseñar autenticación salvo tarea explícita.
- Tratar la matriz de permisos como requisito verificable, no como documentación decorativa.

## 12. Base de datos y persistencia

- No modificar `db/schema.ts`, migraciones o persistencia sin anunciar impacto.
- Antes de persistencia nueva, proponer entidades, campos, relaciones, restricciones, índices y migración.
- Modelar estados y transiciones; evitar flags ad hoc que permitan combinaciones inválidas.
- Entidades mínimas derivadas de la línea base v1.1: roles múltiples/perfil activo, estructura física e historial de ubicación, autorizaciones familiares, estado basal versionado, configuración de publicaciones, agenda/franjas/citas, niveles de supervisión y accesos auditados.
- Citas directas requieren restricción de unicidad y operación transaccional o mecanismo equivalente seguro en D1.
- Mantener idempotencia en firma, creación de eventos, aprobación, publicación y reserva.
- No simular persistencia clínica o de citas mediante navegador.
- Mantener compatibilidad con datos existentes cuando ya haya esquema de dominio.
- `0001` está integrada en `main`, es inmutable y solo puede aplicarse a una D1 local desechable con fixtures sintéticos. La aplicación remota está prohibida; cualquier cambio futuro del esquema debe hacerse mediante `0002` o una migración posterior.
- `D1-P04`, `D1-P05` y `D1-P06` permanecen sin botones, endpoints o grants operativos y se deniegan por defecto.

## 13. Frontend, UX y accesibilidad

- Responsive en escritorio, tableta y móvil; tableta/ordenador prioritarios para profesionales.
- Coherencia con estilos y componentes existentes.
- Estados de riesgo, vencimiento, permiso o error no dependen solo del color.
- No eliminar información necesaria para autoría, estado, continuidad o seguridad.
- Línea temporal plegada por defecto cuando lo establece el wireframe.
- Portal Familiar usa lenguaje sencillo y nunca muestra términos internos automáticamente.
- Ausencia de publicaciones no equivale a estabilidad.
- Fallo técnico no equivale a ausencia de información.
- Objetivo WCAG 2.2 AA.
- No alterar flujos o permisos por motivos estéticos.

## 14. Dependencias y arquitectura

- No introducir dependencias sin necesidad clara.
- Explicar el problema resuelto y por qué las herramientas existentes no bastan.
- No actualizar dependencias ampliamente como efecto colateral.
- No migrar a otro proveedor, backend o base de datos porque aparezca en una hoja de ruta antigua.
- Una decisión de arquitectura productiva requiere tarea y revisión separadas.

## 15. Procedimiento antes de modificar código

1. Leer `AGENTS.md` v1.4, la declaración `LBF-CONNECT-2026-09-06-V1.1`, el PRD v0.5, la matriz v0.2.1 y el wireframe canónico relevante.
2. Inspeccionar `AGENTS.md` adicionales aplicables por directorio.
3. Identificar archivos y estado real del repositorio.
4. Explicar impacto en interfaz, dominio, permisos, persistencia, autenticación, arquitectura, dependencias y tests.
5. Si hay contradicción, detener solo la parte conflictiva y hacerla visible.
6. Modificar únicamente archivos necesarios y preservar cambios ajenos.
7. Evitar refactorizaciones oportunistas.

## 16. Testing y validación mínimos

Después de cada cambio, ejecutar pruebas relevantes y reportar resultados reales.

### 16.1. Generales

- Tests unitarios/de dominio del módulo.
- Build cuando afecte compilación, rutas, dependencias o integración.
- Validación de artefacto/renderizado cuando cambie interfaz.
- Idempotencia y concurrencia cuando afecte firma, valoración, publicación o reserva.
- Pruebas positivas y negativas de autorización server-side.

### 16.2. Estados clínico-asistenciales

- Vencido permanece abierto y visible.
- Transferencia conserva evento, autoría y pendientes.
- Seguimiento sin relevo puede quedar para próxima revisión.
- Indicación leída no es realizada.
- Cierre médico no exige segundo cierre de Enfermería.
- Corrección conserva original.
- Derivación común respeta contenido y permisos.

### 16.3. Seis perfiles y multirol

- Cada perfil solo accede a las acciones de la matriz.
- Cambiar URL, centro, unidad o residente no amplía acceso.
- Multirol exige cambio explícito; no mezcla permisos.
- Dirección operativa no ve detalle clínico.
- Dirección clínica autorizada ve solo lectura y genera auditoría.
- Administración no obtiene contenido clínico por defecto.
- Familiar revocado queda bloqueado en la siguiente petición.

### 16.4. Publicaciones

- Borradores y pendientes no llegan al familiar.
- Programación publica solo contenido aprobado.
- Corrección mantiene original vinculado.
- Retirada excepcional exige permiso y motivo.

### 16.5. Citas

- Servicio desactivado oculta y bloquea endpoints de cita.
- Modo `DIRECT` muestra solo reserva directa.
- Modo `REQUEST` muestra solo solicitud previa.
- Reserva concurrente del mismo hueco produce una sola confirmación.
- Reintento idempotente no duplica cita.
- Solicitud no pasa a confirmada sin aceptación.
- Cambio de modo no reescribe citas existentes.
- Familiar sin autorización activa no consulta ni modifica citas.

No afirmar que una modificación funciona si las pruebas relevantes no se ejecutaron. Distinguir fallo previo de regresión introducida cuando sea posible.

## 17. Alcance de cambios y criterio de terminado

- Priorizar cambios pequeños, reversibles y revisables.
- No mezclar arquitectura, interfaz y modelo de datos salvo que sea inseparable y se explique.
- No borrar código funcional por preferencia estilística.
- No transformar detalles de wireframe en reglas globales distintas del PRD.

Una tarea está terminada cuando:

- El comportamiento está alineado con PRD, matriz y wireframe.
- La autorización server-side está aplicada.
- Los estados y la autoría están preservados.
- No se añadieron datos reales, secretos ni decisiones clínicas automáticas.
- Las pruebas relevantes se ejecutaron y se informaron.
- Los archivos modificados están enumerados.
- Las limitaciones, deudas o contradicciones están declaradas.

## 18. Formato del informe final del agente

```markdown
## Cambios realizados

- ...

## Archivos modificados

- ...

## Pruebas ejecutadas

- Comando o prueba: resultado

## Limitaciones o pendientes

- ...
```

## 19. Regla final

**Principio de conservación:** cumplir la tarea preservando flujo, arquitectura, permisos, estados y reglas clínicas vigentes. Si una tarea exige alterar una decisión estructural, hacerlo visible antes de implementarla.

## Anexo. Cambios de v1.2 a v1.3

- Alineación documental con `LBF-CONNECT-2026-09-05-V1`, sin modificar la línea base funcional.
- PRD v0.4, matriz v0.2, seis wireframes Markdown y trazabilidad v0.1 pasan a ser las referencias vigentes.
- PRD v0.3, matriz v0.1 y wireframes PDF/DOCX quedan expresamente como evidencia histórica.
- La ventana de seis horas queda explicitada solo para cursos/notas clínicas habilitados y nunca para el basal.
- CFS y Pfeiffer permanecen fuera del producto estructurado conforme a la línea base.
- Se actualizan las instrucciones previas a persistencia sin aprobar por anticipado D1/Drizzle.

## Anexo. Cambios de v1.3 a v1.4

- Se adopta `LBF-CONNECT-2026-09-06-V1.1`, PRD v0.5, permisos v0.2.1 y trazabilidad v0.2.
- Se incorporan los catálogos y validaciones de `D1-P01` y `D1-P08`.
- Se preserva la denegación de `D1-P04`–`D1-P06`.
- `0001` quedó integrada tras revisión SQL y pruebas locales sintéticas; sigue limitada a D1 local desechable y cualquier cambio físico posterior requiere `0002` o una migración posterior.
