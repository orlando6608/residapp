# Historial tras un traslado: información a residentes, familiares y personal

**BORRADOR — lo valida una persona (DPO real o abogado) antes de usarlo con datos reales.** Fecha del borrador: 2026-10-28.

Etiquetas: **[Hecho]** verificado en el repositorio, **[Suposición]** mía, **[Decidir]** lo fija la residencia, el DPO real o CJ. Los campos entre corchetes los rellena la residencia, que es la responsable (ADR 0007, líneas 3 a 5).

## Parte 1 — Texto para la cláusula informativa de residentes y representantes

Se inserta en la segunda capa (la completa) de la cláusula informativa de la residencia, en el apartado «Quién puede ver sus datos de salud dentro de la residencia». No hace falta una cláusula aparte. **[Suposición]** No necesita consentimiento: la base es la asistencia (ver fichero 01); solo hay que informar (art. 13).

> **Si usted o su familiar cambia de unidad dentro de la residencia**
>
> Cuando una persona residente pasa de una unidad a otra, el equipo de la unidad nueva **no ve por defecto** los episodios asistenciales (incidencias, cambios de estado, avisos) que ya se cerraron en la unidad anterior. Si el equipo de Enfermería o de Medicina de la unidad nueva necesita consultarlos para atender bien a la persona, puede pedirlos escribiendo para qué los necesita. Solo puede leerlos, no cambiarlos, y el permiso dura **una hora**.
>
> Cada petición y cada lectura **quedan registradas**: quién lo pidió, cuándo, qué persona residente y el motivo que escribió. La Dirección Clínica de la residencia puede revisar esos registros para comprobar que solo se consultó lo necesario.
>
> Esto es un uso **interno** de la residencia, que es la responsable de sus datos. No se comunican a ninguna otra entidad.
>
> Puede ejercer sus derechos (acceder a sus datos, rectificarlos, pedir su supresión cuando proceda, limitarlos u oponerse) escribiendo a [CONTACTO DEL RESPONSABLE] o a nuestro delegado de protección de datos [DPO]. Si no está de acuerdo con la respuesta, puede reclamar ante [AUTORIDAD].
>
> Los registros de accesos se conservan [PLAZO A DECIDIR].

**Notas para quien la adopte:**
- **Autoridad [AUTORIDAD]:** Agencia Española de Protección de Datos (AEPD) o Autoritat Catalana de Protecció de Dades (APDCAT). **A verificar según el centro piloto** (ver fichero 01).
- **Plazo de conservación:** no se debe publicar hasta que la residencia lo decida (ficheros 01 y 05).
- **Residentes con capacidad disminuida:** la información se da también al representante legal o a los familiares que la residencia haya designado. **[Decidir]** cómo se documenta (pregunta abierta del ADR 0007 sobre las poblaciones).
- **Catalán:** ofrezco la versión en catalán si se pide.
- **Hecho:** este texto debe coincidir con lo que hace el sistema: petición de una hora (`ReadDirectionBaseline.cs`, línea 10, valor configurable), solo lectura, registro con motivo (`SqlTransferHistoryAccess.cs`, líneas 38 a 49). Si cambia el código, hay que cambiar el texto.
- **No decir** «la Dirección revisa todos los accesos» mientras no exista una vista que lo permita (ver fichero 04). Por eso el texto dice «puede revisar».

## Parte 2 — Comunicación interna al personal (Enfermería, Medicina, Dirección Clínica, Administración)

Firma [RESIDENCIA / DIRECCIÓN]. Fecha [FECHA]. **[Decidir]** pasar por asesoría laboral y, si procede, informar a la representación de los trabajadores antes de enviarla.

> **Asunto: cómo consultar los episodios cerrados de la unidad anterior de un residente trasladado**
>
> **Qué puede pedir.** Si atiende a un residente que acaba de ser trasladado a su unidad, y existen episodios cerrados en su unidad anterior, verá un aviso con su número. Puede pedirlos si es usted de **Enfermería o de Medicina**. Medicina solo verá los episodios que fueron escalados a Medicina y los propios.
>
> **Cuándo debe pedirlos.** Solo si los necesita para atender al residente (por ejemplo, para continuar un cuidado o entender un antecedente reciente). No los pida por curiosidad ni por razones que no sean asistenciales: es un acceso a datos de salud sujeto a secreto profesional.
>
> **Qué escribir en el motivo.** Una frase breve de por qué los necesita (hasta 300 caracteres). **No escriba datos de salud ni nombres de personas.** Ejemplo válido: «Continuidad asistencial tras el traslado de unidad». Si escribe datos de salud en el motivo, el texto queda guardado en el registro y no se puede borrar fácilmente.
>
> **Qué verá y durante cuánto tiempo.** El historial, la línea temporal y el detalle de esos episodios, solo para leer. El permiso dura **una hora** para ese residente; termina antes si cambia de residente, de perfil o de ámbito o si cierra sesión. Para ver otro residente hay que pedirlo de nuevo.
>
> **Qué queda registrado.** Su nombre de cuenta, su perfil, el residente, la unidad, la hora y **su motivo**, tanto al pedirlo como cada vez que abre el historial, la línea temporal o un episodio. No se puede borrar ni editar.
>
> **Quién lo revisa.** La Dirección Clínica de su ámbito puede ver esas lecturas con su motivo en la trazabilidad del residente. [Decidir: frecuencia y qué ocurre ante un uso indebido; ver el procedimiento de revisión.]
>
> **Qué pasa si se usa mal.** [DECIDE LA RESIDENCIA: texto de consecuencias, p. ej. «se tratará como un acceso indebido a datos de salud y puede dar lugar a medidas disciplinarias y a la comunicación a la autoridad de control»]. Si cree que otra persona ha accedido sin motivo, avise a [CONTACTO DEL DPO O DIRECCIÓN].
>
> **Sus datos como trabajador.** Sus datos de cuenta y acceso en este registro los trata la residencia para garantizar la seguridad y la confidencialidad de los datos de los residentes. Puede ejercer sus derechos en [CONTACTO].

**Qué es cada término, en una frase:** *secreto profesional* es el deber de no contar a nadie más lo que sabe de un residente por su trabajo; *trazabilidad* es la lista, hecha por el sistema, de quién hizo qué y cuándo; *ámbito* es el conjunto de unidades y residentes a los que le da acceso su perfil.

## A verificar o decidir

1. **[Decidir] Residencia:** plazo de conservación (la frase final de la parte 1), consecuencias de un uso indebido y quién revisa.
2. **[Decidir] Asesoría laboral:** si hay que informar o consultar a la representación de los trabajadores; **a verificar**, no lo cito de memoria.
3. **[Decidir] Residencia:** el medio de entrega al personal y la constancia de que lo han recibido.
4. **Pendiente de código (Orlando/CJ):** la pantalla dice «las revisa Dirección» (`_HistorialUnidadAnterior.cshtml`, línea 28). Ajustarla a «puede revisarlas» o construir la vista de revisión.
