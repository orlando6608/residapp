# Pulsera NFC con PIN: guía para Administración del centro

**BORRADOR. Describe lo previsto: la pulsera todavía no existe en ResidApp.** Cuando se construya, esta guía pasa al Manual y a la Guía por perfil (con los textos exactos de las pantallas) y se revisa con `doc-usuario-residapp`. Lo valida una persona (DPO real o abogado y, para el texto al personal, un laboralista) antes de usarla con datos reales. Base: `docs/privacidad/pulsera-nfc-pin-lectura-dpo.md` y `docs/seguridad/pulsera-nfc-guia-implementacion.md`.

## Qué es, en pocas palabras

Una **pulsera de silicona** con un chip NFC (el mismo tipo de comunicación a pocos centímetros que el pago sin contacto). La Auxiliar la acerca a una **tablet compartida de la planta**, escribe su **PIN de 6 cifras** y entra en ResidApp **sin teclear usuario, contraseña ni código del móvil**.

Es una entrada **rápida y limitada**: la sesión que se abre solo permite lo que una Auxiliar necesita en planta y se cierra pronto si no se usa. Para cualquier otra cosa (cambiar su contraseña, firmar, administrar) hay que entrar como siempre.

## Lo que conviene tener claro

- **Es voluntaria.** Nadie está obligado a llevarla. Quien prefiera contraseña con código o passkey sigue entrando igual.
- **Está apagada por defecto.** El centro la activa por escrito (lo hace Plataforma con vuestra instrucción documentada).
- **Solo para Auxiliares.** No sirve para Enfermería, Medicina, Dirección, Administración ni familias.
- **Solo en tablets compartidas registradas.** Antes de usarla hay que **registrar cada tablet** desde «Aparatos» (Administración) y marcarla como compartida. En un móvil o tablet que no esté registrada, la pulsera no se ofrece.
- **No es un control de horarios.** ResidApp no guarda cuándo ni en qué tablet se acerca la pulsera, y **no se puede usar para el registro de jornada, el horario, la ubicación ni el rendimiento** de nadie.

## Qué hace Administración y qué no

| Lo hace Administración | Lo hace la persona | Nadie lo puede hacer |
|---|---|---|
| Registrar las tablets compartidas | Activar su pulsera en «Mi cuenta» (con su contraseña y su código del móvil) | Ver o fijar el PIN de otra persona (ni siquiera Administración) |
| Dar de alta una pulsera para una cuenta de Auxiliar | **Elegir su PIN de 6 cifras** (el único que lo conoce es quien lo elige) | Prestar una pulsera «de invitado» (no existe) |
| Retirar una pulsera (baja) | Retirar su propia pulsera desde «Mi cuenta» si la pierde | Ver horas ni historial de lecturas de nadie |
| Desbloquear una pulsera bloqueada | | |
| Reiniciar el PIN (la persona pone uno nuevo) | | |

Administración ve, por pulsera: la cuenta, el estado (pendiente de activar, activa, bloqueada, retirada), la fecha de alta y **el día** del último uso. No ve horas.

## Dar de alta una pulsera, paso a paso (previsto)

1. **Registrar la tablet** (una sola vez por tablet): en **Aparatos**, crear el registro con QR y marcarla como **compartida**.
2. **Asociar la pulsera a la Auxiliar:** en la ficha de su cuenta, dar de alta la pulsera. Queda «pendiente de activar». Entregarle la pulsera en mano.
3. **La Auxiliar la activa:** en «Mi cuenta» pide contraseña y código del móvil, acerca la pulsera y **elige su PIN de 6 cifras**. Desde ese momento está activa. Administración no ve el PIN.
4. **Probar** que entra desde la tablet registrada.

**Consejo para el PIN que debéis dar al personal:** no uses el PIN de tu tarjeta del banco ni tu fecha de nacimiento, ni cifras repetidas (000000) ni seguidas (123456). ResidApp rechaza los más obvios.

## Si algo sale mal

- **Pérdida o robo de la pulsera:** retirarla **de inmediato** (Administración desde la ficha, o la persona desde «Mi cuenta»). Al retirarla se cierran sus sesiones abiertas con pulsera. Se da de alta una nueva y la persona elige un PIN nuevo. Conviene anotar el motivo (pérdida, robo, sustitución, baja del personal o sospecha) con la lista que ofrece la pantalla; no se escribe texto libre.
- **Cinco PIN equivocados seguidos:** la pulsera se **bloquea** y no se desbloquea sola. Administración la desbloquea y la persona elige un PIN nuevo si lo ha olvidado. Mientras tanto la persona puede entrar con su contraseña y código o con su passkey: no pierde el acceso.
- **Alguien usa la pulsera de otra:** es el mayor riesgo y no se puede evitar con tecnología, porque la pulsera no reconoce a la persona. Lo reduce el centro con una **norma escrita** (no prestar la pulsera ni el PIN) y la propia aplicación: el nombre de quien tiene la sesión siempre se ve en pantalla, hay un botón «Soy otra persona» y cada persona ve sus últimas entradas con pulsera en «Mi cuenta» para detectar una que no hizo.
- **Se acaba el personal, cambia de centro o se suspende la cuenta:** la pulsera deja de servir. Aun así, **recógela y retírala** para que no quede en circulación.
- **La tablet no lee la pulsera:** solo funciona con **Android y Chrome**, con la página abierta en primer plano y la tablet con NFC activado. No funciona en iPad ni iPhone.

## Lo que debe preparar el centro (la aplicación no lo hace por vosotros)

ResidApp no regula la organización del centro. Antes de activar la pulsera hace falta:

1. **Informar al personal por escrito, antes de activarla**, con el texto de abajo (el centro es quien decide y responde de esta medida). Si el centro tiene representación del personal, consultar a un laboralista cómo informarla o consultarla.
2. **Una norma interna:** no prestar la pulsera ni el PIN, a quién avisar si se pierde (persona o cargo), qué pasa si se usa indebidamente. Lo prudente es que una pulsera perdida **no se descuente al trabajador**.
3. **Un inventario del lote:** quién recibió las pulseras, cuántas hay sin asignar y dónde se guardan **bajo llave**. Una pulsera sin dar de alta no sirve a nadie, pero conviene saber dónde está.
4. **Un procedimiento para altas y bajas**, incluido el personal externo o de empresas de trabajo temporal.
5. **Quién revisa y cada cuánto** las altas, bajas y bloqueos de pulseras (decisión del centro; sin ranking por persona).

## Texto para el personal (borrador del DPO, a validar)

Lo adopta el centro como suyo. Los corchetes los rellena el centro. Hay que ofrecerlo también en catalán si se pide.

> **Pulsera con PIN para entrar en ResidApp**
>
> **[RESIDENCIA]** ofrece, **si tú quieres**, una pulsera para entrar más rápido en las tablets compartidas de planta. Quien decide y responde de esta medida es **[RESIDENCIA]**; ResidApp, que presta la aplicación, trabaja siguiendo sus instrucciones.
>
> **Cómo funciona.** Acercas la pulsera a la tablet y escribes tu PIN de 6 cifras, que solo conoces tú. Con ella se abre una sesión **con menos permisos que una entrada normal** (solo lo necesario para tu trabajo de planta) y se cierra enseguida si no la usas. Para otras tareas, entras como siempre.
>
> **Qué se guarda.** Un identificador de la pulsera ligado a tu cuenta; su estado (activa, bloqueada, retirada) y las fechas; **el día** de su último uso; los intentos fallidos; y una versión protegida de tu PIN (nadie, ni ResidApp, puede leerlo). Cada entrada y cada fallo quedan anotados con tu cuenta, la hora, la dirección IP de la residencia y que fue con pulsera.
>
> **Qué no se hace.** No se registra cada vez que acercas la pulsera, ni en qué tablet. **No se usa para controlar tu horario, tu jornada, dónde estás ni tu rendimiento.**
>
> **Qué tienes que hacer.** No prestes tu pulsera ni tu PIN. Si la pierdes, avisa a **[persona o cargo]** o retírala tú en «Mi cuenta».
>
> **Para qué.** Para proteger los datos de salud de los residentes. Base jurídica: **[el DPO confirma]**.
>
> **Cuánto tiempo.** **[plazos]**. Cuando se retira la pulsera, se borra su identificador y la fila a los **[12 meses]**.
>
> **Tus derechos.** Acceso, rectificación, supresión y los demás ante **[contacto]**; reclamación ante **[AEPD o APDCAT]**.

**A residentes y familias** no hace falta una cláusula propia (no se tratan datos suyos). Una línea en la información general del centro basta: «El acceso del personal a la aplicación se protege con contraseña y código del móvil, llave de acceso o, en tablets compartidas, pulsera con PIN, y se registra».

## Qué falta para poder usarla con datos reales

La pulsera todavía no está construida. Además de construirla, antes de datos reales hay que: decidir el plazo y la forma de borrar los registros de acceso, que una persona experta valide la base jurídica y la información al personal, que CJ apruebe la lista de acciones permitidas a una sesión de pulsera, y probarla con las tablets y pulseras reales del centro. Detalle en `docs/seguridad/pulsera-nfc-guia-implementacion.md`, apartado 9.
