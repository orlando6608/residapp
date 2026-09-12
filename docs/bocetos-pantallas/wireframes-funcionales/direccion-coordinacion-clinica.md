# Boceto de pantallas - Dirección / Coordinación Clínica

**Versión:** 0.1
**Fecha:** 2026-09-12
**Estado:** Borrador
**Fuente canónica:** Markdown
**Deriva de:** `docs/legado-cloudflare/docs/product/wireframes/2026-09-05-wireframe-funcional-direccion-coordinacion-clinica-v0.1.md`
**Referencias:** `docs/historias-usuarios/direccion-coordinacion-clinica.md`, `docs/flujos-clinicos/supervision-clinica-direccion.md` y `docs/producto/alcance.md`.

## 1. Alcance

Adaptación del wireframe legado de Dirección/Coordinación Clínica a la especificación vigente. El contenido funcional ya era coherente con `docs/historias-usuarios/direccion-coordinacion-clinica.md`. Se actualizan las referencias normativas y se enlaza el detalle operativo con `docs/flujos-clinicos/supervision-clinica-direccion.md`.

## 2. Reglas transversales

- Dirección supervisa; no valora, indica, ejecuta, escala, corrige ni cierra desde este perfil.
- El detalle clínico exige permiso específico, finalidad válida, ámbito y registro de auditoría antes de entregar contenido.
- El contenido clínico y basal es siempre de solo lectura.
- Una cuenta multirol debe cambiar explícitamente al perfil asistencial autorizado; desde ese momento la acción ya no pertenece a Dirección.
- No se crean rankings nominativos ni predicciones clínicas.

## 3. Pantallas

### DIR-01. Inicio de Dirección Clínica

- **Muestra:** cierres pendientes, eventos abiertos/vencidos, seguimientos, continuidad, indicaciones con incidencia, derivaciones y estado familiar.
- **Regla:** agregación por ámbito; sin apertura automática de notas completas.

### DIR-02. Visión por unidades

- **Muestra:** carga y pendientes por unidad, con denominadores cuando corresponda.
- **Acción:** seleccionar unidad dentro del ámbito.

### DIR-03. Supervisión de pendientes

- **Muestra:** elementos pendientes por tipo, antigüedad y estado.
- **Acción:** abrir detalle operativo.

### DIR-04. Detalle de supervisión operativa

- **Muestra:** residente/episodio, unidad, hitos, responsables de equipo y estado.
- **Regla:** no muestra automáticamente contenido clínico completo ni ofrece acciones asistenciales.

### DIR-05. Contenido clínico de supervisión

- **Acceso condicionado:** permiso clínico específico, finalidad y ámbito.
- **Muestra:** detalle necesario, incluido basal vigente cuando esté autorizado.
- **Reglas:** solo lectura; auditoría antes de servir contenido.

### DIR-06. Línea temporal completa

- **Muestra:** secuencia autorizada, plegada por defecto.
- **Reglas:** solo lectura; conserva basal y ubicación históricos aplicables a cada evento.

### DIR-07. Historial de eventos

- **Muestra:** episodios cerrados y documentos vinculados según permiso.
- **Regla:** no permite exportación masiva de historias individuales.

### DIR-08. Panel de indicadores

- **Muestra:** indicadores agregados por ámbito y periodo con denominadores.
- **Reglas:** sin juicio automático, predicción o ranking individual.

### DIR-09. Indicadores de continuidad

- **Muestra:** transferencias, seguimientos vencidos e indicaciones pendientes/agregadas.
- **Regla:** sirven para detectar proceso, no para atribuir desempeño individual.

### DIR-10. Evolución temporal

- **Muestra:** tendencias agregadas comparables por periodo.
- **Regla:** explica filtros y denominadores.

### DIR-11. Revisión de calidad de proceso

- **Muestra:** cumplimiento de hitos definidos y excepciones.
- **Regla:** no cambia categorías ni umbrales clínicos desde la pantalla.

### DIR-12. Derivaciones

- **Muestra:** estado agregado y episodios autorizados.
- **Acción condicionada:** consultar informe firmado con permiso clínico.
- **Regla:** no generar ni firmar informes.

### DIR-13. Comunicación familiar

- **Muestra:** frecuencia y estados de publicación, pendientes y errores.
- **Regla:** no redactar, modificar ni aprobar.

### DIR-14. Trazabilidad clínica

- **Muestra:** actor, perfil, ámbito, acción, recurso y fecha/hora de hitos autorizados.
- **Regla:** consulta auditada; sin alteración de registros.

### DIR-15. Correcciones y rectificaciones

- **Muestra:** original, corrección/rectificación, motivo, autor y fechas.
- **Reglas:** solo lectura; los basales firmados aparecen como versiones vinculadas, nunca como edición de seis horas.

### DIR-16. Informes de actividad

- **Acciones:** generar informe agregado por ámbito y periodo.
- **Regla:** sin exportación masiva de historias ni rankings nominativos.

### DIR-17. Mi ámbito de supervisión

- **Muestra:** centros, unidades y permisos vigentes del perfil activo.
- **Regla:** informar del ámbito no permite ampliarlo.

### DIR-18. Acceso denegado

- **Muestra:** mensaje neutro y retorno seguro.
- **Reglas:** sin revelar contenido o existencia fuera de ámbito; el error técnico es un estado diferente.

## 4. Consulta basal desde Dirección

- Requiere permiso clínico específico para vigente o histórico, finalidad válida y residente dentro de ámbito.
- El acceso queda auditado antes de entregar contenido.
- Puede mostrar nueve áreas, Barthel común y relaciones entre versiones; CFS no existe en el producto y nunca se muestra.
- No ofrece crear, aportar, firmar, cancelar, corregir o rectificar.

## 5. Criterios de aceptación

1. Ninguna pantalla de Dirección contiene controles asistenciales de escritura.
2. DIR-05/06/07/12/14/15 deniegan contenido clínico detallado sin permiso y auditoría.
3. Consultar basal es solo lectura y no incluye CFS.
4. Para actuar clínicamente se exige cambio explícito a otro perfil autorizado.
5. Los informes y paneles no generan rankings nominativos.

## 6. Diferencias respecto al wireframe legado

| Elemento | Wireframe legado (v0.1) | Este boceto (v0.1) |
| --- | --- | --- |
| Referencias normativas | PRD y matriz de permisos legados | `docs/historias-usuarios/direccion-coordinacion-clinica.md`, `docs/flujos-clinicos/supervision-clinica-direccion.md` y `docs/producto/alcance.md` |
| Contenido funcional | — | Sin cambios; ya coherente con el alcance vigente |
