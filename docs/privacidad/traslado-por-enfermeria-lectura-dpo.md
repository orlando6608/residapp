# Traslado de un residente por Enfermería con permiso: lectura de protección de datos

**BORRADOR de `dpo-residapp` (2026-11-20). Es una segunda lectura, no un DPO designado ni un abogado: lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Las citas de la ley salen del texto consolidado del BOE guardado en `normativa-local\` (no oficial): Llei 21/2000, Ley 41/2002 y RGPD.

## Qué cambia

Hasta ahora solo Administración trasladaba a un residente de unidad, habitación o plaza (script `0040`). Desde el script `0108`, Enfermería también puede, **solo si Administración le concede a su cuenta el permiso `RESIDENT_TRANSFER`** (apagado por defecto, solo para Enfermería). Lo que hace el traslado no cambia: cierra la ubicación y abre otra, guarda el traslado (inmutable), pasa a la unidad de destino los episodios no cerrados, cancela el borrador de basal si cambia de unidad y lo audita con la cuenta y el perfil. CJ dijo el 2026-10-07 «inicialmente Administración»; Orlando decidió construirlo el 2026-11-20.

**Qué afecta a los datos (corrección del agente a la primera descripción).** El traslado no solo mueve los episodios abiertos: cambia a qué unidades —y por tanto a qué personal— queda visible **todo lo que se autoriza por la unidad actual del residente** (su ficha, el basal, las alergias, las citas externas, que no guardan unidad). Los textos informativos y la nota de EIPD deben decir eso. Los episodios cerrados no se mueven.

## Veredicto

**Admisible con condiciones.** No empeora la protección de datos y en un punto la mejora: hoy lo hace Administración, que mueve episodios clínicos y cancela un borrador sin ser personal asistencial, y la Llei 21/2000, art. 11.4, limita el acceso del personal de administración a los datos de la historia clínica relacionados con sus funciones. Que lo decida un profesional asistencial encaja mejor con su art. 11.1 y con el art. 16.1 de la Ley 41/2002. **Lo valida un abogado.** Nada impide construirlo en Azure dev con datos de prueba.

## Por qué es proporcionado

- Con una sola unidad en su ámbito, la enfermera solo puede cambiar habitación o plaza: no cambia ninguna visibilidad. Con varias, ya ve origen y destino: no gana acceso ella misma.
- Todo sigue dentro del mismo centro y del mismo responsable: no hay destinatarios nuevos ni cesión (suposición del agente).
- El permiso es por **cuenta y ámbito de perfil**, no por perfil entero: se puede dar solo a la supervisión de enfermería. La documentación debe decirlo así.

## Riesgos reales (no son de confidencialidad)

1. **Perder visibilidad el equipo de origen** por un traslado equivocado o malintencionado: disponibilidad e integridad (art. 32.1.b RGPD).
2. **Mover a un residente para dificultar el acceso de un compañero** (si ambos tienen unidades distintas dentro del mismo ámbito). La auditoría lo disuade, pero solo si alguien la mira.
3. Dirección Clínica no ve estos traslados en su trazabilidad (solo ve la petición de historial anterior); Administración sí los ve, con cuenta y perfil.

## Condiciones de código y estado en la rama `feat-traslado-enfermeria`

| # | Condición | Estado |
|---|---|---|
| 1 | No ampliar `IdentityUpdate` ni `RequireResidentAdministration` a Enfermería: objetivo de autorización propio (`AuthorizationTarget.Transfer`), con prueba de que el permiso no da baja ni corrección de identidad | **Hecho**, con prueba |
| 2 | No reutilizar `AuthorizationTarget.Create` para la unidad de destino (exigiría `RESIDENT_IDENTITY_CREATE`): objetivo propio `TransferDestination` | **Hecho** |
| 3 | El perfil real en las cuatro filas inmutables (ubicación, traslado, auditoría, cancelación del borrador) | **Hecho**, con prueba |
| 4 | El permiso se comprueba en la base de datos en cada traslado (`revocado_en IS NULL`), también dentro de la transacción | **Hecho**, con prueba (caso de uso y repositorio) |
| 5 | Script nuevo del catálogo con la **lista completa** vigente, solo para Enfermería | **Hecho** (`0108`, parte de `0101`) |
| 6 | Origen y destino en el ámbito de la cuenta; respetar la restricción por residente; mismo centro | **Hecho** (prueba de origen y de destino fuera de ámbito; la restricción por residente la aplica la consulta de evidencia de siempre, sin prueba propia) |
| 7 | La auditoría de Administración muestra quién trasladó y con qué perfil | **Comprobado leyendo** (`Auditoria.cshtml` muestra cuenta y perfil) |
| 8 | Enfermería ve solo que existe un borrador, nunca su contenido | **Hecho** (el formulario no lo muestra; el aviso solo da la fecha) |
| 9 | Recomendado: aviso a quien creó el borrador cancelado, en su ficha del residente | **Hecho**: solo esa cuenta, 14 días (plazo elegido por ingeniería, no por el DPO), sin contenido ni autor; **solo si el creador sigue viendo al residente** (si el traslado lo saca de su ámbito, no abre la ficha y no ve el aviso: lo aceptó Orlando el 2026-11-20, con prueba) |

## Documentos y validaciones pendientes (de una persona)

10. **Registro de actividades:** entrada nueva, «Traslado de unidad, habitación o plaza de un residente», como actividad dentro de «historia asistencial en ResidApp» (modelo: `historial-tras-traslado-01-registro-actividades.md`). Debe constar que lo hace Administración y, si el centro lo activa, Enfermería con permiso. **Pendiente.**
11. **Anexo a la EIPD general** (misma lógica que `historial-tras-traslado-02-nota-eipd.md`): riesgo de visibilidad, disponibilidad e integridad. **Pendiente.**
12. **Información al personal:** un párrafo nuevo en `historial-tras-traslado-03-informacion-residentes-y-personal.md`, parte 2: qué hace el traslado, que queda registrado con cuenta, perfil y hora y que Administración puede revisarlo; pasa por asesoría laboral. **Pendiente.**
13. **Documentación de usuario:** hecha en esta rama (Manual, Guía —ficha ENF-26—, Flujo y el índice de pruebas de CJ).
14. **El centro decide por escrito** a quién se concede el permiso y quién revisa los traslados de Enfermería y cada cuánto (organización del centro; sin ranking por cuenta ni exportación). **Pendiente.**
15. **Validación por persona (DPO real o abogado):** que el traslado por Enfermería se apoya en el art. 9.2.h RGPD y en la Llei 21/2000, art. 11, y que Administración haciéndolo es aceptable (**a verificar**); que el registro de quién traslada es proporcionado y cómo se informa al personal; el plazo de conservación de la auditoría (sigue sin decidir, ver `historial-tras-traslado-05`); y que el riesgo residual de la EIPD es asumible.
16. **Pregunta clínica para CJ (no de protección de datos):** si Enfermería puede cancelar, al trasladar, el borrador de basal que creó Medicina. Orlando eligió cancelar y avisar al creador; CJ puede querer otra cosa.

## A verificar (no confirmado por el agente)

- Si el contenido de un borrador cancelado se conserva o se borra, y con qué plazo (`0091` concede `DELETE` en `basales_borrador_areas` y `basales_borrador_barthel`; no se ha leído cuándo se usa).
- Si `permisos_perfil` protege su historial (`0091` concede `UPDATE` completo): el evento de auditoría de la concesión apunta al ámbito de perfil y no al código del permiso.
- La autoridad de control (AEPD o APDCAT) según el tipo de centro piloto.

## Lo que el agente recomienda no hacer

Motivo obligatorio en texto libre (riesgo de datos de salud en filas inmutables; si el centro lo quiere, lista cerrada), aviso en tiempo real a Administración o Dirección y límite de frecuencia (exigen infraestructura de avisos con datos de residentes y no frenan nada).
