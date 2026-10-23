# ADR 0009 — NFC para identificar camas y auxiliares, y dictado de reportes con voz

Estado: aceptada el 2026-10-13 (decisión del usuario). **Dictado:** prueba de precisión (Fase A) hecha; el servidor (Fase B, tramo 1, 2026-10-23) está
construido y probado con proveedores falsos, sin temperatura hasta que CJ fije su rango; falta la pantalla (tramo 2). **NFC:** sin implementar. Modelos
fijados: `voxtral-mini-latest` (transcripción) y `mistral-small-latest` (salida estructurada, esquema estricto).

## Contexto

Se estudia reducir errores de identificación del residente, acelerar el acceso de los auxiliares y facilitar el registro del estado
de los residentes. ResidApp es una PWA; la lectura de NFC desde una web (Web NFC) solo funciona en Chrome para Android.

## Decisión

Tres piezas, en este orden:

1. **Pegatina NFC pasiva en la cama, con chip seguro, para abrir la ficha del residente.** Chip tipo NTAG 424 DNA (código firmado distinto
   en cada lectura, verificado en servidor). Una pegatina NFC ordinaria se puede clonar y no sirve.
2. **Dictado de reportes con Voxtral (Mistral), con confirmación obligatoria.** El audio se transcribe y un modelo propone los campos
   del formulario; el auxiliar revisa y confirma antes de guardar. Nada se guarda sin esa confirmación. Los valores numéricos se
   validan en servidor contra rangos, como cualquier otro dato. **La confirmación de los valores clínicos se hace siempre en
   pantalla**, no por lectura en voz alta ni por respuesta hablada: el auxiliar ve lo que se va a guardar. Un auricular con micrófono
   es una mejora opcional (para la web es solo otro micrófono), siempre con la pantalla encendida.
3. **Pulsera NFC pasiva del auxiliar + PIN como login rápido, también con chip seguro.** La pulsera sola no autentica (factor de
   posesión); se exige PIN. Administración puede revocarla al instante.

## Flujo del dictado

1. La PWA graba el audio y lo envía al servidor. **La transcripción se hace en el servidor con Voxtral**, no con el reconocimiento de
   voz del navegador: el de Chrome para Android suele enviar el audio a Google, un segundo tratamiento de datos de salud fuera del
   contrato de encargado con Mistral.
2. El servidor pasa el texto a un modelo de la familia Mistral Small con **salida estructurada de esquema estricto** (la versión
   concreta se fija al implementar, comprobando que la soporta). La clave de Mistral nunca llega a la PWA.
3. La PWA recibe el objeto, rellena los inputs y marca los campos rellenados por voz. El auxiliar confirma en pantalla y el POST va
   por el endpoint normal, con su validación de siempre.

Presentación en pantalla (la guía visual vigente sigue mandando):

- Los campos rellenados por voz se distinguen con un **color neutro de información, no verde** (en la guía, el verde significa
  «estable o resuelto» y aquí invitaría a dar por bueno lo que nadie ha validado), un **icono y la etiqueta «Sugerido por voz»**,
  para que no dependan solo del color (WCAG 1.4.1).
- La marca de un campo desaparece cuando el auxiliar lo toca, lo edita o lo confirma.
- **Modo estricto en valores clínicos:** cada valor clínico rellenado por voz exige revisión individual y el botón de guardar
  permanece desactivado hasta que todos estén revisados. Los campos no clínicos solo llevan la marca.

Reglas:

- Un esquema por tipo de reporte, con todos los campos opcionales: lo que no se dijo llega vacío; el modelo no inventa valores. Los
  enums del esquema usan los valores del dominio (y se muestran en español en pantalla).
- La salida del modelo es una **sugerencia, no un dato fiable**: se valida en servidor como cualquier entrada.
- El residente **no se identifica por voz**: lo fija el NFC de la cama o la selección en pantalla, y su nombre no se envía a Mistral.
- No se guarda el audio ni la transcripción; solo un evento de auditoría de que se usó el dictado, sin datos.
- Sin conexión o ante un fallo del servicio, el formulario manual sigue funcionando.

## Dispositivos

Móviles personales Android, o tablets y móviles de la residencia, también Android. Quien use un iPhone u otro dispositivo no Android
queda fuera de las ventajas NFC (lectura de cama y de pulsera): sigue usando la aplicación con el acceso y la búsqueda de residente
habituales.

## Alcance: solo PWA

ResidApp se mantiene como PWA; se evita una app nativa. Por eso el uso con la pantalla apagada o la aplicación en segundo plano queda
fuera de alcance: Chrome para Android puede cortar el micrófono en segundo plano y Web NFC solo lee con la página en primer plano.
Si en el futuro ese uso se volviera imprescindible, se evaluaría aparte, con su propio coste y revisión de seguridad.

## Consecuencias y condiciones

- La voz y la transcripción son datos de salud: contrato de encargado de tratamiento con el proveedor, comprobar región de
  procesamiento y que no se retenga el audio, y evaluación de impacto antes de producción.
- La clave del proveedor vive en `dotnet user-secrets` (desarrollo) y en configuración segura del entorno, nunca en `appsettings.json`.
- Probar la precisión del dictado con audios reales de planta (ruido, acentos, jerga) antes de comprometer el diseño.
- Las etiquetas y pulseras necesitan alta, baja y sustitución desde la aplicación (qué cama o qué cuenta representa cada chip).
