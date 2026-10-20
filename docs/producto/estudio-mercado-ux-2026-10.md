# Estudio de mercado y UX: software para residencias en España frente a ResidApp

Fecha: 2026-10-20.

> **Naturaleza del documento.** Es un estudio de referencia, no una especificación. No modifica [`alcance.md`](alcance.md) ni [`roadmap.md`](roadmap.md): algunas recomendaciones chocan con exclusiones vigentes del alcance (offline, push, integraciones) y quedan como **propuestas pendientes de decisión** de Orlando y CJ. Si alguna se acepta, se incorpora al alcance o a `docs/tareas/` y se anota aquí su estado.

## Método y limitaciones

- **ResidApp:** revisión práctica en `app-residapp-dev` con las identidades sembradas `dev-integrado-auxiliar`, `dev-integrado-enfermeria`, `dev-integrado-direccion` y `dev-integrado-familiar`, en escritorio y en móvil (375×812). Solo lectura: no se envió ningún registro.
- **Competidores:** sin acceso a demos. Lo que se dice de ellos sale de sus webs, comparativas del sector y conocimiento general del mercado; cuando es una estimación, se indica.
- No hay valoraciones de usuarios independientes (Capterra, tiendas de apps) de ResiPlus, Gerosalus ni GeriaClick, ni datos públicos fiables de cuota por proveedor. El único estudio sectorial serio es de pago (Alimarket 2026).

## 1. Panorama del mercado

- **Adopción:** se estima que en torno al 75 % de las residencias usa algún software de gestión (SeniorTic, sin fuente citada); el resto trabaja con hojas de cálculo y papel.
- **Cifras declaradas por los fabricantes (no verificadas):** ResiPlus, unos 2.750 clientes y 35.000 usuarios; Gerosalus, más de 6.000 clientes en España y otros 30 países.

| Segmento | Productos | Perfil de UX (estimación) |
| --- | --- | --- |
| ERP integral clásico | ResiPlus (ADD Informática, líder claro), Gerosalus, Resiges, Velneo ERP Residencias | Muy completos: medicación, PAI, escalas, facturación, turnos. Origen de escritorio, formularios densos y mucho menú; con módulo de tablet y portal de familias añadidos. Curva de aprendizaje alta. |
| ERP corporativo | Cegid Ekon XRP Health (desde unos 50 €/usuario/mes), SAP (Amavir) | Para grupos grandes, centrados en lo administrativo. La UX del personal auxiliar no es su prioridad. |
| ERP web/nube | GeriaClick, Geriges (orientado a UNE 158301) | Despliegue moderno, cuota mensual y usuarios ilimitados. Web funcional, no pensada primero para móvil. |
| Registro a pie de cama | Iseco Calas (marca la tarea en la habitación con verificación de presencia), integraciones tipo Medicip en DomusVi | Muy rápidos, pero solo registran tareas; no gestionan el proceso clínico. |
| Comunicación con familias | ResiPlus Familiares, Gerisoft Familias, Wappa‑Senior (SEGG), Famileo | Fotos, actividades y novedades. Casi ninguno exige aprobación profesional ni permite correcciones trazables. |

**Qué decide la aceptación de un programa:**

- **A favor:** cubrir lo que pide la inspección autonómica (caídas, contenciones, UPP, PAI), resolver facturación y conciertos, y un soporte que responda.
- **En contra (causas de abandono):** registrar lo mismo dos veces, wifi que no llega a las habitaciones, e interfaces difíciles para auxiliares con poca formación digital o con el español como segunda lengua.

## 2. ResidApp: evaluación práctica

ResidApp no es un ERP: cubre la continuidad asistencial y la comunicación con familias, y deja fuera medicación, facturación y turnos laborales. Compite, por tanto, con los módulos de registro y de portal familiar de los ERP, no con el ERP completo.

### Puntos fuertes

1. **Registro por excepción.** La auxiliar elige entre *Sin cambios*, *Registrar cambio* y *No valorable*; un residente estable se cierra con un toque. Ninguno de los ERP grandes plantea así el registro diario: es el principal diferencial.
2. **Registrar un cambio es rápido.** Síntomas en botones agrupados por área, nota opcional, clasificación ordinario/prioritario con motivos cerrados y botón *Continuar* fijo abajo en el móvil.
3. **Mobile-first real.** PWA (manifest y service worker), `lang="es"`, tema claro/oscuro. Los estados combinan color y texto ("Basal vigente/pendiente"), sin depender solo del color.
4. **Inicios orientados a la acción.** Enfermería ve primero los hitos fuera de plazo y después los contadores de bandejas; Dirección ve indicadores con su denominador ("6 de 15").
5. **Portal familiar cuidado.** Lenguaje claro, aviso de que no es canal urgente, aprobación profesional antes de publicar, correcciones visibles y trazadas, retención definida (30 días ordinarias, 6 meses relevantes). Por delante de las apps de familias del mercado.
6. **Privacidad desde el diseño.** Separación entre contenido clínico y familiar, consulta del basal auditada y límites de rol explícitos.

### Debilidades y riesgos (de más a menos grave)

1. **Doble registro.** Los centros ya tienen un ERP y la primera versión no prevé integraciones. Si la auxiliar registra en el ERP y además en ResidApp, el proyecto fracasa: es el principal obstáculo de adopción.
2. **Sin captura offline ni notificaciones push** (excluidos a propósito en `alcance.md`; hoy solo existe la página *Sin conexión*). Con mala cobertura en habitaciones, un registro perdido o un prioritario que nadie ve serán las primeras quejas del piloto.
3. **Identificación del residente.** La lista no muestra foto, habitación ni cama, y no tiene buscador ni orden por pendientes. Con 25 residentes por auxiliar es un problema, y la foto es una medida básica de seguridad del paciente.
4. **Textos densos y jerga interna** ("Cierre cotidiano", "Basal vigente", "ámbito", "hitos"). Las descripciones de las tarjetas son largas para el perfil auxiliar.
5. **Un clic de más.** El inicio del auxiliar solo tiene la tarjeta "Mis residentes"; debería entrar directamente a la lista.
6. **Acceso en dispositivos compartidos sin decidir.** En una tablet de planta compartida hace falta entrar rápido (PIN o tarjeta NFC); usuario y contraseña largos en cada turno no van a funcionar. Relacionado con ADR 0007.
7. **Detalles técnicos observados:**
   - `/PortalFamiliar` devuelve una página en blanco, sin 404 amigable (la ruta real es `/Familia/...`).
   - La primera navegación tardó unos 3,4 s (puede ser arranque en frío del App Service de desarrollo).
   - La tabla de Dirección tiene 10 columnas y no se adapta bien al móvil.
   - El navegador avisó de una escritura en el portapapeles durante la prueba; origen no identificado, pendiente de comprobar.

## 3. Comparativa resumida

Puntuación cualitativa. La columna de ResidApp sale de la prueba práctica; las demás, de información pública.

| Criterio | ERP clásico (ResiPlus/Gerosalus) | ERP nube (GeriaClick/Geriges) | Apps de familias | ResidApp |
| --- | --- | --- | --- | --- |
| Velocidad del registro diario | Media-baja | Media | — | Alta |
| Uso en móvil/tablet | Módulo añadido | Web adaptable | Alta | Alta (PWA) |
| Trazabilidad y roles clínicos | Alta | Media | Baja | Muy alta |
| Comunicación con familias | Correcta | Variable | Alta (fotos) | Alta y controlada |
| Indicadores de proceso | Informes | Informes | — | Buenos (con denominador) |
| Cobertura funcional | Muy alta | Alta | Baja | Baja (complemento) |
| Integración / offline | Alta / variable | Media | — | Nula todavía |
| Madurez y referencias | Muy alta | Media | Media | Ninguna (preproducción) |

**Conclusión:** en diseño y UX del registro diario y de la comunicación con familias, ResidApp está por encima de lo que se ve en el mercado español. Le falta lo que más pesa en la compra: cobertura funcional, integración y referencias. El posicionamiento realista es el de **capa de continuidad asistencial y familias que se suma al ERP del centro**, no el de sustituto.

## 4. Recomendaciones prioritarias

Todas en estado **propuesta**. Encajan con el rediseño de interfaz previsto para después de completar la versión mínima funcional.

| Id | Recomendación | Choca con el alcance vigente | Estado |
| --- | --- | --- | --- |
| R1 | **Integración mínima con el ERP:** importar residentes y ubicaciones (CSV o API) y exportar los registros, para eliminar el doble registro. | Sí ("Integraciones con historias clínicas externas en la primera versión") | Propuesta |
| R2 | **Lista de residentes:** foto, habitación/cama, buscador y orden por "pendiente de cierre". | No | Propuesta |
| R3 | **Textos para auxiliares:** frases cortas, iconos y prueba con 5 auxiliares reales, midiendo el tiempo por registro. | No | Propuesta |
| R4 | **Revisar antes del piloto la exclusión de offline y push:** al menos una cola local para el registro y un aviso para los prioritarios. | Sí ("Notificaciones push en el piloto", "Modo clínico offline") | Propuesta |
| R5 | **Acceso rápido en tablet compartida** (PIN o NFC), a decidir junto con el proveedor de identidad (ADR 0007). | No | Propuesta |
| R6 | **Medir en el piloto:** cuestionario SUS, tiempo por registro, % de cierres diarios dentro del turno y uso del portal por las familias. Complementa [`objetivos.md`](objetivos.md). | No | Propuesta |

## Fuentes

- [Cuadly – comparativa 2026](https://www.cuadly.es/blog/residencias/software-gestion-turnos-residencias-mayores) (publicada por un competidor incluido en ella)
- [SoftwareDoit – directorio de proveedores](https://www.softwaredoit.es/software-para-residencias-geriatricas/directorio-proveedores.html)
- [SoftwareDoit – guía 2026](https://www.softwaredoit.es/software-medico/software-residencias.html)
- [ADD Informática – ResiPlus](https://addinformatica.com/software-gestion-residencias/)
- [ResiPlus – complementos](https://addinformatica.com/complementos/)
- [Los Nogales – implantación de ResiPlus](https://los-nogales.es/resiplus-un-nuevo-software-para-la-digitalizacion-total-de-nuestros-centros/)
- [Gerosalus](https://www.gerosalus.com/)
- [GeriaClick – cómo funciona](https://geriaclick.com/es/como-funciona/)
- [Geriges](https://www.geriges.com/)
- [Resiges](https://resiges.com/)
- [Iseco – app de tareas en habitación](https://www.iseco.es/la-nueva-app-que-revoluciona-las-residencias-de-mayores/)
- [Alimarket – Informe 2026 de software para hospitales y geriátricos](https://www.alimarket.es/sanidad/informe/416940/informe-2026-de-software-de-gestion-para-hospitales-y-geriatricos-en-espana)
- [SeniorTic – tecnologías en residencias](https://www.seniortic.org/las-tecnologias-que-estan-revolucionando-las-residencias-de-mayores/)
- [Amavir – Estado de información no financiera 2022 (SAP)](https://www.amavir.es/wp-content/uploads/2023/04/Estado-de-informacio%CC%81n-no-financiera-consolidado-2022.pdf)
- [DomusVi Pamplona – integración Medicip](https://www.mediciphealth.com/domusvi-pamplona-residencia-pionera-en-equipamiento-tecnologico/)
- [Alimarket – Gerisoft Familias](https://www.alimarket.es/sanidad/noticia/387876/el-grupo-l-onada-presenta-su-nueva-aplicacion-para-movil--gerisoft-familias-)
- [Alimarket – Famileo](https://www.alimarket.es/sanidad/noticia/310929/famileo-lanza-una-app-para-que-los-mayores-lean-en-papel-las-conversaciones-de-mensajeria-movil)
- [Wappa‑Senior (SEGG)](https://psiquiatria.com/neuropsiquiatria/wappa-senior-39441/)
