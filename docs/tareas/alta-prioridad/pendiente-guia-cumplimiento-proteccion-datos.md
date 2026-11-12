# Pendiente: guía de cumplimiento de protección de datos para los propietarios de ResidApp

**Estado: pendiente** (anotada el 2026-11-12, a petición de Orlando). No está empezada.

## Qué se pide

Un documento guía, **en lenguaje llano y dirigido a los propietarios de la plataforma (Orlando y CJ)**, que explique los pasos a seguir para cumplir con la protección de datos en todos sus aspectos. No es un texto jurídico: es la hoja de ruta que permite a dos personas que no son juristas saber qué hay que hacer, en qué orden, quién lo hace y cuándo hace falta una persona experta. Cada término técnico se explica con una frase corriente (regla del proyecto).

## Qué debe cubrir

1. **Los documentos necesarios.** Cuáles son (política de privacidad, declaración sobre cookies, cláusulas informativas a residentes, familias y personal, registro de actividades de tratamiento, evaluación de impacto, contrato de encargado y anexo de módulos, protocolo de brechas, etc.), para qué sirve cada uno, quién lo redacta, quién lo valida, dónde está el borrador y en qué estado.
2. **Cuándo hace falta una persona experta.** Si hay que contratar un abogado especializado o un delegado de protección de datos (DPO), con qué fin concreto (validar contratos, decidir los puntos abiertos, ser contacto ante la autoridad), cuándo encargarlo y qué pedirle, con una lista de preguntas ya preparadas.
3. **Hasta dónde llega la responsabilidad de ResidApp** y cómo encaja con la de cada centro o residencia: quién es responsable del tratamiento y quién encargado, qué decide cada parte, qué le toca a ResidApp por sus propios datos (personal de la plataforma, contacto web, correos) y qué le toca por los datos de los residentes que trata por encargo del centro.
4. **Los nombramientos y figuras que exige la ley.** Cuáles son obligatorios en cada caso (DPO, responsable de seguridad si aplica el Esquema Nacional de Seguridad, a quién se comunica cada designación y en qué plazo), quién debería ser en ResidApp y qué hace cada centro por su lado.
5. **Atender reclamaciones y derechos de pacientes y familias.** Cómo recibe ResidApp o el centro una petición de acceso, rectificación o supresión, quién responde, en qué plazos, cómo ayuda ResidApp al centro, qué hacer si alguien se queja ante la autoridad de control (AEPD o APDCAT) y qué hacer ante una brecha (aviso en 72 horas).
6. **Una guía rectora de pasos** ordenada en tres momentos: antes de usar datos reales, antes del primer centro real y de forma continua (revisiones, cambios de ley, nuevos módulos, nuevos proveedores). Con casillas de «hecho / pendiente / decide una persona».

## Con qué se cuenta ya

- Borradores en `docs/privacidad/` (24 archivos), en especial `antes-de-datos-reales.md` (lista de lo que hay que resolver antes de usar la aplicación con residentes o familias reales) y `anexo-modulos-contrato-borrador.md` (quién es quién y qué falta).
- La base normativa local (`normativa-local/`, ver `docs/privacidad/normativa/README.md`), con el texto literal de las leyes y guías, y el agente `dpo-residapp` para redactar y citar de ahí.
- Los ADR 0014 a 0016 y sus lecturas del DPO.
- Los puntos que siguen abiertos y que solo decide una persona experta: si la residencia es «centro sanitario», AEPD o APDCAT y Esquema Nacional de Seguridad según el centro piloto, bloqueo frente a borrado de familiares, exención del art. 22.2 de la LSSI para la clave del aparato, tope de caducidad de la cookie de idioma. Ver la entrada de relevo del 2026-11-12 en `docs/prompts-ia/continuar-construccion.md`.

## Condicionantes

- **No existe todavía** la sociedad titular de ResidApp ni un DPO o abogado real; condicionan quién firma los contratos y quién es el contacto de privacidad.
- La guía **no sustituye** a un DPO ni a un abogado: dice cuándo acudir a ellos y qué preguntarles. Debe decirlo con claridad.
- Los textos legales se citan de la base normativa local (artículo y versión), no de memoria.

## Cómo hacerlo (propuesta)

1. Pedir a `dpo-residapp` un borrador a partir de los documentos de `docs/privacidad/` y de la base normativa, en lenguaje llano y sin repetir lo que ya dicen los borradores (enlazarlos).
2. Revisión de Orlando para claridad; decisiones suyas, de una en una.
3. Ubicación sugerida: `docs/privacidad/guia-cumplimiento-propietarios.md`. **Decidir** si también se publica como página en la sección «Para Orlando» (`docs/pendientes-cj/orlando/`) para que CJ la lea y responda con el formulario; si es así, hay que registrarla donde corresponda.
4. La valida una persona experta antes de darla por buena (como el resto de `docs/privacidad/`).

## Cuándo está terminada

Cuando Orlando y CJ pueden leerla sin ayuda y saben, para cada obligación, quién la cumple, con qué documento, en qué momento y cuándo hay que llamar a una persona experta; y cuando una persona experta la ha revisado.
