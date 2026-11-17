# Procedimiento: cortar el dictado por voz en todos los centros a la vez

**Escrito para Orlando (quien lo ejecuta) y para CJ (que debe saber que existe): para usarlo cuando haya que apagar el dictado ya, sin ser técnico.**

**BORRADOR de Claude, 2026-11-17, con las decisiones de Orlando de ese día.** Lo valida una persona experta (DPO real o abogado) junto con el resto de papeles del dictado, **antes de usarlo con datos reales**. No sustituye al anexo de módulos del contrato: el plazo definitivo con cada centro lo fija el anexo; el de aquí es el interno de partida.

Antes de un centro real, **este es el único bloqueante operativo del dictado**, ficha de privacidad (`ficha-modulo-dictado-voz.md`, «Cómo se corta para todos los centros»). Hoy no hay centro real con el dictado activado: el procedimiento rige en Azure dev y, cuando exista, en producción (ADR 0014, aplazado).

## 1. Qué es y qué consigue

- **Qué se hace:** quitar un único ajuste de la Web App, `IA__Mistral__ApiKey`. Es la clave con la que ResidApp se identifica ante Mistral, el proveedor de IA que transcribe el audio. (Un «ajuste» es un valor de configuración del servidor que no está en el código.)
- **Qué consigue:** sin esa clave, **ningún centro tiene el dictado**, aunque su módulo figure como activo (`IaOptions.Activa`, `ModuleSettings`). El bloque «Dictar el cambio» desaparece del formulario «Registrar cambio» y el servidor responde 404 **sin llamar a Mistral**. Para este módulo, «apagado» equivale a «no disponible», no a «solo lectura»: ResidApp no guarda ni el audio ni el texto dictado, así que no queda nada propio que consultar.
- **Qué no toca:** nada de lo ya registrado. Lo que una auxiliar confirmó y guardó sigue ahí como cualquier registro. El resto de ResidApp (la base) sigue funcionando.
- **Qué no hace por sí solo:** no borra nada en Mistral. Qué queda allí depende de la retención cero por escrito (ficha, «Qué queda en Mistral al apagar»); mientras no esté firmada, no se da por borrado nada.

## 2. Cuándo se usa

Se corta, sin esperar a entender todo, cuando ocurra cualquiera de estas cosas:

1. **Mistral da problemas**: sospecha o aviso de brecha, cambio de condiciones (retención, región, entrenamiento, subencargados) que no se ha aceptado, o fallo grave que envía datos donde no debe.
2. **Se sospecha que la clave se ha filtrado** (publicada en el repositorio, en un chat, en una captura). Aquí además hay que anularla (apartado 5).
3. **Un centro lo pide** y el anexo exige apagar a nivel de toda la plataforma (si solo quiere apagarlo en su centro, no es este procedimiento: lo hace Plataforma con el interruptor de ese centro, a su petición).
4. **Lo decide Orlando** por prudencia (p. ej., un cambio en el proveedor que aún no se ha revisado).

Si es una brecha, este corte es el paso «Contener» del protocolo de brechas (`protocolo-brechas-borrador.md`, apartado 3.3) y **se sigue también ese protocolo**: avisar al centro en un máximo de 24 horas desde que se tiene constancia.

## 3. Quién y en cuánto tiempo

- **Quién lo hace:** **solo Orlando**, que hoy es la única persona con acceso al servidor de Azure. **Hueco abierto:** no hay suplente. Antes de un centro real hay que dejar nombrada una persona con permiso mínimo (solo cambiar los ajustes de esa Web App) y probada, porque con una sola persona el plazo no se cumple de vacaciones o enfermo. Este papel se actualiza entonces con el nombre.
- **Cuánto tarda (compromiso interno):** **menos de 4 horas en horario laborable** desde que Orlando lo decide o recibe la petición de un centro. Fuera de horario laborable no hay compromiso hasta que haya suplente; en ese caso se corta en cuanto se vea, y se anota. El apagado en sí tarda **menos de un minuto**, más lo que tarda el servidor en reiniciarse (apartado 4, paso 3).
- **Cada hora cuenta:** tratar datos después de una instrucción de apagado es tratar contra una instrucción del centro (RGPD art. 28.3.a). Por eso el plazo definitivo lo fijará el anexo y la aplicación guarda cuándo llegó la petición y cuándo se ejecutó (ADR 0015).

## 4. Pasos para cortar

Ten a mano: la hora de ahora y el motivo. **No pegues la clave en ningún chat, correo ni documento** (ni la leas en voz alta): no hace falta para cortar.

1. **Anota la hora** y el motivo (una línea). Si es un incidente, abre además el registro de incidentes (`protocolo-brechas-borrador.md`, apartado 4.2).
2. **Entra en Azure** con tu cuenta (portal de Azure, o `az login` en PowerShell).
3. **Quita el ajuste.** Es el mismo ajuste, con el mismo nombre, en todos los entornos; solo cambian la Web App y su grupo de recursos. En desarrollo:

   ```powershell
   az webapp config appsettings delete --name app-residapp-dev --resource-group rg-residapp-dev --setting-names IA__Mistral__ApiKey
   ```

   En producción, cuando exista, se sustituyen el nombre y el grupo por los de producción (`[NOMBRE DE LA WEB APP DE PRODUCCIÓN]`, `[GRUPO DE RECURSOS DE PRODUCCIÓN]`: a rellenar al crear el entorno, ADR 0014). Si prefieres el portal: Web App → Configuración → Variables de entorno → `IA__Mistral__ApiKey` → Eliminar → Aplicar.

   Cambiar un ajuste **reinicia la Web App**: durante 30 a 45 segundos la aplicación puede dar error 500 o 502 a todo el mundo y luego responde. Es normal; no repitas el cambio.
4. **Comprueba que ha quedado quitado** (debe salir vacío):

   ```powershell
   az webapp config appsettings list --name app-residapp-dev --resource-group rg-residapp-dev --query "[?name=='IA__Mistral__ApiKey'].name" -o tsv
   ```

5. **Comprueba en la aplicación** (cuando haya pasado el reinicio): entra con una cuenta de Auxiliar con un residente asignado y abre «Registrar cambio»: **no debe verse el bloque «Dictar el cambio»**. En la pantalla de Plataforma, los centros con el módulo figuran con el aviso «Falta la clave global del proveedor de este módulo». Si el bloque sigue, espera un minuto y recarga; si persiste, no des el corte por hecho.
6. **Anota la hora de fin.** La diferencia con la del paso 1 es el tiempo real de corte.

## 5. Si el motivo es una clave filtrada o un problema en Mistral

Quitar el ajuste detiene a ResidApp, pero **no anula la clave**: quien la tenga puede seguir usándola contra Mistral. Además:

1. **Anula la clave en la consola de Mistral** (la clave antigua deja de valer en todas partes). Anota qué clave era y de cuándo, **no su valor**.
2. Si ya había audios enviados, anota la hora del último y deja que lo valore la persona experta; **no borres nada** de los registros del servidor para «limpiar» (protocolo de brechas, «Qué NO hacer»).
3. No se vuelve a encender hasta cumplir el apartado 7.

## 6. A quién se avisa

**Lo hace Orlando.** Cuando haya centros reales con el dictado activo:

- **Aviso al centro**, a la persona autorizada que figure en el anexo (y a su suplente), por el canal acordado en el anexo, **en un máximo de 24 horas desde el corte** (igual que el aviso de incidentes del protocolo de brechas). Sin nombres de residentes ni datos personales: solo que el dictado queda **no disponible** en su centro desde la hora X, el motivo en una frase y cuándo se informará de novedades. La plantilla del apartado 4.1 del protocolo de brechas sirve de base si hay incidente.
- **A CJ**: aviso corto, como suplente del contacto de privacidad (CJ aún no lo ha aceptado por escrito).
- **A las auxiliares**: no hace falta aviso aparte; el bloque desaparece. Si un centro lo pide, se lo comunica su Dirección o Administración.
- **A la persona experta**, cuando exista, si el motivo es un incidente (protocolo de brechas, apartado 3.7).

## 7. Cómo se vuelve a poner

**No se vuelve a encender solo porque haya pasado el tiempo.** Antes:

1. Orlando comprueba y anota que **ha desaparecido el motivo** (el problema de Mistral está resuelto, la clave nueva está creada o el centro lo permite).
2. Si el corte fue por un cambio de Mistral (retención, región, subencargados…), **la persona experta lo ha revisado** y, si cambia la ficha de privacidad de forma sustancial, se sube su versión y cada centro la vuelve a aceptar (anexo, apartado 7).
3. Se vuelve a poner el ajuste **con una clave válida** (la nueva si hubo que anularla). Mejor desde el **portal de Azure** (Variables de entorno → Añadir → `IA__Mistral__ApiKey` → Aplicar), para que el valor no quede en el historial de comandos de PowerShell. De nuevo hay un reinicio de 30 a 45 segundos.
4. Se comprueba al revés que en el apartado 4: el bloque «Dictar el cambio» vuelve a verse a una auxiliar de un centro con el módulo activo, y **sigue sin verse en los centros cuyo módulo está apagado o vencido** (el interruptor del centro manda; la clave global solo es el maestro).
5. Se avisa a los centros de que el dictado está de nuevo disponible.

## 8. Lo que este papel no resuelve (pendiente)

- **Suplente** con permiso mínimo en Azure, probado (apartado 3). Condición antes de un centro real.
- **Producción**: nombres reales de la Web App y del grupo de recursos; el entorno no existe aún (ADR 0014).
- **Prueba automática** («módulo apagado, clave puesta, ninguna llamada a Mistral»): propuesta del anexo, aún no existe; no es parte de este corte, que quita la clave y no depende de ella.
- **Registro de la actividad de Azure** (quién cambió qué ajuste y cuándo): pendiente de comprobar y activar en el seguimiento de seguridad del correo y los registros (B10). Mientras tanto, la prueba de cuándo se cortó son las horas que anota Orlando (pasos 1 y 6).
- **Validación experta** (DPO real o abogado) del papel, del plazo de 4 horas y del contenido del aviso al centro.
- Un **interruptor global dentro de ResidApp** (parada de emergencia de Plataforma) se descartó el 2026-11-16 (ADR 0015); para el dictado, la clave ya lo hace. Si vuelve a hacer falta para otros módulos, es otra decisión.

## 9. Resumen de una línea

Quitar `IA__Mistral__ApiKey` de la Web App → esperar al reinicio → comprobar que desaparece «Dictar el cambio» → anotar horas → avisar a los centros en 24 h; si la clave se ha filtrado, anularla también en Mistral; no se vuelve a poner sin revisar el motivo.
