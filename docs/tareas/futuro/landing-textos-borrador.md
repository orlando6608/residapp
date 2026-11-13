# Landing: borrador de textos (castellano y catalán)

> **Citas a `archivados/…`:** esos documentos de CJ se retiraron del repositorio el 2026-11-11 (son citas históricas). Para leer uno tal como estaba: `git show 571c6cb:docs/pendientes-cj/archivados/<documento>` (ver `docs/pendientes-cj/README.md`, «Citas a `archivados/…` en otros documentos»).

**BORRADOR — no se publica tal cual.** Fecha: 2026-11-09. Lo valida una persona experta (DPO real o abogado) antes de publicar, sobre todo las filas de seguridad y privacidad, y el catalán lo revisa una persona catalanohablante (lo he escrito yo y puede tener errores).

Parte de [`pendientes-landing.md`](pendientes-landing.md) (decisiones de Orlando del 29/10 y correcciones del 09/11) y de las frases prudentes de la sección 8 del análisis (`analisis-landing-page.html`, archivado). Cada fila indica **dónde se ha comprobado** lo que dice. Si algo cambia en la app, cambia la fila en los dos idiomas.

Marcas: **[PENDIENTE]** lo decide una persona (no lo he inventado); **[VERIFICAR]** lo he leído en documentación pero no lo he visto funcionando.

## Reglas que siguen todos los textos

- Sin absolutos («siempre», «nunca», «total», «infranqueable», «impecable»), sin prometer inmediatez y sin nombrar a terceros que no sean necesarios.
- Solo lo que la aplicación hace hoy; lo que no existe, o no se dice o se dice «próximamente».
- La versión catalana dice lo mismo, traducido, y avisa de que **la aplicación está en castellano**.
- Los nombres de los botones de la aplicación se dejan en castellano, entre comillas, porque es lo que la persona verá en pantalla.
- Selector de idioma: un simple enlace entre `residapp.es` y `residapp.cat`. El botón «Acceso clientes» no se muestra hasta que exista `app.residapp.es`.

## 1. Cabecera y portada

| Id | Castellano | Català | Comprobación |
|---|---|---|---|
| H1 | Características · Planes · Preguntas frecuentes · Contacto | Característiques · Plans · Preguntes freqüents · Contacte | Menú. «Tarifas» pasa a «Planes» y «Seguridad (FAQ)» a «Preguntas frecuentes» porque la FAQ trata más temas que la seguridad. |
| H2 | ResidApp está en fase de pilotos. No se usa con datos reales de residentes hasta cerrar el contrato con el centro y la evaluación de impacto. | ResidApp és en fase de pilots. No s'usa amb dades reals de residents fins que es tanqui el contracte amb el centre i l'avaluació d'impacte. | Frase ya admitida en `pendientes-landing.md` («Estado de las dependencias»); coincide con `README.md` y `docs/privacidad/antes-de-datos-reales.md`. Una *evaluación de impacto* es el análisis de riesgos de privacidad que la ley exige antes de tratar datos de salud. |
| H3 *(solo en .cat)* | Aviso encima de todo: «Esta web está en catalán, pero la aplicación ResidApp funciona solo en castellano.» | Aquesta web és en català, però l'aplicació ResidApp funciona només en castellà. | `Program.cs` (solo `es`); ADR 0013 aceptado pero **sin construir**. No prometer plataforma ni portal en catalán. |
| T1 (título) | El registro diario del equipo asistencial, conectado con la información que reciben las familias. | El registre diari de l'equip assistencial, connectat amb la informació que reben les famílies. | ResidApp registra lo que pasa con cada residente; no gestiona «turnos médicos». |
| T2 (subtítulo) | ResidApp ayuda a las residencias a registrar lo que ocurre con cada residente, a coordinar a auxiliares, enfermería y medicina, y a informar a las familias con comunicados que aprueba una persona del equipo. | ResidApp ajuda les residències a registrar què passa amb cada resident, a coordinar auxiliars, infermeria i medicina, i a informar les famílies amb comunicats que aprova una persona de l'equip. | `docs/producto/alcance.md` (módulos Registro cotidiano, Eventos, Publicaciones, Portal Familiar); aprobación humana: Manual, «Portal Familiar». |
| B1 (botón) | Contactar | Contactar | Lleva al apartado 5. **[PENDIENTE: si habrá demostración y en qué condiciones]**; no decir «gratis» mientras no esté decidido. |

## 2. Características (tres bloques)

| Id | Castellano | Català | Comprobación |
|---|---|---|---|
| C1 título | Registro diario sencillo | Registre diari senzill | |
| C1 texto | Cada día, la auxiliar pulsa «Sin cambios» si el residente está como siempre, «No valorable» si no se puede valorar, o «Registrar cambio» si ha observado algo distinto. Cada registro queda con quién lo hizo y cuándo. | Cada dia, l'auxiliar prem «Sin cambios» si el resident està com sempre, «No valorable» si no es pot valorar, o «Registrar cambio» si ha observat alguna cosa diferent. Cada registre queda amb qui l'ha fet i quan. | `Views/Auxiliar/Registro.cshtml` (tres tarjetas con esos nombres). Autoría: `alcance.md`, «Cuentas multirol» (cuenta, perfil, ámbito, fecha y hora). **[VERIFICAR en el navegador]** que basta un toque. No existe un «cierre de turno»: es un cierre diario por residente. |
| C2 título | Del cambio observado a la valoración | Del canvi observat a la valoració | |
| C2 texto | Un cambio observado llega a la bandeja de enfermería, que lo valora y, si hace falta, lo pasa a medicina. Enfermería y medicina registran las constantes (tensión, frecuencia, temperatura, saturación, glucemia) y la pantalla marca las que salen del rango de referencia que fija el centro. Los avisos se ven dentro de la aplicación; no se envían al móvil. | Un canvi observat arriba a la safata d'infermeria, que el valora i, si cal, el passa a medicina. Infermeria i medicina registren les constants (tensió, freqüència, temperatura, saturació, glucèmia) i la pantalla marca les que surten del rang de referència que fixa el centre. Els avisos es veuen dins de l'aplicació; no s'envien al mòbil. | Bandejas, valoración y escalado: `alcance.md`. Constantes: `Views/Shared/_ConstantesFormulario.cshtml`. Rango del centro (no por residente): Manual, «Próximamente». Sin avisos al móvil: `alcance.md`, «Fuera de alcance». Alertas visuales **[VERIFICAR en el navegador]**. No prometer inmediatez («alertas instantáneas»). |
| C3 título | Portal Familiar | Portal Familiar | |
| C3 texto | Las familias ven las novedades que el equipo prepara y aprueba, firmadas como «Equipo asistencial del centro», y pueden gestionar citas con el equipo. No ven constantes, valoraciones, tratamientos ni nombres de profesionales. El portal no es un canal urgente. | Les famílies veuen les novetats que l'equip prepara i aprova, signades com a «Equipo asistencial del centro», i poden gestionar cites amb l'equip. No veuen constants, valoracions, tractaments ni noms de professionals. El portal no és un canal urgent. | Manual, «Portal Familiar» (qué ve y qué no ve nunca; citas; «El portal nunca es un canal urgente»). Firma: `FamilyCommunication.cs`. Sin absolutos como «nunca tienen acceso». Las citas dependen de cómo configure cada centro el servicio. |

## 3. Planes

| Id | Castellano | Català | Comprobación |
|---|---|---|---|
| P1 | El plan depende del número de residentes del centro. Algunas funciones pueden contratarse como módulos opcionales. | El pla depèn del nombre de residents del centre. Algunes funcions es poden contractar com a mòduls opcionals. | Decidido por Orlando el 29/10. **La aplicación aún no tiene topes por residentes, módulos por centro ni facturación** (`pendientes-landing.md`, punto 12): hay que decir «próximamente» o no anunciarlo hasta que exista. |
| P2 | **[PENDIENTE: nombres de los planes, tramos de residentes e importes — los fijan Orlando y CJ; una sola fuente para las dos landings]** | **[PENDIENTE]** | Los importes no tienen base. No anunciar, mientras nadie lo decida, «sin cuotas de alta», «usuarios ilimitados», «soporte preferente», «formación» ni «exportación avanzada» (no existe: solo se exporta estructura y censo, ADR 0010). Tampoco «arquitectura multi-residencia»: grupos solo propuestos (ADR 0008). |
| P3 | Antes de publicar precios: titular, condiciones de contratación, IVA y contrato de encargado con cada centro. | — (nota interna, no se publica) | `pendientes-landing.md`, punto 13; a validar con un abogado. |

## 4. Preguntas frecuentes

| Id | Castellano | Català | Comprobación |
|---|---|---|---|
| Q1 | **¿Dónde se guardan los datos?** La aplicación está alojada en Microsoft Azure. La región concreta se confirma con cada centro antes de empezar. | **On es guarden les dades?** L'aplicació està allotjada a Microsoft Azure. La regió concreta es confirma amb cada centre abans de començar. | Frase prudente aprobada (análisis, sección 8). Dev está en Spain Central (ADR 0014), pero **la región de producción no está decidida**: no afirmar «en España» ni «en la UE». **[PENDIENTE: cuando se decida, concretar]** |
| Q2 | **¿Se mezclan los datos de distintas residencias?** Los datos de cada centro se guardan separados: cada dato lleva el centro al que pertenece y la base de datos aplica ese filtro además de la aplicación. Es una medida entre otras (acceso por perfiles, segundo factor para el personal, registro de inicios de sesión y de acciones relevantes), no una garantía absoluta. | **Es barregen les dades de diferents residències?** Les dades de cada centre es guarden separades: cada dada porta el centre al qual pertany i la base de dades aplica aquest filtre a més de l'aplicació. És una mesura entre altres (accés per perfils, segon factor per al personal, registre d'inicis de sessió i d'accions rellevants), no una garantia absoluta. | Seguridad por filas: scripts `0030`, `0088`, `0090` (ADR 0008). Quien es dueño de la base (`db_owner`) la salta, de ahí «no absoluta». Segundo factor solo del personal (familiares: solo contraseña, `AccesoController.cs`). Registros: `eventos_acceso` y `eventos_auditoria`. Sin «infranqueable» ni «jamás». |
| Q3 | **¿Se puede modificar el registro de lo que se hace en la aplicación?** La aplicación guarda un registro de las acciones relevantes, protegido contra modificaciones y borrados. No registra cada lectura. | **Es pot modificar el registre del que es fa a l'aplicació?** L'aplicació guarda un registre de les accions rellevants, protegit contra modificacions i esborraments. No registra cada lectura. | Disparador `TR_audit_immutable` (script `0002`). No decir «inmutable»: quien administra la base podría quitar el disparador. **Revisar con la persona experta** el matiz de «no registra cada lectura». |
| Q4 | **¿Qué ven las familias?** Solo las novedades que el equipo del centro ha preparado y aprobado, y sus citas. No ven constantes, valoraciones, tratamientos ni nombres de profesionales. | **Què veuen les famílies?** Només les novetats que l'equip del centre ha preparat i aprovat, i les seves cites. No veuen constants, valoracions, tractaments ni noms de professionals. | Manual, «Portal Familiar», «Qué no ve nunca». Aprobación humana previa: `docs/producto/objetivos.md`. |
| Q5 | **¿Quién es responsable de los datos de los residentes?** El centro. ResidApp actúa por cuenta del centro, siguiendo sus instrucciones. | **Qui és el responsable de les dades dels residents?** El centre. ResidApp actua per compte del centre, seguint les seves instruccions. | Frase admitida en `pendientes-landing.md`; coincide con `Views/Home/Privacy.cshtml` (texto en revisión). |
| Q6 | **¿Cumple la normativa de protección de datos?** ResidApp está diseñada para ayudar a su centro a cumplir con la protección de datos. Está en fase de pilotos y no se usa con datos reales hasta cerrar el contrato y la evaluación de impacto. | **Compleix la normativa de protecció de dades?** ResidApp està dissenyada per ajudar el vostre centre a complir amb la protecció de dades. És en fase de pilots i no s'usa amb dades reals fins que es tanqui el contracte i l'avaluació d'impacte. | Frase prudente aprobada (análisis, sección 8). No decir «cumplimiento total». **Validación experta obligatoria.** |
| Q7 | **¿Sustituye a la historia clínica del centro?** No. ResidApp no sustituye la historia clínica oficial ni el software integral del centro, y no hace diagnósticos ni recomendaciones automáticas. | **Substitueix la història clínica del centre?** No. ResidApp no substitueix la història clínica oficial ni el programari integral del centre, i no fa diagnòstics ni recomanacions automàtiques. | `alcance.md`, «Fuera de alcance». |
| Q8 | **¿Envía avisos al móvil?** No. Los avisos se ven dentro de la aplicación. Las familias pueden recibir un correo genérico, sin datos clínicos, cuando haya un cambio en una cita, si el centro lo activa. | **Envia avisos al mòbil?** No. Els avisos es veuen dins de l'aplicació. Les famílies poden rebre un correu genèric, sense dades clíniques, quan hi hagi un canvi en una cita, si el centre ho activa. | `alcance.md`; ADR 0012 (avisos de cita construidos, **apagados por defecto**); Manual, «Citas». **[PENDIENTE]** decidir si esta frase se publica antes de encender el correo. |
| Q9 | **¿En qué idiomas está?** La aplicación está en castellano. | **En quins idiomes està?** L'aplicació està en castellà. | `Program.cs`. El portal en catalán (ADR 0013) está aceptado pero no construido: no decir nada hasta que exista. |

## 5. Contacto y pie de página

| Id | Castellano | Català | Comprobación |
|---|---|---|---|
| K1 | Escríbenos a **[PENDIENTE: dirección y quién la atiende]**. Por favor, no escribas datos de residentes ni de familiares. | Escriu-nos a **[PENDIENTE]**. Si us plau, no escriguis dades de residents ni de famílies. | Decidido: `mailto:` y no formulario (un formulario exigiría guardar datos aparte, un sistema contra correo basura sin rastreadores y un contrato con el servicio). Hoy no hay buzón `@residapp.es` (Microsoft 365: «todavía no»). |
| K2 | Al escribirnos, tus datos se usarán solo para responderte. Más información en la política de privacidad. | En escriure'ns, les teves dades s'usaran només per respondre't. Més informació a la política de privacitat. | **[PENDIENTE]** la redacta la persona experta: responsable es ResidApp/Orlando (no una residencia), plazo de conservación y derechos. |
| F1 | © 2026 **[PENDIENTE: titular]** · Aviso legal · Política de privacidad · Cookies | © 2026 **[PENDIENTE]** · Avís legal · Política de privacitat · Galetes | Aviso legal con nombre o razón social, NIF y domicilio (LSSI, art. 10): titular pendiente de Orlando. Textos legales en los dos idiomas, misma fuente y misma fecha de versión; decidir cuál prevalece si discrepan. |
| F2 | Esta web no usa cookies ni carga contenidos de terceros. | Aquesta web no fa servir galetes ni carrega continguts de tercers. | **Solo se puede escribir si la landing se construye así** (todo local, sin analítica, mapas ni vídeos). No enlazar al Manual, al Flujo ni a nada de `dev.residapp.es`. |

## Siguiente paso

1. Orlando decide los **[PENDIENTE]** (titular, buzón, demostración, importes, qué se dice del correo).
2. Se repasan en el navegador las filas **[VERIFICAR]** (`probar-en-navegador`).
3. El agente `dpo-residapp` revisa las filas de seguridad y privacidad (Q1 a Q6, K2, F1, F2); después, una persona experta (DPO real o abogado) las valida.
4. Una persona catalanohablante revisa la columna *Català*.
5. Solo entonces se diseña la maqueta nueva con la guía visual (Bootstrap 5, claro/oscuro, logo real, todo local).
