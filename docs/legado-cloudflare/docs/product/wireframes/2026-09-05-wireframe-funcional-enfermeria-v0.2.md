# Wireframe funcional - Enfermería

**Versión:** 0.2  
**Fecha:** 2026-09-05  
**Estado:** Candidato a línea base funcional  
**Fuente canónica:** Markdown  
**Sustituye:** Wireframe funcional Enfermería v0.1  
**Referencias normativas:** PRD v0.4 (`AUTH`, `RES`, `BAS`, `ENF`, `DER`, `FAM`, `HIS`, `COR`, `AUD`) y matriz de permisos v0.2.

## 1. Alcance de la revisión

Se conserva la navegación y la numeración del wireframe v0.1. Los cambios materiales son:

- uso del módulo basal común de nueve áreas más `BARTHEL_COMUN_V0_1`;
- retirada completa de CFS y Pfeiffer;
- separación entre identidad administrativa y valoración basal;
- un solo borrador basal activo, firma exclusiva de su creador y aportaciones con autoría individual;
- basal firmado inmutable y rectificación mediante nueva versión vinculada;
- ventana de seis horas limitada a cursos o notas clínicas expresamente habilitados;
- actualización del informe de derivación: incluye Barthel, pero no CFS ni el bloque externo de servicios contactados.

## 2. Reglas transversales del perfil

- Toda acción se valida en servidor con cuenta activa, perfil **Enfermería** activo, ámbito y permiso específico.
- Las bandejas son compartidas por unidad. Iniciar una valoración protege la concurrencia, pero no crea propiedad permanente.
- Observaciones originales, autorías y marcas de tiempo no son editables.
- El basal solo se muestra o modifica dentro del ámbito autorizado. Crear la identidad del residente exige el permiso independiente `RESIDENT_IDENTITY_CREATE`.
- Los fallos de autorización o carga nunca se presentan como ausencia de incidencias.

## 3. Navegación conservada

`Inicio` · `Prioritarios` · `Cambios ordinarios` · `Seguimientos` · `Indicaciones` · `Comunicaciones` · `Residentes` · `Historial` · `Mi cuenta`

## 4. Pantallas

### ENF-01. Inicio de Enfermería

- **Muestra:** contadores de prioritarios, ordinarios, seguimientos, indicaciones y comunicaciones pendientes; residentes del ámbito.
- **Acciones:** abrir cada bandeja, registrar evento propio y acceder a residentes.
- **Reglas:** cerrados no aparecen; abiertos vencidos continúan visibles; los contadores respetan unidad y permisos.

### ENF-02. Bandeja de cambios ordinarios

- **Muestra:** residente, unidad, área observada, autor y fecha/hora, antigüedad y estado.
- **Acciones:** filtrar y abrir detalle.
- **Reglas:** orden por antigüedad; la observación original permanece inmutable.

### ENF-03. Bandeja prioritaria

- **Muestra:** motivo inicial, aviso directo documentado, tiempo transcurrido y estado.
- **Acciones:** iniciar valoración o continuar una ya abierta.
- **Reglas:** la clasificación inicial organiza la atención y no constituye diagnóstico.

### ENF-04. Detalle del evento recibido

- **Muestra:** observación original, autoría, basal vigente resumido y últimas actuaciones.
- **Acciones:** empezar valoración; desplegar línea temporal si existe permiso.
- **Reglas:** comenzar registra profesional y hora; ante edición concurrente se obliga a recargar.

### ENF-05. Valoración de Enfermería

- **Campos:** hallazgos, valoración, actuaciones, comunicaciones, resultado y constantes opcionales (T, PA, FC, FR, SpO2, aire/oxigenoterapia, flujo O2, glucemia y otra con nombre/valor/unidad).
- **Acciones:** guardar borrador y pasar a decisión asistencial.
- **Reglas:** nunca modifica la observación original; identidad, perfil, ámbito y hora proceden del servidor.

### ENF-06. Decisión asistencial

- **Opciones:** cerrar; iniciar seguimiento; escalar a Medicina; activar/documentar protocolo urgente.
- **Reglas:** son salidas explícitas; el sistema no decide clínicamente ni infiere prioridad.

### ENF-07A. Cierre por Enfermería

- **Muestra:** resumen de valoración y actuaciones.
- **Acciones:** cerrar y decidir si genera comunicación familiar.
- **Reglas:** el cierre es idempotente; el evento pasa a Historial.

### ENF-07B. Seguimiento u observación

- **Campos:** fecha prevista o criterio, equipo responsable e indicaciones de continuidad.
- **Reglas:** exige fecha o criterio; permanece abierto aunque venza.

### ENF-08. Bandeja compartida de seguimientos

- **Muestra:** responsable de equipo, próxima revisión, última actuación y vencimiento.
- **Acciones:** registrar actuación, reprogramar justificadamente o resolver.
- **Reglas:** cada actuación conserva su autoría; no se asigna propiedad permanente a una persona.

### ENF-09. Transferencia y continuidad de turno

- **Campos:** equipo entrante y nota de continuidad.
- **Reglas:** la transferencia queda registrada; el evento no desaparece si la recepción no se confirma.

### ENF-10. Escalado a Medicina

- **Muestra/envía:** observación, basal vigente, valoración, constantes, actuaciones y motivo.
- **Reglas:** no genera resumen diagnóstico ni decisión automática.

### ENF-11. Protocolo urgente

- **Acciones:** documentar activación, actuaciones y evolución; abrir módulo de derivación cuando proceda.
- **Reglas:** la documentación no retrasa la atención; se conserva la trazabilidad interna de contactos.

### ENF-12. Informe de derivación a Urgencias

- **Muestra:** identificación y centro, basal relevante, Barthel, cognición/comunicación, motivo, observación, valoraciones, constantes, oxigenoterapia, actuaciones, evolución y firmante.
- **Acciones:** previsualizar, completar texto del informe, firmar y generar PDF.
- **Reglas:** la vista previa es obligatoria; editar el informe no altera las fuentes; no incluye CFS ni el bloque externo de servicios contactados/horas. El PDF firmado queda vinculado al evento.

### ENF-13. Indicaciones y tareas procedentes de Medicina

- **Muestra:** texto, fecha o criterio, emisor y estado de lectura/realización.
- **Acciones:** confirmar lectura; registrar realizada o no realizada con incidencia.
- **Reglas:** leer y realizar son hitos diferentes.

### ENF-14. Decisión sobre comunicación familiar

- **Opciones:** no comunicar o preparar comunicación.
- **Reglas:** solo al cerrar; exige audiencia autorizada; derivaciones generan actualización relevante y registro del intento de llamada.

### ENF-15. Redacción y aprobación familiar

- **Campos:** tipo, texto comprensible, audiencia y momento de publicación.
- **Acciones:** aprobar o volver a editar.
- **Reglas:** aprobación humana; el profesional visible será “Equipo asistencial del centro”; sin datos internos ni escalas.

### ENF-16. Registrar un evento observado por Enfermería

- **Campos:** residente, observación, clasificación inicial y datos clínicos pertinentes.
- **Acciones:** guardar y continuar directamente la valoración.
- **Reglas:** no simula autoría de Auxiliar ni se reenvía artificialmente a Enfermería.

### ENF-17. Lista de residentes

- **Muestra:** residentes del ámbito, unidad, ubicación actual y estado basal (vigente/pendiente).
- **Acciones:** buscar, filtrar y abrir ficha.
- **Reglas:** la ubicación actual deriva del intervalo vigente.

### ENF-18. Ficha del residente

- **Muestra:** identidad, ubicación, basal vigente, eventos e historial autorizados.
- **Acciones:** abrir basal, iniciar reevaluación con permiso o registrar evento.
- **Reglas:** no permite modificar identidad ni ubicación; el histórico basal requiere permiso.

### ENF-19. Alta de nuevo residente

- **Estado:** **condicionada**, no disponible por defecto.
- **Acción:** crear únicamente la identidad administrativa mínima cuando el centro haya concedido `RESIDENT_IDENTITY_CREATE`.
- **Reglas:** no asigna habitación ni altera el historial de ubicación; deja el basal pendiente; no deriva del permiso basal.

### ENF-20. Estado basal

- **Muestra:** versión vigente o único borrador activo y exactamente nueve áreas: movilidad, alimentación, continencia, aseo/higiene, cognición, comunicación, conducta, sueño y ayudas habituales.
- **Campos comunes de versión:** motivo (alta, revisión programada, cambio funcional consolidado o rectificación), fuente y fecha.
- **Acciones:** crear borrador inicial/reevaluación con permiso; editar solo el borrador propio; añadir aportación identificada a borrador ajeno con `BASELINE_DRAFT_CONTRIBUTE`; cancelar el borrador propio con motivo.
- **Reglas:** las nueve áreas son obligatorias para firmar; `NO_DOCUMENTADO` no significa normalidad; opciones incompatibles son excluyentes; `OTRO` exige descripción cuando sea necesaria. No existe puntuación conjunta.

### ENF-21. Índice de Barthel común

- **Instrumento:** `BARTHEL_COMUN_V0_1`, diez ítems y total automático sobre 100.
- **Muestra:** opciones abreviadas pero explicadas, puntuación de cada respuesta y total calculado.
- **Reglas:** conserva respuestas y puntuaciones; deposición y micción consideran la semana previa; traslado, deambulación y continencia no se abrevian de forma ambigua; el total no es editable.

### ENF-22. Clinical Frailty Scale

- **Estado:** **RETIRADA desde v0.2**.
- **Regla:** no existe pantalla activa, campo, puntuación, resumen, permiso ni contenido de informe asociado a CFS. El identificador se conserva para trazabilidad documental y no debe reutilizarse.

### ENF-23. Confirmación y firma del basal

- **Muestra:** validación de nueve áreas, Barthel, cognición documentada, fuente/fecha, aportaciones y resumen de cambios.
- **Acciones:** firmar únicamente si la persona usuaria creó el borrador con el mismo perfil autorizado; volver a editar; cancelar con motivo.
- **Reglas:** no se transfiere la firma. Al firmar se congelan contenido, autoría, perfil, fecha/hora, catálogo y contexto organizativo; la nueva versión pasa a vigente y la anterior a histórica de forma atómica e idempotente.

### ENF-24. Historial de eventos

- **Muestra:** eventos cerrados, derivaciones y publicaciones relacionadas según permisos.
- **Acciones:** filtrar, abrir detalle y desplegar línea temporal autorizada.
- **Reglas:** cada evento conserva referencia al basal y ubicación aplicables cuando ocurrió.

### ENF-25. Historial basal y correcciones

- **Muestra:** versiones firmadas, vigente/históricas, motivo, autoría y vínculos de reevaluación o rectificación.
- **Acciones:** consultar; iniciar nueva reevaluación con permiso. La rectificación permanece bloqueada hasta definir actor y alcance.
- **Reglas:** un basal firmado no se edita ni borra y nunca usa la ventana de seis horas. La corrección de cursos/notas propias solo se ofrece cuando el tipo de objeto y la política la habilitan; fuera de ventana se añade rectificación trazable.

## 5. Estados mínimos

- **Carga:** cargando, vacío real, error técnico y acceso denegado se diferencian.
- **Concurrencia:** versión desactualizada obliga a recargar; no se sobreescribe trabajo ajeno.
- **Basal:** sin basal, borrador propio, borrador ajeno consultable/aportable, firmado vigente, histórico y cancelado.
- **Accesibilidad:** foco visible, mensajes junto al campo y ninguna acción dependiente solo del color.

## 6. Criterios de aceptación específicos

1. No aparece CFS en ninguna ruta activa, informe o resumen.
2. ENF-20 contiene nueve áreas exactas y ENF-21 calcula Barthel desde diez respuestas persistidas.
3. Una enfermera sin permiso basal puede ver lo autorizado, pero no crear ni reevaluar un basal.
4. Nadie puede firmar un borrador basal ajeno ni aplicar seis horas a un basal firmado.
5. ENF-12 omite CFS y contactos externos, pero conserva Barthel y el PDF firmado vinculado.
6. ENF-19 solo se habilita mediante el permiso administrativo independiente.

## 7. Cambios frente a v0.1

| Elemento | v0.1 | v0.2 |
| --- | --- | --- |
| ENF-20/21/23/25 | Basal con reglas previas | Módulo común, borrador único, firma del creador e inmutabilidad |
| ENF-22 | CFS activa | Retirada; código reservado |
| ENF-19 | Alta asociada al flujo de Enfermería | Permiso administrativo excepcional e independiente |
| ENF-12 | Referencias previas de derivación | Barthel; sin CFS ni bloque externo de contactos |
| Corrección | Regla amplia de seis horas | Solo cursos/notas habilitados; nunca basal |
