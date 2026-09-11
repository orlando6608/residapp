# Declaración de línea base funcional

## Plataforma asistencial y de comunicación con familias - Connect

**Identificador de línea base:** `LBF-CONNECT-2026-09-05-V1`  
**Versión de la declaración:** 1.0  
**Fecha efectiva:** 5 de septiembre de 2026  
**Estado:** APROBADA COMO LÍNEA BASE FUNCIONAL  
**Fuente canónica:** Markdown versionado en GitHub  
**Ámbito:** definición funcional del prototipo y preparación del diseño técnico  

## 1. Declaración formal

Por decisión expresa del responsable del proyecto, se aprueba y congela la línea base funcional `LBF-CONNECT-2026-09-05-V1`, compuesta por las versiones exactas identificadas en el manifiesto de este documento.

Desde su fecha efectiva, esta línea base constituye la referencia funcional obligatoria para:

- diseño y revisión de la arquitectura;
- propuesta del esquema físico D1/Drizzle;
- desarrollo del prototipo navegable;
- implementación de autorización RBAC/ABAC;
- redacción de historias de usuario y criterios de aceptación;
- diseño, ejecución y revisión de pruebas;
- evaluación de solicitudes de cambio.

La aprobación convierte en definitivas, dentro de esta línea base, las versiones que en su cabecera se describían como candidatas. No modifica su contenido ni crea una versión documental nueva de cada artefacto; esta declaración es el acto de aprobación que resuelve ese estado transitorio.

## 2. Manifiesto de artefactos aprobados

> **Corrección editorial posterior (6 de septiembre de 2026):** se corrigieron la ruta real del PRD y el nombre de la carpeta `decisions`. No cambia ningún artefacto funcional, huella, permiso, flujo, entidad ni criterio de aceptación. El commit y el tag originales de la línea base permanecen como evidencia histórica.
Las huellas SHA-256 se calculan sobre los archivos Markdown exactos antes de esta declaración. Un archivo con el mismo nombre y una huella diferente no pertenece a esta línea base.

| Tipo | Documento y versión | Ruta canónica en el repositorio | SHA-256 |
| --- | --- | --- | --- |
| PRD | PRD plataforma de contacto con familias v0.4 consolidado | `docs/product/2026-09-05-PRD-plataforma-contacto-familias-v0.4-consolidado.md` | `f8f28a8893217788bab35dc7b95297dc75200e0cf7e82d082808e98e826195d9` |
| Permisos | Matriz de permisos de seis perfiles v0.2 | `docs/product/permissions/2026-09-05-matriz-permisos-seis-perfiles-v0.2.md` | `a65cf2b6f244a28a1a8f3fb0a72f3430d62063b960dccfebf2bd9097e4923218` |
| Wireframe | Auxiliar v0.2 | `docs/product/wireframes/2026-09-05-wireframe-funcional-auxiliar-v0.2.md` | `384ac7587abb5fd280f7321f5fe826bf28d10a39812b171904c38dd5127b880e` |
| Wireframe | Enfermería v0.2 | `docs/product/wireframes/2026-09-05-wireframe-funcional-enfermeria-v0.2.md` | `fe3f0bed0e946a2f5dfcdce25db375a3e9be417d2dfdc70bcb5a06cbbbed62ef` |
| Wireframe | Medicina v0.2 | `docs/product/wireframes/2026-09-05-wireframe-funcional-medicina-v0.2.md` | `b6febd89276f727c77f929f5ec0eecfe4fc40f387cf3985c755b0fd65c2d5256` |
| Wireframe | Portal Familiar v0.2 | `docs/product/wireframes/2026-09-05-wireframe-funcional-portal-familiar-v0.2.md` | `2314bbcc202889731f9735c730c41807f41db5ae6efba4ad80977a42b16b235a` |
| Wireframe | Administración v0.2 | `docs/product/wireframes/2026-09-05-wireframe-funcional-administracion-v0.2.md` | `978841e2b54ea3b3453c251a77ac3ec05b256dcb9b58870786a9228187e5685a` |
| Wireframe | Dirección/Coordinación Clínica v0.1 | `docs/product/wireframes/2026-09-05-wireframe-funcional-direccion-coordinacion-clinica-v0.1.md` | `f0d450846bfc304970910318a370b1b78172a92de68340fae910a7ca3b924c07` |
| Trazabilidad | Matriz de trazabilidad funcional v0.1 | `docs/product/traceability/2026-09-05-matriz-trazabilidad-funcional-v0.1.md` | `2ea5a0e4f0a4c803c4282179749006ce1e3055708c652d9a29edd7dec54d247a` |

**Total:** nueve artefactos funcionales canónicos.

## 3. Fuente de decisiones y material histórico

El archivo `docs/product/decisions/2026-09-05-registro-decisiones-posteriores-2026-09-02-v0.1.md` conserva la procedencia de las decisiones utilizadas en la consolidación. Es una fuente de gobierno y contexto, pero no sustituye ninguno de los nueve artefactos del manifiesto.

Los PDF y DOCX anteriores se conservan como versiones cerradas o históricas. Sirven para auditoría de evolución y comparación, pero no son la fuente canónica de cambios. Ante una diferencia con los Markdown aprobados, prevalece la línea base definida aquí.

## 4. Jerarquía documental vigente

En caso de contradicción se aplicará este orden:

1. Decisiones explícitas posteriores, aprobadas y registradas mediante el procedimiento de cambio.
2. PRD v0.4 consolidado.
3. Matriz de permisos v0.2.
4. Wireframe canónico del perfil afectado.
5. AGENTS.md vigente.
6. Implementación y pruebas existentes.

La matriz de trazabilidad no introduce requisitos, permisos o comportamientos nuevos. Actúa como índice verificable entre las fuentes anteriores. Si contradice una fuente normativa, debe corregirse la trazabilidad; no se utilizará para modificar silenciosamente la regla original.

La declaración de línea base identifica y congela versiones, pero tampoco altera el contenido funcional de los documentos incluidos.

## 5. Alcance funcional congelado

Quedan aprobadas, entre otras, las siguientes reglas invariantes:

- seis perfiles separados: Auxiliar, Enfermería, Medicina, Familiar, Administración y Dirección/Coordinación Clínica;
- autorización server-side con cuenta activa, perfil activo explícito, ámbito y permiso específico; denegación por defecto;
- los permisos de una cuenta multirol no se combinan;
- centro y unidad obligatorios y ubicación longitudinal mediante intervalos;
- exactamente nueve áreas basales, sin puntuación conjunta;
- Índice de Barthel común `BARTHEL_COMUN_V0_1`, con diez respuestas persistidas y total automático sobre 100;
- retirada completa de CFS y Pfeiffer;
- un único borrador basal activo por residente y firma exclusiva de su creador;
- aportaciones a borrador ajeno con autoría individual, sin transferencia de firma;
- basal firmado inmutable y rectificación mediante nueva versión vinculada;
- ventana de seis horas limitada a cursos o notas clínicas expresamente habilitados, nunca al basal;
- separación entre identidad administrativa y valoración basal;
- Auxiliar consulta únicamente el basal vigente de residentes asignados;
- Familiar y Administración no acceden al basal;
- Dirección accede al detalle clínico solo en lectura, con permiso, finalidad, ámbito y auditoría previa;
- publicaciones familiares separadas del registro interno, con aprobación humana e inmutabilidad tras publicación;
- citas familiares configuradas por centro con una única modalidad vigente: `DIRECT` o `REQUEST`;
- trazabilidad completa de 140 requisitos funcionales y 140 pruebas identificadas.

## 6. Qué significa esta aprobación

La línea base:

- autoriza utilizar estos documentos para diseñar y construir;
- exige que toda implementación sea trazable a los requisitos aprobados;
- impide cambiar alcance, permisos o comportamientos de forma silenciosa;
- permite corregir errores puramente ortográficos o enlaces sin alterar semántica, dejando constancia en Git;
- no convierte decisiones pendientes en aprobadas;
- no constituye autorización para tratar datos personales o clínicos reales;
- no acredita todavía preparación jurídica, de seguridad o de producción.

## 7. Exclusiones y puertas pendientes

Estas cuestiones quedan fuera de la aprobación y no pueden resolverse por inferencia durante el desarrollo:

1. Esquema físico definitivo D1/Drizzle, índices, migraciones y estrategia de base compartida o por centro.
2. Profesionales concretos que recibirán los permisos configurables en cada centro.
3. Actor y procedimiento para cancelar un borrador basal cuando su creador no está disponible.
4. Actor, versiones alcanzables y permiso para iniciar una rectificación basal.
5. Efectos de traslados entre centros/unidades e inactivación/reactivación sobre grants y tareas pendientes.
6. Finalidades, ámbitos y conservación de la lectura clínica de Dirección.
7. Política definitiva de corrección y conservación de cursos/notas clínicas.
8. Conservación jurídica, copias de seguridad, restauración y eliminación de datos.
9. Autenticación y segundo factor definitivos para producción.
10. Validación jurídica, DPO, contratos y autorización para utilizar datos reales.
11. Parámetros concretos del servicio de citas por centro y acceso profesional a agendas.
12. Validaciones de accesibilidad y usabilidad del prototipo.
13. Procedimiento técnico de provisionamiento de centros y soporte excepcional.

Estas puertas no invalidan la línea base funcional. Sí pueden bloquear el esquema definitivo, una función concreta, el piloto con datos reales o el paso a producción.

## 8. Control obligatorio de cambios

Toda modificación posterior que altere significado, capacidad, acceso, pantalla, entidad, estado o criterio de aceptación seguirá este flujo:

1. **Propuesta:** describir problema, motivo, alternativa y alcance.
2. **Impacto:** identificar requisitos, perfiles, permisos, pantallas, entidades y pruebas afectados.
3. **Decisión:** aprobar, rechazar o aplazar; registrar responsable y fecha.
4. **Actualización coordinada:** modificar, según proceda, PRD, permisos, wireframes y trazabilidad en el mismo cambio lógico.
5. **Verificación:** ejecutar pruebas afectadas y comprobar ausencia de contradicciones.
6. **Revisión Git:** presentar diff o pull request legible; queda prohibida la corrección silenciosa.
7. **Nueva línea base:** cuando el cambio sea funcional, emitir una declaración posterior que sustituya expresamente esta versión y actualice el manifiesto y las huellas.

### 8.1 Clasificación de cambios

| Tipo | Ejemplo | Efecto sobre la línea base |
| --- | --- | --- |
| Editorial | Ortografía, formato o enlace sin cambio semántico | Mantiene alcance; registrar en Git y actualizar huella en la siguiente declaración. |
| Funcional compatible | Aclaración o nueva capacidad que no elimina una regla vigente | Nueva versión menor de los documentos afectados y nueva declaración. |
| Funcional incompatible | Cambio de flujo, permiso, estado, significado clínico o eliminación de capacidad | Nueva versión mayor de línea base y revisión completa de impacto. |
| Técnico | Traducción fiel a D1/Drizzle sin cambiar comportamiento | No cambia la línea base funcional; requiere trazabilidad y revisión arquitectónica. |

## 9. Reglas para D1/Drizzle

La línea base funcional permite comenzar la propuesta técnica de D1/Drizzle, pero no la aprueba por anticipado.

El diseño deberá:

- representar las entidades y estados funcionales sin reducir sus garantías;
- imponer mediante restricciones e índices las unicidades aprobadas cuando sea viable;
- mantener separación entre identidad, perfil, ámbito, permiso y autoría;
- conservar inmutabilidad, versionado, relaciones temporales y auditoría append-only;
- justificar cualquier desviación y devolverla al procedimiento de cambio funcional;
- incluir migraciones y pruebas antes de modificar el esquema del proyecto.

## 10. Identificación en Git

La versión aprobada debe quedar asociada a:

- un único commit que contenga los nueve artefactos y esta declaración;
- una etiqueta anotada recomendada: `functional-baseline-2026-09-05-v1`;
- un repositorio sin cambios documentales pendientes en esos archivos en el momento de etiquetar.

El commit y la etiqueta de Git, junto con las huellas del manifiesto, constituyen la evidencia técnica de qué contenido fue aprobado.

## 11. Criterios de sustitución

Esta línea base permanece vigente hasta que otra declaración:

- indique expresamente que la sustituye;
- identifique las nuevas versiones;
- documente cambios y decisiones pendientes resueltas;
- regenere la trazabilidad afectada;
- incluya nuevas huellas de integridad;
- quede aprobada y etiquetada en Git.

## 12. Registro de aprobación

| Campo | Valor |
| --- | --- |
| Decisión | Aprobar la línea base funcional `LBF-CONNECT-2026-09-05-V1` |
| Fecha efectiva | 5 de septiembre de 2026 |
| Estado | Aprobada |
| Aprobación | Decisión expresa del responsable del proyecto |
| Alcance | Nueve artefactos enumerados en el manifiesto |
| Próxima puerta | Propuesta y revisión del diseño físico D1/Drizzle |

---

**Fin de la declaración de línea base funcional v1.0.**
