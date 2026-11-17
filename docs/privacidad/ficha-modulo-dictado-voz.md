# Ficha de privacidad del módulo «Dictado por voz» — versión 1

**Estado:** borrador (2026-11-16), a partir de la lectura de `dpo-residapp` para el PR 1 del ADR 0015. **Lo valida una persona** (DPO real o abogado) **antes de activar el módulo en un centro con datos reales.** Esta es la versión que guarda `modulos_centro.version_ficha` al activarlo (`PrivacySheetVersion` del catálogo en `src/ResidApp.Domain/Modules/CenterModules.cs`). Si cambia de forma sustancial, se sube la versión y el centro vuelve a aceptarla.

## Qué hace el módulo
El Auxiliar dicta de viva voz un cambio de un residente asignado. El servidor envía el audio a Mistral, que lo transcribe y devuelve una **sugerencia** de áreas y opciones; la persona la revisa antes de confirmar nada (ADR 0009 y 0011).

## Qué datos trata y qué conserva ResidApp
- Trata el audio y su transcripción, que pueden incluir datos de salud del residente, solo mientras dura la petición.
- **ResidApp no guarda ni el audio ni la transcripción.** Por eso, al apagar el módulo no queda nada propio en solo lectura: para este módulo, «solo lectura» equivale a «no disponible» (el botón desaparece y el servidor responde 404 sin llamar a Mistral). El catálogo lo marca con `HasOwnData = false`.
- No hay contadores de uso por persona.

## Quién interviene
- **Mistral** es el único proveedor autorizado (ADR 0011), subencargado condicional del anexo de subencargados.
- La clave global `IA__Mistral__ApiKey` es el interruptor maestro: sin ella, ningún centro tiene el dictado, aunque su fila diga ACTIVO.

## Cómo se enciende y se apaga
- **Enciende y apaga solo Plataforma**, a petición de la persona autorizada del centro y en el plazo que fije el anexo del contrato. El centro no apaga por sí solo y no hay apagado de emergencia global dentro de la aplicación (decisión de Orlando, 2026-11-16).
- Cada cambio guarda cuándo llegó la petición, por qué canal y con una referencia que asigna ResidApp (módulo y número del cambio; nadie la teclea, así que no puede llevar nombres), la referencia y versión del anexo, la vigencia (obligatoria: el contrato fija la duración del tratamiento), esta versión de la ficha y los códigos de las condiciones comprobadas.
- **Al vencer la vigencia, el módulo deja de funcionar solo**, sin tarea programada.
- Existe un historial de activaciones, con la cuenta y el perfil de la persona operadora (dato seudonimizado del operador de Plataforma). El plazo de conservación está **sin decidir** (`antes-de-datos-reales.md`).

## Cómo se corta para todos los centros
Quitar el ajuste `IA__Mistral__ApiKey` de la Web App. Lo hace Plataforma. Procedimiento escrito (borrador, 2026-11-17): `procedimiento-corte-global-dictado.md`. Lo hace Orlando en menos de 4 horas en horario laborable; **falta un suplente** con permiso mínimo en Azure.

## Qué queda en Mistral al apagar
Con **retención cero por escrito**, nada. Sin ella, no se borra nada hasta que acabe el contrato con Mistral (anexo, cláusula 4.1.i).

## Condiciones que deben cumplirse antes de activarlo en un centro real
Son los códigos que guarda la activación (`condiciones_codigos`). **Hoy ninguna está cumplida.**

| Código | Condición |
|---|---|
| `ANEXO_FIRMADO` | El anexo de módulos del contrato, firmado, vale como instrucción documentada del centro (RGPD art. 28.3.a), con el plazo de apagado fijado y conciliado con la baja comercial. Lo valida el DPO real o un abogado. |
| `RETENCION_CERO_MISTRAL` | Retención cero por escrito de Mistral, con confirmación de que su acuerdo cubre datos de salud, región UE, entrenamiento desactivado y modelos no preliminares (anexo 4.1.c y 4.1.j). |
| `EIPD_CENTRO` | La evaluación de impacto del centro cubre el dictado. |
| `INFORMACION_PERSONAL_Y_RESIDENTES` | El personal y los residentes (o sus representantes) están informados (anexo 4.1.j). |

Además, antes de un centro real (no son casillas de esta ficha): contrato de encargado y anexo de subencargados con una parte contratante por el lado de ResidApp, política de privacidad corregida (incremento 6), salida completa de datos al fin del contrato (art. 28.3.g), suplente de Plataforma, aviso escrito al centro en cada alta, baja y vencimiento próximo, y el procedimiento de cuentas de personal para el operador que se va.
