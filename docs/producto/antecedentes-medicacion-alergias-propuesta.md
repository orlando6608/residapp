# Propuesta: medicación habitual, enfermedades y alergias en el informe de derivación a Urgencias

**Estado: borrador para decidir (2026-11-11). No se ha construido nada.** Lo valida una persona antes de usarlo con datos reales. La lectura de protección de datos está en [`docs/privacidad/antecedentes-medicacion-alergias-lectura-dpo.md`](../privacidad/antecedentes-medicacion-alergias-lectura-dpo.md) (también borrador); este documento la resume y la traduce a decisiones de producto.

## Qué se pide y de dónde viene

Al probar la guía 2 de pruebas (prueba 12, protocolo urgente y derivación iniciados por Medicina), el Product Owner pidió que el informe de derivación a Urgencias recoja la medicación que toma habitualmente el residente, las enfermedades que padece y si tiene o no alergias a algún medicamento. Él mismo dijo que no sabía si eso ya existía en el programa.

## Qué hay hoy

- **No existe ningún dato de medicación habitual, enfermedades ni alergias**: ni en el esquema SQL ni en el dominio ni en el basal. El basal sí recoge deterioro cognitivo (con su causa y su estadio), y esa información ya viaja en el informe.
- `docs/producto/alcance.md` deja fuera la «administración de medicación», y la política de privacidad (`Views/Home/Privacy.cshtml`, línea 54) dice a las familias y al personal que ResidApp **no guarda la medicación** de los residentes.
- El informe reúne solo, de forma automática, la identificación, el basal, la observación, las valoraciones, las constantes, las actuaciones y la evolución. El profesional escribe a mano el motivo y, opcionalmente, «Información adicional para Urgencias» (hasta 4.000 caracteres; desde el 2026-11-13 el campo lleva una ayuda que lo pide, hecha por decisión de Orlando): **hoy ya puede escribir ahí** la medicación, las enfermedades y las alergias, una derivación cada vez.
- El informe firmado no se puede corregir (un informe por evento): un dato equivocado queda impreso.

## Opciones

| | Qué es | Dato nuevo | Cuándo encaja |
|---|---|---|---|
| **A** | Dejar el texto libre como está | Ninguno | Siempre; es lo que hay |
| **A mejorado** | El mismo campo con una ayuda: «medicación habitual, enfermedades y alergias; solo lo necesario para la atención urgente; sin nombres de familiares; escribe “no consta” si no se sabe» | Ninguno | Enseguida; es un cambio de texto |
| **C** | Una ficha del residente **solo con las alergias a medicamentos** (tres estados: tiene, no tiene, no consta; con fecha de revisión y versiones), que el informe incluye solo | Un apartado vivo por residente | Primer paso recomendado por el DPO |
| **B** | Ficha con enfermedades, medicación habitual y alergias | Tres apartados vivos por residente | Más adelante y por fases, solo si se cumplen las condiciones |

## Qué recomienda el DPO y qué implica

**Recomienda C ahora, con A mejorado para lo demás, y B solo por fases.** Razones principales: las alergias son el dato más crítico en una urgencia y cambian poco; la medicación es lo más volátil y lo que más daño hace desactualizado, y es el germen del módulo «Medicamentos», que el proyecto tiene bloqueado; una lista de enfermedades tiende a convertirse en la historia clínica; y con B ResidApp guardaría una segunda copia de lo que el centro ya tiene en su hoja de tratamiento, con riesgo de que no coincidan. No hay datos reales ni producción, así que nada urge.

Con **A mejorado** no cambia nada más que un texto. Con **C** habría que construir: una tabla nueva (script SQL con seguridad por filas y permisos mínimos), la pantalla para que Medicina la edite (y quizá Enfermería, con permiso apagado por defecto), el apartado automático en el informe (con qué dice el PDF si falta o está caducada la revisión), la consulta y el borrado de la ficha, las pruebas, el Manual, la Guía por perfil y la guía de CJ, y la frase de la política de privacidad si se llegara a B. Con **B** además cambia el alcance y la frase «no guarda la medicación» deja de ser cierta.

Lo más importante no es la opción sino la **exactitud**: sea cual sea, alguien debe ser la fuente de verdad y responder de que el dato esté al día; eso lo decide el responsable clínico, no el software.

## Decisiones (en este orden)

1. **Orlando y CJ: qué opción.** ¿A mejorado ya, y C como siguiente paso? ¿O B de entrada? Reabrir el alcance es decisión del Product Owner.
2. **El médico responsable (CJ, con Orlando): las reglas clínicas.** Qué entra, quién edita y revisa, cada cuánto y qué dice el PDF si falta o caduca. Sin Auxiliar, sin Administración y sin familia.
3. **Antes de datos reales (no antes de construir en desarrollo):** base jurídica por escrito para el tratamiento de datos de salud, si la residencia cuenta como centro sanitario, si hace falta DPO designado, información a residentes y familias (con los servicios de urgencias como destinatarios), registro de actividades, evaluación de impacto y contrato de encargado. Los valida un abogado o el DPO real.
4. **La residencia:** su procedimiento para entregar el PDF (canal, quién, qué hacer con la copia y con un error).

## Qué se le ha dicho a CJ

Que hoy esos datos no existen en ResidApp, que mientras tanto puede escribirlos en «Información adicional para Urgencias» y que hay una propuesta en estudio, sin construir nada hasta decidirla (nota de la pregunta 12 de la guía 2).

## Si se aprueba A mejorado

Es un cambio de texto en `Views/Shared/_Derivacion.cshtml` (la ayuda del campo), con una prueba del texto y la revisión de la documentación de usuario. No toca datos ni esquema.
