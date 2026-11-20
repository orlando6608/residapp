# Zona horaria por centro (F1, `zonas_horarias` y `zonas_centro`): lectura de protección de datos

**BORRADOR. Lo valida una persona (DPO real o abogado) antes de usarlo.** Fecha: 2026-11-21. Segunda lectura del agente `dpo-residapp`, no un dictamen. Diseño leído: `docs/producto/zonas-horarias-por-centro-estudio.md` y el patrón de `database/scripts/0098_modulos_centro.sql`. Este script solo incluye zonas de España; cualquier otra zona se añadirá con un script aparte, con su propia lectura.

## Conclusión

Admisible. La zona de un centro no es un dato personal y el diseño no añade ninguno nuevo salvo la cuenta de quien la fija, que se trata como en `modulos_centro`. El riesgo real no es de confidencialidad sino de **exactitud**: que una hora clínica se muestre mal.

## 1. ¿Son datos personales?

- **La zona del centro, no.** Describe a un centro (una empresa), no a una persona: el RGPD protege a «personas físicas identificadas o identificables» (art. 4.1, `rgpd.txt:43`) y no regula los datos de personas jurídicas (considerando 14, `rgpd-considerandos.txt:19`). Los recuentos `franjas_afectadas` y `citas_afectadas` tampoco: son cifras sueltas, sin fecha ni residente. Mientras no se les añada un identificador, una fecha de cita o un nombre, siguen siendo cifras.
- **La cuenta del operador, sí** (`cambiado_por_cuenta_id`): dato personal seudonimizado, igual que en `0098` (líneas 13-15). Va al registro de actividades de ResidApp (art. 30.2) con acceso restringido. Si la Administración del centro ve el historial, que lea «Plataforma de ResidApp» y no el nombre de la cuenta.

## 2. Plazo de conservación

Igual que `modulos_centro`: **sin decidir** (`0098`, línea 15; `antes-de-datos-reales.md`, apartado 13). Criterio de partida: mientras el centro sea cliente, más el plazo de prescripción de las infracciones que interese. La tabla es inmutable: un borrado futuro exigirá un procedimiento escrito. Conviene que la decida la misma persona y en el mismo acto que la de `modulos_centro`.

## 3. ¿Se puede inferir dónde está una persona?

**No más de lo que ya se sabe.** La zona es del centro, y el nombre y la dirección del centro ya son conocidos. Se mantiene así con tres cautelas:
- **Nunca por persona ni por navegador.** La zona sale del centro del ámbito activo, no de la zona del dispositivo (el estudio ya lo dice, apartado 7).
- **La caché de unos segundos se indexa por el centro verificado en la base**, no por la cookie del ámbito, que el ADR 0008 declara no fiable. Si no, un centro podría ver horas de otro (fallo de exactitud, no de confidencialidad).
- Las filas de cambio por `CAMBIO_DE_UBICACION` revelan que un centro se ha trasladado: es información del centro, no de residentes.

## 4. Exactitud de las horas (el riesgo que sí hay)

- «Sin fila = Madrid» falla en silencio: si la lectura falla, se verá la hora de Madrid sin avisar. Mejor que un fallo de lectura se registre.
- Corregir la zona cambia **cómo se ven** todas las horas históricas del centro (se guardan en UTC). Para `ERROR_DE_ALTA` es lo correcto. Para `CAMBIO_DE_UBICACION` no: las horas de antes del traslado se leerían en la zona nueva. Decidir si se muestran con la zona vigente en cada momento (hay `numero` y `cambiado_en` para ello).
- Un informe firmado (PDF con huella) no debe cambiar de hora al reimprimirse tras una corrección. Que ingeniería lo compruebe.
- Los procesos sin ámbito (rol `residapp_tareas`) no pueden leer la zona del centro por la seguridad por filas: es F2, pero hay que anotarlo.

## 5. Para `antes-de-datos-reales.md` (nuevo apartado, a añadir por la sesión principal)

1. Plazo de conservación de `zonas_centro`, junto al de `modulos_centro`.
2. Una línea en el registro de actividades de ResidApp (operaciones de Plataforma sobre la configuración del centro).
3. La zona la declara el centro al contratar (pregunta C del estudio); Plataforma solo la ejecuta (art. 28.3.a). Que conste en el alta.
4. Una zona nueva fuera de España no se añade sin su propio estudio (ya en el apartado 18).

## Qué debe validar una persona

1. Plazo de conservación de `zonas_centro` (decisión, con el de `modulos_centro`).
2. Que la zona se declare en el contrato o el alta del centro.
3. Que el historial que ve el centro no muestre el nombre de la cuenta operadora.
4. Cómo se muestran las horas históricas tras un `CAMBIO_DE_UBICACION` y que los informes firmados no cambien (con ingeniería y CJ).

## A verificar

Que `sys.time_zone_info` de Azure SQL tenga los nombres de Windows del catálogo (lo dice el estudio, apartado 3.1). No lo he podido comprobar.

**No soy DPO designado ni abogado.**
