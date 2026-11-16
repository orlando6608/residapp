# Ficha de antecedentes de salud: diseño y valores por defecto

**Estado: diseño aprobado por Orlando (2026-11-15). Fase 1 (alergias; script `0094`, PR #285) fase 2 (enfermedades e intervenciones; script `0096`, PR #291) y fase 3 (medicación habitual; script `0097`) construidas.** Lo que cambió al construir, tras la revisión del esquema por el DPO: los textos de la alergia y de la reacción caben en 100 y 150 caracteres (una sola línea), los elementos solo se añaden en la misma transacción que creó su versión, la lectura trata como «ficha con error» una versión sin todos sus elementos, una corrección deja su propia acción de auditoría, y Dirección Clínica no lee la ficha en la fase 1 (la decisión 9 llegará con su lectura con finalidad). Antes, la propuesta: Lo valida una persona antes de usarlo con datos reales (lo jurídico, un abogado o el DPO real; lo clínico, CJ). Resume y concreta tres fuentes: las respuestas de CJ (2026-11-14 y 2026-11-15, guardadas fuera del repositorio), la lectura del DPO ([`ficha-alergias-enfermedades-medicacion-lectura-dpo.md`](../privacidad/ficha-alergias-enfermedades-medicacion-lectura-dpo.md), con su numeración de apartados) y las decisiones de Orlando del 2026-11-15. No contiene datos de personas reales: los ejemplos son inventados.

## 1. Qué es

Una **ficha por residente con tres apartados**, que se pide siempre en el alta: **alergias e intolerancias a medicamentos**, **enfermedades e intervenciones** (con año) y **medicación habitual** (nombre, dosis y pauta). Es una **lista informativa**: ResidApp no registra qué se administra ni cuándo, no avisa de alergias ni de interacciones y no decide nada por el personal. Sirve para el día a día y se copia en el informe de derivación a Urgencias. No se muestra a la familia, ni al Auxiliar, ni a Administración, ni a Plataforma; ninguna función de IA la lee (ADR 0011).

Ya decidido por CJ y por Orlando (no se reabre aquí): obligatoria en el alta pero **nunca bloquea** nada (ni el alta, ni el basal, ni una derivación); **sin revisión periódica ni caducidad**, solo se actualiza cuando ocurre algo; alergias con cuatro opciones (alergia sí, no refiere, no se sabe o no consta, intolerancia o efecto adverso); el informe imprime distintos «No refiere» y «No consta» (aclaración 1.1); Enfermería la rellena y la cambia y **Medicina también puede rellenarla y cambiarla** (aclaración 1.2); va **en la base**, no en el módulo «Medicamentos»; se construye **por fases** (alergias, enfermedades, medicación la última); centro piloto privado y pequeño como supuesto de trabajo.

## 2. Valores por defecto para lo que falta

Son propuestas de ingeniería, elegidas por ser lo más prudente y lo más fácil de cambiar después. Cada una dice qué cuesta cambiarla si CJ discrepa. CJ las verá al probar la pantalla y puede corregirlas; no se le pide ahora un documento nuevo.

| # | Decisión | Valor por defecto | Por qué | Si CJ discrepa |
|---|---|---|---|---|
| 1 | **Frase del informe** (hoy «revisado el… por…», que sería falsa si nadie revisa) | «Registrado en la ficha del residente; **última modificación el (fecha) por (perfil)**. Esta ficha no se revisa de forma periódica. Contrastar con la hoja de tratamiento oficial del centro y con el residente o su familia.» Una línea por apartado, con su fecha. Se imprime el perfil, no el nombre | Es verdad sea cual sea el uso, y conserva la cautela que CJ aceptó (contrastar con la hoja oficial) | Es solo texto; cambia en un sitio |
| 2 | **Alergia e intolerancia a la vez** | **Pueden convivir.** Cada elemento de la lista es «Alergia» o «Intolerancia o efecto adverso». El estado «No refiere alergias a medicamentos» admite intolerancias, pero no alergias. Los cuatro botones de CJ se conservan en pantalla | En la práctica ocurre («no refiere alergias, pero el paracetamol le da náuseas») y, si no, se perdería el dato | Cambia una regla de validación, no el esquema |
| 3 | **«Ninguna enfermedad conocida» y «no toma medicación habitual»** | Cada apartado tiene tres estados: **con entradas**, **«Refiere que no hay»** («Sin enfermedades ni intervenciones conocidas» / «No toma medicación habitual») y **«No se sabe o no consta»**. «Pendiente» (nadie lo ha rellenado) no es un estado guardado: es que no hay ninguna versión | Es la misma lógica que el cuarteto de alergias; evita que «nadie ha mirado» se lea como «ninguna» | Añadir o quitar un estado es un cambio pequeño antes de construir |
| 4 | **Alergias no medicamentosas** (látex, alimentos, contrastes) | **No, solo medicamentos.** La etiqueta pone siempre «alergias **a medicamentos**» | Es lo que pidió CJ y lo que acepta el DPO; ampliar exigiría decidirlo expresamente | Añadir un tipo de elemento después no rompe lo guardado |
| 5 | **Qué es «medicación habitual»** | Lo que consta en la receta o la hoja de tratamiento o que el residente toma de forma regular: incluye las pautas «si precisa», parches, inhaladores, insulinas y suplementos. Nombre, dosis y pauta en texto corto (ejemplo inventado: «Paracetamol 1 g, cada 8 h»). **No se anota** si el residente toma menos de lo pautado (adherencia), ni vía, fecha de inicio o fin, prescriptor o motivo | Es lo que describe CJ (ejemplo 2.9), y lo que sale de ahí convertiría la lista en administración (DPO, 5.2) | Se amplía el texto de ayuda; el esquema no cambia |
| 6 | **Recordatorio al cerrar un evento con derivación** | **No.** No hay recordatorio, ni aviso, ni marca | CJ ha dicho que revisar «es carga de trabajo poco productiva»; el DPO lo clasifica como recomendable, no exigido | Es una pantalla más; se puede añadir sin tocar datos |
| 7 | **Reacción en la alergia** | **Opcional** en «Alergia» y **obligatoria** en «Intolerancia o efecto adverso» (CJ la pidió ahí, con el motivo) | CJ lo pidió para la intolerancia; en una alergia, apuntarla ayuda y no obligarla evita rellenar por trámite | Es un `CHECK` |
| 8 | **Residentes ya registrados antes de la ficha** (todos los de prueba y los que se carguen el día del piloto) | Quedan en **«pendiente»**: la pantalla del residente lo muestra con un aviso discreto («Ficha de antecedentes pendiente»), no bloquea nada y **no se rellena nada de forma automática ni con datos del basal**. El informe imprime «Ficha pendiente de rellenar en este centro: no hay datos registrados. No equivale a “sin alergias”» | Rellenar solo sería inventar; no hace falta script de migración de datos | — |
| 9 | **Quién ve la ficha** | Enfermería y Medicina, en su ámbito. Dirección Clínica solo con el permiso de lectura clínica, la finalidad declarada y la auditoría previa que ya exige para el basal (un tipo de recurso clínico nuevo). Ni Auxiliar, ni Administración, ni Plataforma, ni la familia | Lo pide el DPO (apartado 2.1 y 2.2); coherencia con el basal | Conceder más es una decisión; quitar es fácil |
| 10 | **Quién edita** | Enfermería y Medicina, **con un permiso propio de edición** (apagado por defecto; lo concede Administración por perfil, como el basal). En el centro piloto lo dejaremos concedido a los dos perfiles | Art. 25.2 RGPD (apagado por defecto); CJ quiere que Medicina pueda rellenarla (1.2) | Es configuración |
| 11 | **Fuente del dato** | **Opcional**, por categoría de una lista cerrada («informe médico», «receta electrónica», «el propio residente», «familia o cuidadores»), nunca el nombre de quien informó | CJ no quiere un método rígido; el DPO pide categoría y no nombre | — |
| 12 | **Historial** | Cada guardado crea una **versión nueva** y no se pisa la anterior (versión, cuenta, perfil, fecha). Las versiones anteriores solo se ven en una pantalla de historial con permiso, no en las pantallas ordinarias ni en el PDF | Llei 21/2000, art. 9.3; art. 32 LOPDGDD (el DPO real debe confirmar que basta) | — |

## 3. Cómo se construye

**Modelo (a nivel de diseño, sin SQL todavía).** Es el mismo patrón que el basal y los informes de derivación: tablas de **solo añadir**, versionadas, con seguridad por filas por centro.

- **Una cabecera por apartado y por guardado**: residente, apartado (alergias, enfermedades, medicación), número de versión, estado (con entradas / refiere que no hay / no consta), fuente opcional, motivo del cambio (lista cerrada), cuenta, perfil y fecha. «Pendiente» = no hay ninguna cabecera.
- **Una fila por elemento** de cada versión: texto corto con límite de longitud y los campos propios (tipo alergia/intolerancia y reacción; año en enfermedades; dosis y pauta en medicación). Nada de columnas «por si viene una base de datos de fármacos».
- **Reglas que el propio SQL impone**: con entradas exige al menos un elemento; los otros estados, ninguno (salvo las intolerancias en el apartado de alergias, decisión 2 de la tabla); no hay estados por defecto; año no futuro; anulación por error como otra fila, no como un cambio.
- **Concurrencia**: si dos personas guardan a la vez, la segunda ve que ya hay una versión nueva y decide; no se pisa en silencio.
- **Informe de derivación**: al previsualizar se muestran las versiones de la ficha y, al firmar, se comprueba que siguen siendo las mismas (si no, se vuelve a mostrar). Lo que se imprime sale guardado en el contenido del propio informe, como el resto; no hace falta columna nueva.
- **Auditoría**: cada cambio deja una fila con solo identificadores (cuenta, perfil, residente, apartado, versión), sin el texto clínico.
- **Fuera del censo exportable y fuera de cualquier función de IA**; con una prueba que lo impida.

**Pantalla.** Un bloque «Antecedentes de salud» en la ficha del residente de Enfermería y de Medicina, con los tres apartados, el estado, la «última modificación» de cada uno y, si procede, el aviso «pendiente». Se pide en el alta de la persona pero no la bloquea. Textos en español, sin estados por defecto, accesible (AA) y según la guía de diseño; los iconos se reutilizan de los ya creados.

**Fases** (cada una es utilizable por sí sola):

1. **Fase 1 — Alergias e intolerancias.** Estructura común (versiones, permisos, auditoría, historial), apartado de alergias, aviso de pendiente, apartado en el informe, permisos, pruebas, política de privacidad ya corregida (PR #280), `_Derivacion.cshtml`, Manual y Guía por perfil. Es lo que pide el plan de la 0.4.0 («ayuda escrita + ficha de alergias»).
2. **Fase 2 — Enfermedades e intervenciones.** Mismo patrón, con año.
3. **Fase 3 — Medicación habitual.** La última y solo con los textos públicos ya corregidos (hecho en #280) y con la advertencia del DPO sobre exactitud. No hay administración, alertas ni cruces.

## 4. Lo que no entra

Antecedentes familiares; alergias no medicamentosas; adherencia; vía, fechas, prescriptor o motivo de la prescripción; stock, alertas, cruces o bloqueo de la prescripción por alergia (CJ lo apuntó como futuro: sería otra decisión, con su análisis regulatorio y su EIPD; apartado 6 de la lectura del DPO); dictado; IA; documentos o fotos de recetas; texto libre largo; revisión periódica; caducidad.

## 5. Antes de escribir el SQL de cada fase

- Consultar de nuevo al agente `dpo-residapp` con el esquema concreto, como exige la guía de trabajo.
- Verificar el siguiente script libre (a 2026-11-15 el último de `database/scripts/` es el `0093`; el siguiente sería el `0094`).
- Pedir a `doc-usuario-residapp` qué documentación hay que actualizar (la ayuda de `_Derivacion.cshtml:39-44`, que dice que ResidApp no guarda estos datos, el Manual, la Guía por perfil y las guías de CJ).
- Datos de prueba, semillas, capturas y ejemplos del Manual, **solo inventados** (el repositorio es público).
- Pasar el agente `qa-revisor-residapp` al cerrar cada fase.

## 6. Lo que sigue abierto fuera de este diseño

Validación por un abogado o el DPO real (base jurídica, si la residencia es centro sanitario, plazo de conservación, bloqueo de versiones, auditoría de lecturas); tipo de centro piloto (AEPD o APDCAT); información a residentes y familias; contrato de encargado con la categoría nueva; si ResidApp con esta ficha pasaría a ser un sistema de historia clínica electrónica del Reglamento (UE) 2025/327; decisión de Orlando sobre fusionar el PR de release 0.4.0 (ver el relevo). Detalle en `docs/privacidad/antes-de-datos-reales.md`, apartado 11.
