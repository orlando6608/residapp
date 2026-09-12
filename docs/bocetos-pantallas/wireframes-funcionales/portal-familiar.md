# Boceto de pantallas - Portal Familiar

**Versión:** 0.1
**Fecha:** 2026-09-12
**Estado:** Borrador
**Fuente canónica:** Markdown
**Deriva de:** `docs/legado-cloudflare/docs/product/wireframes/2026-09-05-wireframe-funcional-portal-familiar-v0.2.md`
**Referencias:** `docs/historias-usuarios/portal-familiar.md` y `docs/producto/alcance.md`.

## 1. Alcance

Adaptación del wireframe legado del Portal Familiar a la especificación vigente. No se elimina ninguna pantalla: el legado ya no incluía CFS ni contenido fuera de alcance en este rol. Se actualizan las referencias normativas (de PRD/matriz de permisos legados a las historias de usuario y al alcance vigentes) y se ajusta la terminología a la usada hoy en `docs/producto/alcance.md`.

## 2. Límites del portal

- Solo una autorización **Activa** permite operar sobre el residente vinculado.
- La familia ve únicamente publicaciones aprobadas y publicadas para su audiencia.
- No accede a observaciones, valoraciones, constantes, basal, Barthel, indicaciones, seguimientos, derivaciones, Historial ni línea temporal interna.
- CFS no existe en el producto; no se ofrece ni siquiera como referencia de contenido no visible.
- Sin email, SMS, WhatsApp, push, chat, comentarios ni descarga específica en el piloto.

## 3. Pantallas

### FAM-01. Acceso

- **Campos:** credencial de cuenta.
- **Acciones:** continuar o recuperar acceso.
- **Reglas:** mensajes neutros; conocer una URL o identificador no concede acceso.

### FAM-02. Segundo factor

- **Campos:** código o mecanismo aprobado por la política vigente.
- **Regla:** obligatorio antes de datos reales; intentos y bloqueo seguros.

### FAM-03. Selección de residente

- **Muestra:** solo residentes con autorización Activa.
- **Regla:** no revela vínculos revocados, suspendidos o inexistentes.

### FAM-04. Inicio del residente

- **Muestra:** últimas publicaciones visibles, estado de cita propia y accesos a historial visible/citas/cuenta.
- **Regla:** ausencia de publicación no se presenta como estabilidad ni "todo bien".

### FAM-05. Historial visible de actualizaciones

- **Muestra:** ordinarias publicadas durante 30 días y relevantes durante 6 meses.
- **Acción:** abrir detalle.
- **Regla:** la ventana del portal no define conservación jurídica interna.

### FAM-06. Detalle de publicación

- **Muestra:** tipo, fecha, texto aprobado, "Equipo asistencial del centro" y correcciones vinculadas.
- **Regla:** sin datos internos ni identidad individual del profesional.

### FAM-07. Sin publicaciones recientes

- **Texto funcional:** "No hay publicaciones disponibles en este periodo".
- **Regla:** no implica ausencia de cambios, incidencias o atención.

### FAM-08. Resumen diario/semanal

- **Muestra:** publicación ordinaria según frecuencia configurada.
- **Regla:** solo contenido humano aprobado y ya publicado.

### FAM-09. Actualización relevante

- **Muestra:** texto aprobado y fecha de publicación.
- **Regla:** la relevancia la decide un profesional; el sistema no la infiere.

### FAM-10. Derivación urgente y contacto familiar

- **Muestra:** actualización relevante aprobada.
- **Regla:** el contacto urgente se realiza por teléfono y se documenta internamente; el portal no sustituye el canal urgente.

### FAM-11. Corrección de una publicación publicada

- **Muestra:** publicación original inmutable y publicación correctora vinculada.
- **Regla:** no se sustituye silenciosamente el texto original.

### FAM-12. Retirada excepcional

- **Muestra:** aviso de contenido ya no disponible cuando proceda.
- **Regla:** retirada solo por causa autorizada; no borra el original interno.

### FAM-13. Iniciar cita con el equipo asistencial

- **Aviso fijo:** "Las citas no son un canal urgente".
- **Común:** elegir Equipo médico o Enfermería y modalidad habilitada (presencial, telefónica o videoconferencia); nunca profesional concreto.
- **Si el centro usa Reserva directa:** mostrar huecos realmente disponibles, elegir fecha/hora, revisar y confirmar; el resultado es **Cita confirmada**. Si el hueco ya se ocupó, no se crea la cita y se muestran alternativas.
- **Si el centro usa Solicitud previa:** capturar equipo, modalidad, motivo categorizado, comentario breve opcional y disponibilidad aproximada; el resultado es **Pendiente de gestión**.
- **Regla:** la configuración llega del servidor y nunca se muestran ambos flujos a la vez.

### FAM-14. Seguimiento de cita o solicitud

- **Reserva directa:** consultar cita confirmada; reprogramar eligiendo un hueco disponible o cancelar con confirmación.
- **Solicitud previa:** consultar estado; aceptar propuesta (crea cita confirmada), solicitar otro horario o cancelar.
- **Reglas:** sin chat; actor y fecha/hora de cada cambio; operaciones idempotentes.

### FAM-15. Autorización no activa

- **Muestra:** acceso no disponible y canal de contacto administrativo.
- **Regla:** no revela publicaciones ni datos del residente.

### FAM-16. Mi cuenta

- **Acciones:** gestionar credenciales, segundo factor, sesiones y datos propios limitados.
- **Regla:** no permite alterar vínculos o autorizaciones.

### FAM-17. Cambio entre residentes

- **Muestra:** selector con autorizaciones activas.
- **Regla:** cada cambio recarga el ámbito; no mezcla datos entre residentes.

### FAM-18. Sesión caducada y error técnico

- **Estados separados:** sesión caducada, sin autorización, sin publicaciones y fallo técnico.
- **Regla:** un fallo técnico nunca se representa como ausencia de información.

## 4. Estados de cita visibles

| Modo | Estados del portal |
| --- | --- |
| Reserva directa | Selección de hueco → revisión → cita confirmada → reprogramada/cancelada/finalizada |
| Solicitud previa | Pendiente de gestión → propuesta → aceptada/cambio solicitado/cancelada; aceptada crea cita confirmada |
| Servicio desactivado | Citas no disponibles; no se muestran formularios |

## 5. Criterios de aceptación

1. Un familiar sin autorización Activa no puede leer ni operar sobre el residente.
2. El portal no devuelve contenido clínico interno aunque se manipule la URL.
3. FAM-13 muestra exclusivamente el modo vigente del centro.
4. Dos confirmaciones no reservan el mismo hueco y la doble pulsación no duplica la cita.
5. FAM-11 conserva original y correctora; FAM-12 no borra la evidencia interna.
6. Los estados vacío, error técnico y autorización no activa se comunican de forma distinta.

## 6. Diferencias respecto al wireframe legado

| Elemento | Wireframe legado (v0.2) | Este boceto (v0.1) |
| --- | --- | --- |
| Referencias normativas | PRD y matriz de permisos legados | `docs/historias-usuarios/portal-familiar.md` y `docs/producto/alcance.md` |
| Contenido funcional | — | Sin cambios; ya coherente con el alcance vigente |
