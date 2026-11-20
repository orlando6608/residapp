# Pulsera NFC con PIN: especificación de compra

**BORRADOR para Orlando (quien compra).** Está pensado para poder enseñarse a un proveedor o distribuidor, pero **no se manda tal cual**: los puntos marcados «a verificar» son hipótesis que hay que confirmar con el fabricante antes de pedir. Base: `docs/privacidad/pulsera-nfc-pin-lectura-dpo.md` (apartado 2.9 y 2.5) y la nota de aplicación de NXP **AN12196** («NTAG 424 DNA features and hints»). Los precios **no se han consultado**: se piden ofertas.

## 1. Qué se compra y en qué orden

1. **Primero, un lote de prueba de 5 a 10 pulseras**, para probar con las tablets reales del piloto. **No se compra el lote real todavía.**
2. Después, el lote real: **número de personal con pulsera + 15 % de reserva**, numerado.
3. Aparte, **un lector USB de NFC** compatible con el chip, y **al menos una tablet Android con NFC y Chrome** para probar. Muchas tablets baratas no traen NFC y el iPad queda fuera (ADR 0009).

**Antes de comprar nada** deben estar decididos: B corregida y solo Auxiliar (hecho el 2026-11-20), la lista blanca de acciones (CJ) y quién va a operar el lector y la herramienta de personalización (hoy, una sola persona).

## 2. Requisitos del producto

| Requisito | Por qué |
|---|---|
| Chip **NXP NTAG 424 DNA** original, con **certificado de autenticidad**. No sirven NTAG 213/215/216 ni MIFARE Classic | Solo el 424 DNA firma un mensaje distinto en cada lectura. Hay chips falsificados (suposición) |
| Entrega **sin datos escritos por el proveedor**: sin dirección web suya ni servicio de validación propio, con claves de transporte o por defecto | Que el proveedor no conozca las claves finales y no meta a un tercero en la lectura |
| **Random ID activado por nosotros** al personalizar (es irreversible: no debe venir activado por el proveedor con claves ajenas) | Evita que un tercero siga a la persona con el número de serie fijo |
| **Número de serie solo cifrado** dentro del mensaje, **sin dato del centro** en la dirección (ruta genérica) | Un tercero que lea el mensaje no debe saber de qué centro es |
| **Escritura del mensaje protegida por clave** (a verificar los derechos por defecto) | Que otro móvil no pueda reescribirlo |
| **Sin límite de lecturas** configurado en el chip | AN12196 lo avisa como riesgo de denegación de servicio |
| Pulsera de **silicona**, resistente al gel hidroalcohólico y al lavado, con cierre que se **cambie sin herramientas** (o precintado de un solo uso: ver la consulta laboral) | Uso diario en planta. No es de privacidad |
| **Sin nombre, foto, número de empleado ni código** que coincida con algo del sistema. Si lleva número visible, es de lote y no coincide con el del chip | Que el proveedor no reciba ni imprima datos personales |
| Declaración del fabricante sobre materiales (silicona en contacto con la piel) | Seguridad del producto |
| **Lote numerado** y trazable por caja | Custodia y existencias |

**El PIN no va en la pulsera:** el chip no lo guarda. El PIN solo lo conoce la persona y lo comprueba ResidApp.

## 3. Lo que NO se pide ni se envía al proveedor

- **Ningún dato personal**: ni nombres, ni listas de personal, ni asignaciones. Así el proveedor es un **proveedor de bienes** y no un encargado del tratamiento (art. 28 del RGPD). Si grabara nombres, sería encargado: el centro tendría que autorizarlo por escrito y habría que trasladarle las mismas obligaciones. **Se evita no enviándole nada.**
- **Ninguna clave final.** La mejor opción es que el proveedor programe con **claves de transporte** y que ResidApp las **cambie al recibir**, con un lector USB; así, si el proveedor las filtra, ya no valen. Si ofrece «claves a medida», hay que ver cómo se entregan (ver apartado 5).
- **Ninguna clave de producción mezclada con las de pruebas.** Claves distintas por **lote, centro y entorno**; nada se reutiliza entre clientes.

## 4. Lo que hay que tener para personalizar las pulseras

**Web NFC no puede cambiar las claves de un NTAG 424 DNA** (solo lee datos NDEF; el cambio de clave va por comandos de bajo nivel). Por eso hace falta, **fuera de la aplicación web**:

- Un **PC con lector USB de NFC** y un programa propio (por ejemplo, un script en `tools\`) que: cambie las claves, active **Random ID**, configure el mensaje con firma, proteja la escritura y compruebe una lectura.
- Un sitio y una persona responsables: el equipo y quien lo usa **forman parte de la cadena de custodia** de las claves.

## 5. Preguntas para el fabricante o el distribuidor (a verificar antes de pedir)

1. ¿Entregáis el NTAG 424 DNA **sin Random ID activado** y con claves de transporte, para activarlo nosotros?
2. ¿Qué **derechos de acceso** trae por defecto el fichero del mensaje (lectura y escritura sin clave)?
3. ¿Ofrecéis **claves a medida**? Si es así: ¿cómo las generáis, cómo nos las entregáis (canal cifrado), cuánto tiempo las retenéis y certificáis su **destrucción**?
4. ¿Podéis **no escribir nada** ni usar vuestro servicio de validación, y entregarlas **numeradas por lote**?
5. ¿Certificado de **autenticidad** del chip y declaración de **materiales** de la pulsera?
6. ¿Plazo, mínimo de pedido, **lote de prueba de 5 a 10 unidades**, y precio por unidad en cada tramo? (Precio: **a pedir**; no hay cifra consultada.)
7. ¿Qué **lector USB** recomendáis para cambiar claves y activar Random ID, y qué herramienta oficial de NXP existe?
8. ¿Cómo responde la pulsera con **guantes, humedad y puesta en la muñeca** a la distancia de lectura de una tablet?

## 6. Cláusulas del contrato de suministro (no son las del art. 28 si no hay datos personales)

- Confidencialidad de las claves y **destrucción comprobable** de las de transporte tras el trabajo.
- Claves distintas por lote y por centro; **nunca reutilizadas** entre clientes.
- Entrega de claves por **canal cifrado**.
- **No subcontratar** la programación sin permiso.
- **Aviso inmediato** de cualquier incidente que afecte a las claves.
- Certificado de autenticidad del chip y especificación completa del lote.

## 7. Recepción y custodia del lote (lista de comprobación)

1. Contar las cajas y comprobar la **numeración del lote** con el albarán; anotar quién recibe.
2. Probar **una pulsera por caja** con el lector: que se lee, que el chip es el esperado, que **no viene con Random ID activado** (si el proveedor no lo activó) y que las claves son las de transporte.
3. **Personalizar** las pulseras (cambiar claves, activar Random ID, configurar el mensaje, proteger la escritura) y **probar una lectura** de cada una.
4. Guardar el lote **bajo llave**, con registro de quién retira cada pulsera y a quién se entrega, y con cuenta de existencias (asignadas, sin asignar, retiradas).
5. **Las claves** de cada lote se guardan **fuera del repositorio y de la base de datos** (Key Vault en producción; valores de prueba en desarrollo) con una versión (`kid`).

## 8. Cantidades y reposición

- **Lote real = personal con pulsera + 15 % de reserva.** Rotar las claves de las pulseras **equivale a cambiarlas** (el cambio de clave exige la herramienta de personalización), así que la reserva sirve también para una rotación de emergencia ante sospecha. No hay rotación periódica por calendario.
- Si se pierde una caja sin asignar, **las pulseras no sirven** mientras no se den de alta (el alta exige a Administración y a la persona), pero hay que registrar la pérdida.

## 9. Pruebas del lote de prueba (con las tablets reales)

Antes de comprar el lote real, comprobar con 5 a 10 pulseras:

- Lectura con la pulsera **puesta**, con **guantes**, con **humedad** y con la tablet en su funda.
- Que la tablet **tiene NFC activado**, es **Android con Chrome** y la página lee la etiqueta con Web NFC en primer plano.
- La pulsera **con Random ID activado** (es lo que se usará).
- Qué ocurre si **otro móvil** abre el mensaje: la dirección debe abrir una página inocua que no comprueba ni registra nada.
- **Intentos de reescritura** desde otro móvil: no debe poder.
- Si la tablet o el móvil de un tercero ve el mensaje, que no revele el centro ni la persona.

## 10. Alternativa anotada, no elegida

Una **llave de seguridad FIDO2 con NFC y PIN propio** evitaría que ResidApp custodie el PIN y las claves del chip, resiste a webs falsas y reutiliza las passkeys ya hechas. Se **descartó** el 2026-11-20 por coste (decenas de euros por unidad frente a un euro o dos), formato (llavero o tarjeta, no pulsera) y porque no está comprobada con las tablets. Queda como opción futura si el coste de custodiar el PIN y las claves resulta alto.

## 11. Lo que no se ha podido confirmar (y quién lo confirma)

- Comportamiento exacto de Web NFC con Random ID y con la dirección abierta fuera de la página: **prueba con aparatos reales** (apartado 9).
- Derechos de acceso por defecto, qué entregan los proveedores y los servicios de «claves a medida»: **el fabricante o el distribuidor** (apartado 5).
- Precio por unidad y del lector USB: **ofertas** (no consultadas).
- Si el centro piloto tiene tablets con NFC: **el centro**.
