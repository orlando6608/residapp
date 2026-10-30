# Antes de usar con residentes reales: lo que hay que hacer primero

**Para quién es:** Orlando, CJ y cualquier sesión de Claude que retome el trabajo. Es una lista de cosas por cerrar, no un dictamen.
Hoy estas funciones solo se usan con datos de prueba (la base de Azure es de desarrollo). Esta lista dice qué falta para que se puedan usar con residentes de verdad.

**Instrucción para Claude:** si Orlando habla de activar el centro piloto, de usar datos reales o de mergear algo que toque el historial tras un traslado, el cierre excepcional
o Calidad de proceso, **recuérdale esta lista antes** de seguir. No la des por cumplida sin que él lo diga.

## Las funciones a las que afecta

| Función | Script | Dónde está explicada |
| --- | --- | --- |
| Historial de los episodios cerrados tras un traslado | `0077` | Borradores `historial-tras-traslado-01…05` de esta carpeta |
| Cierre excepcional de un episodio por Dirección Clínica | `0075` | Nota `historial-tras-traslado-06-nota-retroactiva-0075-0076.md` |
| Permiso y pantalla de Calidad de proceso | `0076` | La misma nota 06 |
| La baja cancela las citas futuras y cierra las solicitudes de cita del residente | `0078` | Más abajo, apartado 4 |
| Respuestas de CJ a Dirección y traslados: finalidad del motivo, lista de peticiones, constancia de consulta, Calidad sin nombres | `0079` | Más abajo, apartado 5 |
| Landings en castellano y catalán, dominios propios y Portal Familiar en dos idiomas (aún sin construir) | — | Más abajo, apartado 6 |

## 1. Que una persona valide los borradores

Los documentos de esta carpeta (`historial-tras-traslado-01…06`) los redactó el agente `dpo-residapp`: son una segunda lectura experta, **no** un DPO designado ni un abogado.
Hasta que **el DPO real de la residencia o un abogado** los lea, corrija y dé por buenos, no se usan con datos reales.

Qué tiene que mirar esa persona (cada borrador deja sus decisiones abiertas al final, en «A verificar o decidir»):
- el registro de actividades de tratamiento y el anexo a la evaluación de impacto (los análisis de riesgos que exige el RGPD cuando se tratan datos de salud);
- el texto informativo para residentes, representantes y personal;
- el procedimiento de revisión de las lecturas `TRANSFER_HISTORY_READ` (quién, cada cuánto);
- cuánto se conserva el motivo que escribe quien pide el historial y cómo se borraría, porque la tabla no admite cambios;
- los plazos legales de conservación de la documentación clínica en Cataluña, que el agente no pudo confirmar en fuente oficial y dejó «a verificar»;
- qué autoridad de control corresponde (AEPD o APDCAT) según el tipo de centro piloto.

## 2. Que Orlando y CJ decidan las mejoras que propone el agente

Son cambios en la aplicación que el agente aconseja y que **no están hechos**. Cada uno está explicado en `docs/tareas/alta-prioridad/pendientes-enfermeria.md` («Tras un traslado entre unidades»),
y la mayoría se han preguntado a CJ en `docs/pendientes-cj/archivados/direccion-y-traslados-confirmaciones-cj.html`:

1. **Hecho el 2026-10-28 (`0079`).** Una vista de Dirección con **todas las peticiones** del periodo. Hoy solo ve las lecturas residente por residente, y la pantalla dice «Dirección puede revisarlas». (Pregunta 3.3.)
2. **Hecho en parte el 2026-10-28 (`0079`):** el motivo con **lista cerrada** más texto corto opcional; las filas de lectura siguen copiando el texto. (Pregunta 3.2.)
3. **Registrar las peticiones rechazadas**, para detectar sondeos.
4. Un **límite de antigüedad** de los episodios que se pueden abrir. **CJ lo descartó el 2026-10-28** (3.1: sin límite; se mirará con datos de uso).
5. Una **vía de purga** del motivo (por ejemplo, sustituir el texto por una marca fija dejando constancia, como ya hace `0075`).
6. **Hecho el 2026-10-28 (`0079`).** **Auditar la consulta de Calidad de proceso** de `0076` (2.3) y quitar el nombre del residente de la lista de hitos (2.2).

## 3. Condiciones abiertas de `0075` y `0076`

Están en la nota 06. En resumen: clasificar el motivo del cierre excepcional (historia clínica o auditoría) y fijar su conservación, igualar su advertencia a la de `0077`,
confirmar con un abogado que Dirección Clínica puede cambiar el estado de un episodio, y decidir si la consulta nominal de Calidad de proceso se audita.

## 4. La baja que cancela citas (`0078`)

Informe del agente `dpo-residapp` (2026-10-28): admisible con condiciones, y las del código están hechas (causa y motivo fijo en el historial, aviso antes de confirmar, alcance exacto, auditoría solo con identificadores). Lo que **no** es código y valida una persona:

- el texto informativo del Portal Familiar: que, al terminar la estancia, se cancelan las citas futuras y se cierran las solicitudes abiertas, y que el centro avisa por su canal habitual (lo adopta la residencia como responsable);
- quién avisa a la familia y por qué canal, con cuidado en la baja por fallecimiento (la aplicación no envía correos y no avisa);
- que atribuir la cancelación a la cuenta de Administración que dio la baja, con causa y motivo fijos, es adecuado como registro (y que no sirve para evaluar a esa persona);
- una línea en el registro de actividades («cancelación de citas por baja», datos solo de identificación) y el plazo de conservación de las filas nuevas (hoy el de la baja, 5 años, sin confirmar en fuente oficial).

## 5. Dirección y traslados: respuestas de CJ del 2026-10-28 (`0079`)

Informe del agente `dpo-residapp` (2026-10-28): admisible con condiciones. **Hecho en el código:** Calidad de proceso sin nombres (ni se leen), motivo con finalidad cerrada y texto de 150 como máximo (el tope lo fija CJ), lista de peticiones ordenada por fecha, sin ranking por cuenta ni exportación, constancia previa a entregar la lista, alcance por unidad del residente, y la frase «para detectar accesos indebidos, no para valorar el desempeño». **Lo que valida una persona:**

- la base jurídica del registro de quién consulta Indicadores, Calidad y la lista (control de accesos y seguridad: ¿obligación legal o interés legítimo?);
- el aviso por escrito a las Direcciones con el permiso y al personal que pide historiales, y a su representación si procede (asesoría laboral);
- quién revisa la lista de peticiones, cada cuánto, y que quien revisa no se revise a sí mismo;
- que el texto opcional del motivo siga copiándose a las filas de lectura (riesgo residual medio; la purga tendría que cubrir todas) o se deje solo la etiqueta;
- el plazo de conservación de las filas nuevas (siguen el de la auditoría, sin decidir) y, para las cifras pequeñas de Calidad por unidad y periodo, anotarlo en la evaluación de impacto.

**Borradores de esta carpeta que quedan desactualizados** (no se han reescrito): `historial-tras-traslado-01` (datos, destinatarios y la pantalla nueva), `-02` (riesgos 1, 2 y 5), `-03` (parte 2: qué escribir, quién lo revisa y qué queda registrado, y el aviso del registro de Indicadores y Calidad), `-04` (puntos 2, 3 y 5 y la pregunta 4), `-05` (la columna nueva) y `-06` (0076: nombres y auditoría resueltos, cifras pequeñas abiertas; 0075: la derivación a Urgencias ya no se cierra así).

## 6. Dominios, landings y portal en dos idiomas (decisiones de Orlando del 2026-10-29)

Informes del agente `dpo-residapp` del 2026-10-29: `landings-y-passkeys-por-subdominio.md` y `portal-familiar-dos-idiomas.md`, ambos de esta carpeta (borradores, los valida una persona). **Hoy no hay nada construido de esto**, pero si se construye hay que cerrar antes de usarlo con familias reales:

**Landings (`residapp.es` y `residapp.cat`) y dominios** (propuesta del agente, a decidir por Orlando):

- la política de privacidad, el aviso legal y la declaración de cookies de las dos landings, redactados y validados en castellano y en catalán, con el titular de la web identificado (persona o sociedad, NIF y domicilio) y la versión que prevalece si discrepan;
- ningún recurso de terceros en las landings ni en las pantallas de entrada, **tampoco imágenes ni letras** (hoy la maqueta carga Tailwind desde jsdelivr y fotos de Unsplash, lo que envía la IP de los visitantes a terceros; Orlando decidió el 2026-10-29 que todo sea local) y ningún enlace con rastreo ni analítica de terceros;
- precios y planes publicados (decidido el 2026-10-29): importes y módulos fijados, que cada módulo anunciado exista en la aplicación o diga «próximamente», condiciones de contratación e IVA, y el contrato de encargado con cada centro; qué obligaciones hay al publicar precios en una web B2B queda para un abogado;
- FAQ de las landings contrastada con la aplicación y con las frases prudentes de `analisis-landing-page.html` (sección 8), y validada por una persona experta en lo que toca a seguridad, protección de datos y dictado por voz;
- chat de las landings: solo un asistente guiado sin IA (sin texto libre ni servidor); un bot con IA conversacional exige antes un ADR nuevo (o ampliar el 0011) y la revisión del DPO real, porque hoy el único caso de uso de IA aprobado es el dictado;
- las landings se rediseñan con la guía visual del proyecto (`docs/bocetos-pantallas/guia-diseno-sistema-visual.md`), no contradicen lo que hace u ofrece la aplicación y no enlazan al Manual ni al Flujo;
- el botón «Acceso clientes» oculto hasta que exista `app.residapp.es`; `dev.residapp.es` nunca enlazado desde una web pública y sin indexar;
- `app.residapp.es` creado con su DNS, el registro `TXT` de verificación (`asuid`), HSTS sin `includeSubDomains` ni `preload` en el dominio raíz y cabeceras de seguridad y de referencia cerradas; ningún DNS colgante de un subdominio cuyo recurso de Azure ya no exista;
- una línea nueva en el registro de actividades para los contactos comerciales de la landing (ResidApp como responsable, no la residencia) y su plazo de conservación (el análisis de la landing propone 6 a 12 meses, sin contrastar en fuente oficial);
- la política de la aplicación (`Views/Home/Privacy.cshtml`, hoy «sin redactar») adoptada por cada residencia como plantilla.

**Portal Familiar y entrada en castellano y catalán** (ADR 0013, aceptado el 2026-10-30; aún sin construir):

- la declaración de cookies y la política de privacidad del Portal Familiar validadas en castellano y en catalán, con la cookie de idioma recogida;
- la cláusula informativa al familiar sobre el campo de idioma (qué se guarda, para qué, que es voluntario, cómo cambiarlo, que no se deduce, qué versión prevalece), validada por el DPO real;
- la decisión del DPO real sobre la sugerencia al personal (sin cifras, o la alternativa de ofrecer siempre «redactar también en catalán») y sobre la base jurídica del campo (art. 6.1.b o 6.1.f);
- las plantillas de correo (ADR 0012) en catalán revisadas por una persona que sepa catalán, con el aviso contra fraude en los dos idiomas;
- el registro de actividades con el idioma preferido del familiar, el idioma por defecto del centro y la cookie técnica, y la misma conservación para el idioma anotado en el registro de envíos de correo;
- **a verificar con un abogado:** el art. 22.2 de la LSSI y la guía de cookies de la AEPD en fuente oficial (el agente solo vio una nota de prensa y comentarios de despachos), el tope de caducidad de una cookie de idioma exenta, si el art. 128-1 del Codi de consum exige la información legal en catalán en una web B2B, si los derechos lingüísticos en Cataluña obligan a la residencia a atender en catalán, y si la autoridad de control es la AEPD o la APDCAT según el centro piloto.

## Cómo se marca como hecho

Cuando una persona valide los borradores y se decidan las mejoras, anótalo aquí con la fecha y quién lo hizo, y quita el recordatorio del traspaso (`docs/prompts-ia/continuar-construccion.md`)
y de la memoria de Claude. Mientras tanto, no se borra nada de esta lista.
