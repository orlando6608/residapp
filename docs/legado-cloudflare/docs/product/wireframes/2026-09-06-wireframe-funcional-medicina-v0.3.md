# Wireframe funcional - Medicina

**Versión:** 0.3  
**Fecha:** 2026-09-06  
**Estado:** Aprobado para la línea base funcional v1.1  
**Fuente canónica:** Markdown  
**Sustituye:** Wireframe funcional Medicina v0.2  
**Referencias:** PRD v0.5 y matriz de permisos v0.2.1.

## 1. Alcance de la revisión

Se conservan navegación, flujos y códigos `MED-01` a `MED-24`. Se corrigen el contexto basal, la derivación, la autoría/firma, las correcciones y los límites administrativos. CFS y Pfeiffer desaparecen de toda interfaz activa.

## 2. Reglas transversales

- Cuenta activa, perfil **Medicina** explícito, ámbito y permiso se validan en servidor.
- Medicina no obtiene permiso de alta administrativa por poder valorar el basal.
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
- **Reglas:** fuentes no editables; sin CFS; la línea temporal requiere autorización.

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
- **Reglas:** sin selector automático de prioridad; lectura y realización serán hitos distintos.

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
- **Reglas:** no altera registros fuente; sin CFS; servicios contactados/horas solo en trazabilidad interna; PDF firmado vinculado al evento.

### MED-15. Cierre médico del evento

- **Muestra:** resumen y pendientes.
- **Acción:** confirmar cierre idempotente.
- **Regla:** pasa a Historial y no requiere segundo cierre de Enfermería.

### MED-16. Decisión sobre comunicación familiar

- **Opciones:** no comunicar o preparar publicación; derivación implica actualización relevante e intento de llamada documentado.
- **Regla:** exige audiencia autorizada.

### MED-17. Redacción y aprobación familiar

- **Campos:** tipo, texto comprensible, audiencia y momento.
- **Reglas:** aprobación humana; visible como “Equipo asistencial del centro”; sin notas, escalas ni datos internos.

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
- **Reglas:** no modifica identidad ni ubicación; sin CFS.

### MED-21. Consulta y reevaluación del estado basal

- **Muestra:** nueve áreas basales y Barthel común; versiones históricas solo con permiso.
- **Acciones condicionadas:** crear borrador inicial con `BASELINE_INITIAL_COMPLETE`; reevaluar con `BASELINE_REEVALUATE`; aportar a borrador ajeno con `BASELINE_DRAFT_CONTRIBUTE`; firmar solo el borrador propio.
- **Reglas:** un borrador activo por residente; exactamente nueve áreas; `BARTHEL_COMUN_V0_1` de diez ítems con total automático/100; fuente y fecha comunes; sin CFS/Pfeiffer. Ayuda técnica utiliza `NINGUNA` o `NO_DOCUMENTADO`, nunca `NO_APLICA`; alimentación usa `NO_APLICA` en textura/líquidos solo si la vía es exclusivamente enteral; `OTRO/OTRA` exige descripción. El basal firmado es inmutable; la rectificación mediante nueva versión vinculada continúa bloqueada hasta definir actor/alcance.

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

1. No aparece CFS en MED-03, MED-14, MED-20, MED-21 ni en otra ruta activa.
2. MED-21 usa el mismo catálogo y algoritmo Barthel que Enfermería.
3. Medicina no puede crear identidad administrativa ni obtener ese permiso por asociación.
4. Solo el creador firma el borrador basal y el basal firmado no admite corrección de seis horas.
5. MED-14 conserva vista previa y PDF firmado, pero omite CFS y contactos externos.

## 6. Cambios frente a v0.1

| Elemento | Cambio v0.2 |
| --- | --- |
| Contexto clínico | CFS y Pfeiffer retirados |
| MED-21 | Módulo basal común, permiso separado, borrador único y firma del creador |
| MED-14 | Barthel vigente; sin CFS ni bloque externo de contactos |
| MED-23 | Ventana de seis horas limitada a notas/cursos habilitados |
| MED-19/20 | Sin alta ni modificación administrativa |

## 7. Cambios de v0.2 a v0.3

- MED-21 adopta las reglas cerradas de ayuda técnica, alimentación enteral y textos `OTRO/OTRA`.
- No cambia ningún permiso ni se habilita el inicio de rectificación.

