# Aviso a CJ de que hay notas de ingeniería nuevas: lectura de privacidad (corta)

**BORRADOR del agente `dpo-residapp`. Lo valida una persona antes de usarlo con datos reales.** Fecha: 2026-11-11. Segunda lectura experta, no un dictamen.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija Orlando, CJ o un DPO o abogado.

## Conclusión

**Admisible, con condiciones.** Es un correo de coordinación de trabajo entre dos personas del equipo, en desarrollo, sin datos de residentes, familiares ni profesionales de ninguna residencia. El único dato personal es la dirección de correo de CJ (y el hecho de que trabaja en el proyecto). El riesgo más importante no es de datos: es que el correo parezca un aviso de «entra y escribe tu clave» (patrón de suplantación) y que el servicio se encienda por error en producción, donde `/pendientes-cj` no debe existir.

## Respuestas

1. **Base y deber de informar.** [Suposición] Quien decide fines y medios es Orlando (o la sociedad futura) para su propio proyecto; esto no cae en el ADR 0007 (residencia responsable, ResidApp encargada) porque no trata datos de la residencia. Base razonable: art. 6.1.f RGPD (interés legítimo en coordinar el trabajo) o 6.1.b si hay acuerdo de colaboración con CJ; lo más limpio y barato es que **CJ lo pida o lo acepte por escrito** (basta un mensaje) y que se le diga qué se guarda. Art. 13: una frase en la hoja de acceso de CJ o en el pie del correo (quién es responsable, para qué, cómo dejar de recibirlo). No hace falta nada más que en el aviso a Orlando, salvo que **Orlando sí es el responsable de su propia dirección y CJ es un tercero**: por eso aquí sí conviene dejar constancia de la aceptación. Dirección: mejor una **personal o de proyecto** que la del hospital o centro donde trabaja (evita mezclar con un empleador sanitario, filtros ajenos y que el dominio delate dónde trabaja). [Decidir CJ]. Si fuera comunicación comercial entraría la LSSI; esto es un aviso de servicio al colaborador: a verificar con abogado, no cito artículo de memoria.
2. **Minimización.** Adecuado: nombres de documento y enlace. Recomendaciones: asunto neutro sin el nombre «CJ» ni palabras clínicas (se ve en pantallas de bloqueo); el cuerpo no nombra a CJ ni a Orlando; sin texto de notas ni respuestas ni cifras de «completadas». Pie propuesto: «Aviso del entorno de pruebas de ResidApp. No contiene datos de pacientes. ResidApp nunca te pedirá tu clave por correo: si no esperabas este aviso, no entres. Para dejar de recibirlo, díselo a Orlando (este buzón no recibe respuestas).» El enlace va en texto visible, sin parámetros ni clave.
3. **Dirección y estado.** El ajuste de la Web App más la lista de permitidas basta como control técnico (no sale del repo, `AvisoRespuestasCj.cs:13` y `66-68`) [Hecho]; no sustituye al consentimiento de CJ ni se lee como registro de él. Estado en fichero: solo nombre de documento, huella SHA-256 de las notas (públicas) y hora. **No** guardar: dirección, identificador del mensaje, texto, resultado con la dirección, IP. No poner la dirección en el log: hoy los avisos no la escriben (`AvisoRespuestasCj.cs:33`, `55`, `59`) y debe seguir así. La carpeta es la misma de las respuestas, fuera del paquete (`RespuestasCjStore.cs:17`, `48-62`) [Hecho].
4. **Documentación.** `Views/Home/Privacy.cshtml`: **no** hace falta; es la política de la residencia hacia residentes, familias y personal, y esto es de otro responsable y otra población (ya hay una línea de la clave de CJ en las líneas 176 y 182). `docs/privacidad/`: no existe hoy ninguna entrada del aviso a Orlando ni de este (búsqueda de «AvisoRespuestasCj» en `docs/privacidad`, sin resultados) [Hecho]: añadir al registro de actividades propio del proyecto (el de contactos y equipo, como en `landings-y-passkeys-por-subdominio.md:27`) una línea «correo de coordinación a Orlando y CJ». Mencionar el aviso a CJ en la hoja de acceso de CJ.
5. **Condiciones al construirlo.**
   1. Aceptación de CJ por escrito antes de poner su dirección; retirar el ajuste cuando termine su colaboración.
   2. No enviar si el entorno es Producción aunque el ajuste exista (hoy el worker no lo comprueba: `AvisoRespuestasCjWorker.cs:22-45`) [Hecho]. Producción no debe llevar `/pendientes-cj` (`entorno-produccion-lectura-dpo.md:219`).
   3. `UrlBase` fija de configuración, nunca `Host`; una sola URL por documento; sin `Reply-To`; un destinatario; seguimiento de aperturas apagado.
   4. Pruebas: el correo no contiene el texto de ninguna nota, ni respuestas, ni la dirección en el log ni en el fichero de estado.
   5. Dirección en `Correo__DireccionesPermitidas__N`; no usar `TodosLosDestinos` para esto.
   6. Notas siguen sin datos personales ni copias de comentarios de CJ (ya es regla del README, línea 58).

## A verificar o decidir

1. **[Decidir CJ]** Qué dirección usa y su aceptación escrita.
2. **[Decidir Orlando]** Si añade la línea al registro de actividades del proyecto y a la hoja de acceso.
3. **A verificar con abogado/DPO:** la base (6.1.f o 6.1.b) y si la LSSI aplica a este aviso.
4. **A verificar:** retención de los registros de entrega de ACS (30 días según ADR 0012, paso de Orlando aún sin hacer).
