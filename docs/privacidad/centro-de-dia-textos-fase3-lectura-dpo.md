# Centro de Día, fase 3: textos de cara a personas (política, correos y casillas)

**BORRADOR del agente `dpo-residapp`. NO está validado por una persona experta (DPO real o abogado). No usar con datos reales hasta que lo valide.** Fecha: 2026-11-24. Segunda lectura, no un dictamen. Rama `fase3-vocabulario-por-tipo` (PR #392). No modifica código, ADR ni otros documentos.

Etiquetas: **[Hecho]** verificado en el repositorio o en `normativa-local/`; **[Suposición]**; **[Opinión]**; **[Decidir]** lo fija una persona; **[A verificar]**. Normativa: `rgpd.txt` (consolidada a 2016-05-04, texto consolidado, no oficial), comprobada en `normativa-local/INDICE.md` el 2026-11-20 21:41 (menos de una semana). No incluyo datos reales ni nombres de centros.

Términos: **responsable** = quien decide para qué y cómo se usan los datos (el centro); **encargada** = ResidApp, que trata por cuenta del responsable; **art. 13** = la información que el centro da a quien le entrega sus datos; **art. 14** = la que da a quien aparece en sus datos sin haberlos dado él; **neutro** = escrito para que valga igual en una residencia y en un centro de día.

## Conclusión

**Admisible con condiciones.** La fase 3 no crea tratamientos nuevos de personas, así que no abre obligaciones nuevas de los arts. 13 y 14: se puede seguir con datos de prueba. Pero **antes de usar un Centro de Día con personas reales hay que corregir la política de privacidad** (`Privacy.cshtml`), porque hoy identifica como responsable a «la residencia» y describe datos («habitación o plaza») que un Centro de Día no recoge. Es un defecto de transparencia (art. 5.1.a y 12.1), no un riesgo para los datos.

Mi recomendación, por orden:

1. **Política de privacidad: texto neutro, no dinámico.** Es una página pública sin sesión: no hay centro del que leer el tipo. Y es un texto legal que debe ser el mismo para todos y versionable.
2. **Correos: no adaptarlos. Mantenerlos neutros.** Adaptarlos sería un retroceso en privacidad (ver apartado 2).
3. **Casillas: bastan las cuatro para la fase 3**, con tres ajustes menores de redacción (apartado 3). No añadir casillas.
4. **Lo que sí pide decisión humana:** quién es el responsable que consta en cada centro, cómo se entrega la información art. 13 en un centro de día, y la terminología «residente» en textos legales (apartado 4).

## 1. Política de privacidad (`Privacy.cshtml`)

### 1.1 Qué dice hoy y por qué no vale tal cual para un Centro de Día

- **Responsable.** Línea 20: «El responsable de tus datos es el centro (la residencia)…»; línea 30: «El centro (la residencia) es el responsable del tratamiento». Un Centro de Día no es «la residencia». El art. 13.1.a exige «la identidad y los datos de contacto del responsable» (`rgpd.txt:156`) y el art. 12.1 «un lenguaje claro y sencillo» (`rgpd.txt:139`). Llamar «residencia» a quien es un centro de día no cambia quién es el responsable, pero confunde al lector sobre quién es y puede llevar a dirigir mal un ejercicio de derechos.
- **Datos que no se recogen.** Línea 48: «unidad y habitación o plaza»; línea 19 y la 2.1: «residencias de personas mayores», «personas residentes». En un Centro de Día no hay habitación. Decir que se trata un dato que no existe es inexacto (art. 5.1.a, línea 73).
- **Lo que ya vale para ambas modalidades** [Hecho]: roles (apartado 1: centro responsable, ResidApp encargada), subencargados (apartado 9), cookies (apartado 10), seguridad, derechos (apartado 12). No hay nada que cambiar ahí.
- **Lo que no hay que añadir todavía:** asistencia, horarios, dietas, parte diario y personas que recogen. No existen (fase 5). La página dice «Si una función no está aquí, es que no existe» (línea 43): añadirlos antes de construirlos sería inventar tratamientos.

### 1.2 Neutro, no dinámico

[Opinión] **Neutro**, por tres razones:

1. **No hay centro conocido.** La página se ve sin iniciar sesión (línea 82: «se pueden ver sin iniciar sesión»). Un visitante, una persona que está valorando la residencia o un familiar con un enlace no tienen centro activo.
2. **Una persona con cuentas en dos centros** (una residencia y un centro de día de la misma entidad, apartado 2.6 de la lectura previa) vería un texto u otro según la cookie del ámbito. Un texto legal que cambia según una cookie dificulta saber qué se mostró a quién y cuándo.
3. **Es lo que el centro debe controlar.** La información art. 13 completa (identidad, DPO, plazos, base jurídica) es del centro (la política ya lo dice, línea 30: «Cada centro tiene su propia información de privacidad»). La página general solo debe ser exacta y no contradecirla.

Propuesta de redacción (no la aplico; la aplica la sesión principal tras validarla una persona):

| Línea | Hoy | Propuesta neutra |
|---|---|---|
| 19 | «una aplicación que usan las residencias de personas mayores» | «una aplicación que usan los centros de atención a personas mayores (residencias y centros de día)» |
| 20 y 30 | «el centro (la residencia)» | «el centro que te dio acceso o que atiende a tu familiar (una residencia o un centro de día)». Mantener «el centro», que ya es el término del resto de la página |
| 21, 45 y ss. | «personas residentes» | «personas atendidas (residentes en una residencia o usuarias de un centro de día)», definido una sola vez; después «las personas atendidas» o «residentes» si Orlando mantiene el término |
| 48 | «unidad y habitación o plaza» | «unidad y, si el centro es una residencia, habitación o plaza» |

**Qué no cambia el texto neutro:** la base jurídica del apartado 3 ya remite a cada centro; la autoridad del apartado 13 sigue entre corchetes («AEPD o APDCAT, según el tipo de centro»). Para el piloto privado la hipótesis de trabajo es AEPD (lectura previa, apartado 6.2), **[A verificar]** por la persona experta.

### 1.3 Información art. 13 y 14 que cambia con la modalidad (para después)

- **Fase 3: nada nuevo.** Los datos son los mismos que ya tiene una residencia; solo cambia el vocabulario.
- **Fase 5 (asistencia, parte, personas que recogen):** entonces sí. Las **personas que recogen** no dieron sus datos ellas: art. 14. Hay que informarles «dentro de un plazo razonable, y a más tardar dentro de un mes» o «a más tardar en el momento de la primera comunicación» (art. 14.3, líneas 194-195), salvo que ya dispongan de la información (14.5.a, línea 199). Esto ya está en la lectura previa (apartado 2.3) y no se resuelve con la política general, sino con una hoja del centro.
- **Quién la entrega** [Decidir]: el centro, a la familia, en el alta. ResidApp aporta la plantilla.

## 2. Correos de citas externas y recordatorios

### 2.1 Qué hacen hoy [Hecho]

Las cuatro plantillas (`ProposalNoticeTemplate`, `ChangeNoticeTemplate`, `ReminderTemplate`, `ExternalNoticeTemplate`, en `src/ResidApp.Application/Email/`) son genéricas a propósito: no reciben residente, centro, unidad, motivo ni fecha (comentarios de cabecera, p. ej. `ProposalNoticeTemplate.cs:11`). La única mención al centro está en frases comunes del final: «Si necesitas algo, llama al centro» (`ChangeNoticeTemplate.cs:30`, `ReminderTemplate.cs:30`, `ProposalNoticeTemplate.cs:40`, `ExternalNoticeTemplate.cs:32` y `:44`), «díselo a la Administración del centro» (`ExternalNoticeTemplate.cs:82`) y «el responsable de tus datos es el centro que la registró» (`ProposalNoticeTemplate.cs:24`). **No dicen «residencia»** [Hecho, comprobado con búsqueda por «residencia» y «centro» en las cuatro plantillas]. Por tanto, **un Centro de Día no recibe hoy un texto inexacto**: «el centro» vale para ambas modalidades.

### 2.2 Recomendación: neutros, no adaptados [Opinión]

1. **Adaptarlos filtraría la modalidad.** Un correo que dijera «centro de día» (asunto, vista previa o cuerpo) diría a quien lo vea, o a cualquiera con acceso al buzón, que esa persona usa un servicio de atención diurna. La lectura previa (apartado 1) lo trata como dato de salud por inferencia: revela dependencia o necesidad de apoyo. El ADR 0012 ya decidió que ningún correo dice nada de salud (lectura previa, apartado 3.3). Es la misma razón por la que no llevan el nombre del centro ni del residente.
2. **Exigiría cambiar el diseño.** Hoy la plantilla no recibe el centro. Pasarle el tipo de servicio rompería la regla «ni centro, ni residente», y los correos corren fuera de la petición (sin ámbito), lo que obligaría a leer el centro desde la base en cada envío.
3. **Ganancia nula.** «El centro» es verdad en las dos modalidades y el lector ya lo asocia a su centro.

**Condición:** que quede escrito en el ADR 0012 o en el 0017 que las plantillas son **neutras respecto a la modalidad por diseño** (y una prueba que falle si una plantilla incluye «residencia» o «centro de día»), para que una futura adaptación del vocabulario no las toque.

### 2.3 Lo que sí hay que mirar [A verificar]

- **Pie de la propuesta** (`ProposalNoticeTemplate.cs:24`): «dirígete al centro. En el apartado «Privacidad» del portal tienes la información completa». Remite a la política general: por eso la corrección del apartado 1 es necesaria antes de un Centro de Día real.
- **Asunto «Tienes una cita mañana en ResidApp»** (`ReminderTemplate.cs:24`): neutro. En un Centro de Día la cita del día anterior puede ser una cita externa del usuario; el correo de citas externas ya es distinto (`ExternalNoticeTemplate.cs:8-9`). No cambia nada por la modalidad.
- **Idioma:** si llega el catalán (traspaso en `docs/tareas/alta-prioridad/pendientes-catalan.md`), «el centro» se traduce igual en ambas modalidades; no hay razón para dos plantillas.

## 3. Las casillas de `AjustesFamilia.cshtml`

Cuatro confirmaciones que el centro marca antes de abrir las citas externas a las familias y los avisos por correo: `informada` (línea 69), `voluntad` (línea 73), `eipd` (línea 77) y `correo-informada` (línea 120). Con `CenterVocabulary.TheServiceStart` dicen «El centro de día…» o «La residencia…».

**Conclusión: suficientes para esta función y esta fase.** Son la **constancia de que el responsable ha hecho lo que le toca** (principio de responsabilidad proactiva): no sustituyen su información a las personas, ni su EIPD, ni su procedimiento. Cubren lo mismo en las dos modalidades: informar (art. 13), voluntad del residente (Ley 41/2002, art. 5.1) y EIPD (art. 35). El correo tiene su casilla aparte.

Ajustes menores [Opinión]:

1. **«ya ha informado a los residentes y a sus familias»** (línea 69): en un Centro de Día el usuario tiene más probabilidad de conservar capacidad y la familia es más plural. Conviene decir **«a las personas atendidas, a sus representantes y a sus familias»** en ambos tipos (es más exacto también para la residencia). No hace falta que sea distinto por modalidad.
2. **«voluntad de cada residente»** (línea 73): cubre también el nivel «por familiar» que la lectura previa recomendaba (apartado 2.4). Hoy el interruptor es por residente y «todo o nada». No cambia la casilla: avisa de que sigue pendiente la decisión.
3. **«ha actualizado su EIPD»** (línea 77): para un Centro de Día, la EIPD es la **del propio centro de día**, no la de la residencia de la misma entidad. La casilla dice «El centro de día», así que es coherente. Una persona experta debe confirmar si una sola EIPD cubre ambos centros de la misma entidad (art. 35.1: «una única evaluación podrá abordar una serie de operaciones de tratamiento similares»). **[A verificar]** en `rgpd.txt`, art. 35.1; no lo he releído hoy porque no cambia la conclusión de la casilla.
4. **Enunciado de la leyenda** (línea 66, «estas tres cosas»): es correcto (la casilla del correo es otro formulario, línea 120). Sin cambio.

**Qué no cubren ni deben cubrir:** que la información se entregó de verdad, que el procedimiento existe o que la EIPD es adecuada. Las casillas solo guardan quién las abre, cuándo y con qué confirmaciones (línea 80). Si la persona experta quiere evidencia más fuerte, es un documento firmado por el centro, no más casillas.

## 4. Qué debe decidir una persona

**Orlando:**

1. **¿Política neutra (recomendada) o por tipo?** Si es por tipo, la página deja de ser pública y única y hay que decidir qué se muestra a quien no tiene sesión.
2. **¿«Residente» se mantiene en los textos legales** (política, cláusulas, información a la familia)? La decisión del 2026-11-19 (`CenterVocabulary.cs:5`) lo mantiene en las pantallas. Mi opinión: en lo que lee la familia o la persona atendida, «persona atendida» o el nombre del centro; la lectura previa (apartado 2.3) ya lo recomendaba. Es de producto.
3. **¿Se declara por escrito (ADR 0012 o 0017) que los correos son neutros respecto a la modalidad?** Lo recomiendo.
4. Los textos estáticos de cara a la familia (`Guia/familia.json`, `Manual.cshtml`) dicen «residente» y «residencia» sin adaptarse. No es un texto legal ni cambia con la modalidad. Prioridad baja, pero no los he leído enteros.

**Persona experta (DPO real o abogado):**

1. Que la política neutra identifica bien al responsable de un Centro de Día (art. 13.1.a).
2. Cómo entrega el centro la información art. 13 a usuarios y familias, y art. 14 a las personas que recogen (fase 5d).
3. Si una EIPD de entidad cubre residencia y centro de día, o hace falta una por centro.
4. Base jurídica del art. 9.2.h para un servicio social privado y autoridad de control del piloto (ya en la lectura previa, apartados 6.2 y 6.3).

## A verificar o decidir

- **[A verificar]** art. 35.1 RGPD sobre «una única evaluación» (no releído hoy; lo cito de la lectura previa).
- **[A verificar]** que `Guia/familia.json`, `Manual.cshtml` y los PDF no tienen texto legal sobre responsable o base jurídica que haya que neutralizar. No los he recorrido.
- **[A verificar]** que no existen más correos o textos fuera de `src/ResidApp.Application/Email/` (busqué por esa carpeta y por las dos plantillas citadas, no por todo `src`).
- **[Decidir]** apartado 4.
- **No incluí** la versión catalana de la política; la pido si hace falta.
- **No hay una tabla de qué cambia en la fase 5**; está en `centro-de-dia-lectura-dpo.md`, apartado 4.1.
