# Boceto de pantallas - Medicina

**Versión:** 0.1
**Fecha:** 2026-09-12
**Estado:** Borrador
**Fuente canónica:** Markdown
**Deriva de:** `docs/legado-cloudflare/docs/product/wireframes/2026-09-06-wireframe-funcional-medicina-v0.3.md`
**Referencias:** `docs/historias-usuarios/medicina.md`, `docs/flujos-clinicos/valoracion-conducta-medicina.md`, `docs/flujos-clinicos/derivacion-urgencias.md`, `docs/flujos-clinicos/gestion-basal-barthel.md` y `docs/producto/alcance.md`.

## 1. Alcance

Adaptación del wireframe legado de Medicina a la especificación vigente. El wireframe legado ya no incluía ninguna pantalla dedicada a CFS ni a Pfeiffer (ambas quedaron fuera de alcance del producto); esta versión mantiene esa exclusión y solo actualiza las referencias normativas y de flujos clínicos.

## 2. Reglas transversales

- Cuenta activa, perfil **Medicina** explícito, ámbito y permiso se validan en servidor.
- Medicina no obtiene permiso de alta administrativa por poder valorar el basal.
- El basal no incluye CFS ni Pfeiffer; ninguna de las dos escalas forma parte del producto.
- Observación original y valoración de Enfermería son de solo lectura.
- Los seguimientos vencidos permanecen visibles; el sistema no decide diagnósticos ni desenlaces.
- Si Medicina cierra un evento, no se exige un segundo cierre de Enfermería.

## 3. Pantallas

### MED-01. Inicio de Medicina

- **Muestra:** escalados, seguimientos, indicaciones/incidencias, comunicaciones, residentes e Historial autorizados.
- **Reglas:** contadores por ámbito; cerrados solo en Historial.

### MED-02. Bandeja de escalados

- **Muestra:** residente, unidad, motivo, constantes, actuaciones, antigüedad y estado.
- **Acción:** abrir escalado.
- **Regla:** sin resumen diagnóstico automático.

### MED-03. Detalle del escalado recibido

- **Muestra:** observación original, valoración de Enfermería, constantes, actuaciones, basal vigente y línea temporal bajo demanda.
- **Acción:** iniciar valoración médica.
- **Reglas:** fuentes no editables; la línea temporal requiere autorización.

### MED-04. Inicio de valoración médica

- **Muestra:** contexto y aviso de concurrencia.
- **Acción:** comenzar/continuar.
- **Regla:** registra profesional y hora sin crear propiedad permanente.

### MED-05. Valoración médica

- **Campos:** hallazgos/exploración, valoración, constantes opcionales y actuaciones.
- **Regla:** conserva autoría; no modifica documentación previa.

### MED-06. Conducta médica

- **Opciones:** registrar actuaciones e indicaciones; cerrar, iniciar seguimiento o activar/documentar protocolo urgente.
- **Regla:** desenlace humano explícito.

### MED-07. Indicaciones a Enfermería

- **Campos:** texto, fecha prevista o criterio e información adicional.
- **Reglas:** sin selector automático de prioridad; lectura y realización son hitos distintos.

### MED-08. Seguimiento de indicaciones emitidas

- **Muestra:** lectura, realización/no realización e incidencias.
- **Regla:** Medicina consulta; Enfermería registra ejecución.

### MED-09. Regla de indicaciones pendientes

- **Muestra:** indicaciones no leídas, vencidas o con incidencia.
- **Regla:** permanecen visibles hasta resolución; no caducan silenciosamente.

### MED-10. Crear seguimiento médico

- **Campos:** fecha o criterio, equipo responsable y objetivo.
- **Regla:** si un resultado es necesario para el desenlace, el evento permanece en seguimiento.

### MED-11. Bandeja de seguimientos médicos

- **Muestra:** próxima revisión, última actuación, responsable de equipo y vencimiento.
- **Acciones:** registrar revisión, reprogramar justificadamente o cerrar.

### MED-12. Continuidad entre médicos

- **Opciones al terminar turno:** transferir al equipo entrante o conservar para próxima revisión.
- **Regla:** la trazabilidad no depende de la confirmación de recepción.

### MED-13. Protocolo urgente

- **Acciones:** documentar protocolo, evolución y derivación cuando proceda.
- **Regla:** la atención no se retrasa por completar la pantalla.

### MED-14. Informe de derivación a Urgencias

- **Muestra:** identificación/centro, basal relevante, Barthel, cognición/comunicación, motivo, observación, valoraciones, constantes, oxigenoterapia, actuaciones, evolución y firmante.
- **Acciones:** vista previa, edición del informe, firma y PDF.
- **Reglas:** no altera registros fuente; servicios contactados/horas solo en trazabilidad interna; PDF firmado vinculado al evento.

### MED-15. Cierre médico del evento

- **Muestra:** resumen y pendientes.
- **Acción:** confirmar cierre idempotente.
- **Regla:** pasa a Historial y no requiere segundo cierre de Enfermería.

### MED-16. Decisión sobre comunicación familiar

- **Opciones:** no comunicar o preparar publicación; derivación implica actualización relevante e intento de llamada documentado.
- **Regla:** exige audiencia autorizada.

### MED-17. Redacción y aprobación familiar

- **Campos:** tipo, texto comprensible, audiencia y momento.
- **Reglas:** aprobación humana; visible como "Equipo asistencial del centro"; sin notas, escalas ni datos internos.

### MED-18. Evento iniciado directamente por Medicina

- **Campos:** residente, observación, valoración y actuaciones.
- **Regla:** continúa en Medicina sin reenviarse artificialmente.

### MED-19. Lista de residentes

- **Muestra:** residentes del ámbito, unidad, ubicación actual y estado basal.
- **Acciones:** buscar y abrir ficha.
- **Regla:** no ofrece alta administrativa.

### MED-20. Ficha médica del residente

- **Muestra:** identidad, ubicación, basal vigente, eventos e historial autorizados.
- **Acciones:** registrar evento; abrir basal o iniciar reevaluación si existe permiso.
- **Regla:** no modifica identidad ni ubicación.

### MED-21. Consulta y reevaluación del estado basal

- **Muestra:** nueve áreas basales y Barthel común; versiones históricas solo con permiso.
- **Acciones condicionadas:** crear borrador inicial con `BASELINE_INITIAL_COMPLETE`; reevaluar con `BASELINE_REEVALUATE`; aportar a borrador ajeno con `BASELINE_DRAFT_CONTRIBUTE`; firmar solo el borrador propio.
- **Reglas:** un borrador activo por residente; exactamente nueve áreas; `BARTHEL_COMUN_V0_1` de diez ítems con total automático/100; fuente y fecha comunes. Ayuda técnica utiliza `NINGUNA` o `NO_DOCUMENTADO`, nunca `NO_APLICA`; alimentación usa `NO_APLICA` en textura/líquidos solo si la vía es exclusivamente enteral; `OTRO/OTRA` exige descripción. El basal firmado es inmutable; la rectificación mediante nueva versión vinculada continúa bloqueada hasta definir actor/alcance.

### MED-22. Historial de eventos

- **Muestra:** eventos cerrados, derivaciones y publicaciones relacionadas según permisos.
- **Regla:** cada evento conserva el basal y la ubicación aplicables cuando ocurrió.

### MED-23. Corrección y rectificación de registros médicos

- **Muestra:** original, corrección/rectificación, motivo, autor y fechas.
- **Reglas:** seis horas solo para cursos/notas propias expresamente habilitados; fuera de ventana se añade rectificación. Nunca permite editar un basal firmado ni una publicación ya publicada.

### MED-24. Línea temporal completa

- **Muestra:** secuencia clínica y asistencial autorizada, plegada por defecto.
- **Reglas:** solo lectura; accesos auditados cuando corresponda; no reconstruye el pasado con el basal o ubicación actuales.

## 4. Estados mínimos

- Cargando, vacío real, error técnico, acceso denegado y contenido no autorizado son estados distintos.
- En basal: sin permiso, sin basal, borrador propio, borrador ajeno aportable, vigente, histórico y cancelado.
- Ante concurrencia se recarga; nunca se sobreescribe contenido ajeno.

## 5. Criterios de aceptación

1. No aparece CFS ni Pfeiffer en MED-03, MED-14, MED-20, MED-21 ni en ninguna otra pantalla.
2. MED-21 usa el mismo catálogo y algoritmo Barthel que Enfermería.
3. Medicina no puede crear identidad administrativa ni obtener ese permiso por asociación.
4. Solo el creador firma el borrador basal y el basal firmado no admite corrección de seis horas.
5. MED-14 conserva vista previa y PDF firmado, pero omite contactos externos.

## 6. Diferencias respecto al wireframe legado

| Elemento | Wireframe legado (v0.3) | Este boceto (v0.1) |
| --- | --- | --- |
| Referencias normativas | PRD y matriz de permisos legados | `docs/historias-usuarios/medicina.md`, flujos clínicos correspondientes y `docs/producto/alcance.md` |
| CFS/Pfeiffer | Ya retirados en v0.2/v0.3 del legado | Se mantiene la exclusión; se añade Pfeiffer explícitamente a las reglas transversales para mayor claridad |
| Contenido funcional | — | Sin más cambios; ya coherente con el alcance vigente |
