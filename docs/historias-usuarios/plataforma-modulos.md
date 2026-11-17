# Historias de usuario — Plataforma y módulos opcionales por centro

Plataforma es el perfil del proveedor (ResidApp): provisiona los centros y, desde el ADR 0015, **enciende y apaga los módulos opcionales de cada centro**, solo a petición del centro. Un módulo es una función que no tienen todos los centros; hoy solo existe **Dictado por voz** (`DICTADO_VOZ`). Todo lo demás es base. Estas historias cubren Plataforma y lo que ven los demás perfiles; el procedimiento de operación está en `docs/privacidad/procedimiento-corte-global-dictado.md` y la ficha de privacidad en `docs/privacidad/ficha-modulo-dictado-voz.md`.

## Cómo leer estas historias

Cada historia sigue el formato "Como [perfil], quiero..., para...", e incluye criterios de aceptación verificables en lenguaje llano.

## Historias de usuario

### 1. Activar o renovar un módulo en un centro

**Como** Plataforma, **quiero** activar un módulo en un centro, o renovarlo, a petición suya y con el respaldo del contrato, **para** que el centro lo use solo durante el periodo contratado y quede prueba de que lo pidió.

Criterios de aceptación:
- Solo Plataforma activa un módulo; la Administración o la Dirección del centro no tienen ningún botón para hacerlo.
- Solo se puede activar en un centro activo. Hay que indicar la vigencia («desde» y «hasta», este último día incluido, con el día de Madrid), la referencia y la versión del anexo del contrato, y marcar una a una las condiciones de la ficha de privacidad; si falta alguna, no se activa.
- Hay que indicar cuándo recibió Plataforma la petición del centro (no puede ser de hace más de un año) y por qué canal. La referencia de la petición no se escribe: la asigna ResidApp al guardar (el código del módulo y el número del cambio, por ejemplo `DICTADO_VOZ-0003`) y se muestra en el aviso de confirmación para anotarla en la respuesta al centro.
- Una renovación empieza hoy o antes, nunca en el futuro, y no puede nacer ya vencida.
- Cada cambio se guarda como una fila nueva de un historial al que solo se añade: no se editan ni se borran, ni se corrigen fechas. El centro ve el cambio en su auditoría, con el nombre del módulo y sin datos de residentes.
- Activar un módulo no concede permisos a ningún perfil: quién puede usarlo dentro del centro lo sigue decidiendo su Administración.
- La aplicación no envía aviso al centro: Plataforma avisa por escrito a la persona autorizada que figure en el anexo.

Códigos de origen: `PLA-09`, ADR 0015.

### 2. Apagar un módulo en un centro

**Como** Plataforma, **quiero** apagar un módulo en un centro cuando el centro me lo pide, **para** dejar de tratar datos en ese centro en cuanto lo instruye y poder probar cuándo se pidió y cuándo se ejecutó.

Criterios de aceptación:
- Se apaga a petición del centro y en el plazo que fije el anexo; el centro no lo apaga por sí solo.
- Se puede apagar siempre, también en un centro inactivo.
- Hay que elegir un motivo de una lista cerrada e indicar cuándo llegó la petición, por qué canal; la referencia la asigna ResidApp.
- El apagado tiene efecto inmediato en el servidor: desde ese momento, el módulo no está disponible en ese centro, también para lo que estaba abierto en una pantalla.
- Si otra persona de Plataforma cambió el módulo mientras tanto, la aplicación lo dice y pide recargar, sin duplicar el cambio.
- Apagar nunca quita funciones base ni borra lo ya registrado.

Códigos de origen: `PLA-10`, ADR 0015.

### 3. Vigilar los vencimientos y consultar el historial de un módulo

**Como** Plataforma, **quiero** ver qué módulos vencen pronto y el historial de cada módulo en cada centro, **para** renovarlos a tiempo y poder atender cualquier consulta sobre quién cambió qué y cuándo.

Criterios de aceptación:
- La lista de centros muestra un panel «Módulos que vencen» con los vencimientos próximos.
- El historial de un módulo en un centro muestra cada cambio (activación, renovación, baja) con sus fechas, el anexo, el canal y la referencia. No contiene datos de residentes ni texto libre.
- Un módulo que nunca se ha activado no tiene historial.

Códigos de origen: ADR 0015, Guía `PLA-09`.

### 4. Que un módulo vencido o apagado deje de aparecer solo

**Como** profesional de un centro (por ejemplo, Auxiliar), **quiero** ver una función de módulo solo si mi centro la tiene activa y vigente, **para** no usar algo que el centro no ha contratado o que ya no puede tratar.

Criterios de aceptación:
- Con el dictado activo y vigente, el bloque «Dictar el cambio» aparece al registrar un cambio (si el dispositivo tiene micrófono). En otro caso, no aparece.
- Al vencer la vigencia, el módulo deja de estar disponible en ese centro **sin que nadie lo apague** y sin tarea programada: se calcula al consultar, con el día de Madrid.
- Para el dictado, «apagado o vencido» equivale a «no disponible», no a «solo lectura»: ResidApp no guarda el audio ni el texto dictado, así que no queda nada propio que consultar. Si se intenta usar por una dirección directa, el servidor responde como si no existiera.
- Lo que la persona confirmó y guardó en el registro sigue ahí, como cualquier otro registro.
- Un módulo apagado o vencido nunca quita funciones base.

Códigos de origen: ADR 0015, ADR 0009, ADR 0011.

### 5. Poder cortar el dictado en todos los centros a la vez

**Como** Plataforma (quien administra el servidor), **quiero** poder cortar el dictado en todos los centros a la vez si el proveedor de IA da problemas, **para** contener un incidente sin esperar a apagar centro por centro.

Criterios de aceptación:
- Quitar el ajuste `IA__Mistral__ApiKey` de la Web App deja el dictado no disponible en todos los centros, aunque el módulo de un centro figure activo; el servidor no llama al proveedor.
- Mientras la clave falte, la pantalla de módulos de Plataforma avisa de que el módulo no funciona en ningún centro.
- El corte, sus pasos, el plazo y cómo se vuelve a poner están en un procedimiento escrito; no se vuelve a encender sin comprobar que desapareció el motivo.

Códigos de origen: ADR 0015 (punto 6), `procedimiento-corte-global-dictado.md`.

## Tabla resumen de trazabilidad

| Historia | Códigos de origen |
| --- | --- |
| 1. Activar o renovar un módulo | `PLA-09`, ADR 0015 |
| 2. Apagar un módulo | `PLA-10`, ADR 0015 |
| 3. Vencimientos e historial | ADR 0015, `PLA-09` |
| 4. Un módulo vencido o apagado deja de aparecer | ADR 0015, ADR 0009, ADR 0011 |
| 5. Corte global del dictado | ADR 0015, `procedimiento-corte-global-dictado.md` |

## Nota de procedencia

Escritas el 2026-11-17 a partir del ADR 0015 y de lo construido en los PR #305 a #309 (script `0098`). Cubren el hueco que señaló `qa-revisor-residapp`: hasta hoy los módulos tenían ADR y guía, pero ninguna historia de usuario de la que partir para las pruebas.
