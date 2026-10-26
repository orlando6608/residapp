# Formas de acceso e identificación por tipo de dispositivo

Estado: borrador de análisis (2026-10-26). **No es una decisión.** Resume lo que dicen el ADR 0007 (proveedor de identidad) y el ADR 0009 (NFC y voz),
y añade una valoración de qué encaja en cada dispositivo. Lo marcado como *valoración* no está en ningún ADR: es una opinión a revisar con CJ y con el DPO.
Se retoma cuando se implemente **I5**.

## Para qué sirve

Los ADR hablan de «tablets compartidas» y «móviles de familiares», pero no de cada combinación de dispositivo y propietario. Esta tabla las recorre
todas para ver dónde basta lo que ya existe y dónde aporta algo la pulsera NFC con PIN.

## Formas de acceso

**Hechas (ADR 0007, I0 a I4):**

- **Contraseña (12 caracteres o más) más TOTP** (código de una aplicación autenticadora): obligatorio para el personal.
- **Passkey** (llave de acceso: huella, cara o PIN del dispositivo): opcional para el personal con TOTP. Sustituye a contraseña y código. Solo funciona
  si está configurado `Acceso:PasskeyDominio`.
- **Familiares:** solo contraseña, hasta que CJ responda la pregunta 3.2 del portal familiar.
- **Plataforma:** solo passkey en cuanto tiene una.
- **Sesión:** 30 minutos de inactividad y 12 horas absolutas. El tiempo de inactividad definitivo de las tablets compartidas sigue abierto.

**Pendiente (I5, ADR 0009):** pulsera NFC pasiva (chip NTAG 424 DNA) del auxiliar más PIN, como entrada rápida. Limitaciones:

- Web NFC solo funciona en **Chrome para Android**, con la página en primer plano. No existe en ordenador, iPhone ni iPad.
- Es para el personal (auxiliar), no para familiares.
- La pulsera sola no autentica: se exige el PIN. Administración puede revocarla.
- Sin decidir: si pulsera más PIN **sustituye** a contraseña más TOTP o se suma a ellos.

## Tabla

| Dispositivo | Propietario | Acceso actual | Con I5 |
|---|---|---|---|
| Ordenador compartido | Residencia | Contraseña + TOTP. Passkey *encaja mal* (está ligada a una persona). | No aplica: Web NFC no existe en ordenador. |
| Ordenador personal en casa | Usuario | Contraseña + TOTP, o passkey. Familiares: solo contraseña. | No aplica. |
| Tablet individual | Residencia | Contraseña + TOTP, o passkey (*encaja* si cada tablet tiene un solo usuario). | Pulsera + PIN si es Android. Aporta poco frente a la passkey. |
| Tablet compartida | Residencia | Contraseña + TOTP en cada turno. Passkey *encaja mal*. Es el caso que motivó I5. | **Pulsera + PIN** si es Android. El caso principal. |
| Teléfono compartido | Residencia | Igual que la tablet compartida. | **Pulsera + PIN** si es Android. |
| Teléfono individual | Residencia | Contraseña + TOTP, o passkey (*encaja*). | Opcional, si es Android. |
| Teléfono individual en la residencia | Usuario | Contraseña + TOTP, o passkey (la opción más cómoda). | Opcional, si es Android. |
| Teléfono individual en casa | Usuario | Contraseña + TOTP, o passkey. Familiares: solo contraseña. | Opcional para el personal con Android. No aplica a familiares. |

## Matices

- **Dispositivos que no son Android** (iPhone, iPad, ordenadores): nunca podrán usar la pulsera. Siguen con contraseña y TOTP, o con passkey.
- **Passkey en dispositivos compartidos** (*valoración*): no hay nada en los ADR. Encaja mal porque la credencial se desbloquea con la huella o el PIN de
  una persona y se guarda en su perfil del dispositivo.
- **Pulsera en dispositivos individuales** (*valoración*): la passkey ya da una entrada igual de rápida y más resistente al phishing, sin hardware ni PIN que
  custodiar. La pulsera solo aportaría algo a quien no use passkey.
- **Acceso desde casa del personal:** los documentos no dicen si está permitido ni restringido. Hoy no hay restricción por lugar en lo que consta.
- **Familiares:** I5 no cambia nada para ellos.

## Preguntas abiertas antes de implementar I5

1. ¿Pulsera + PIN sustituye al TOTP o se suma? (CJ y DPO).
2. Tiempo de inactividad definitivo de las tablets compartidas (puerta abierta 3 del ADR 0007).
3. Alta, baja y sustitución de pulseras desde la aplicación.
4. Modelo de amenazas de la pulsera con PIN: pérdida, robo, préstamo.
5. Si la passkey se admite o no en dispositivos compartidos.

## Fuentes

- `docs/decisiones-arquitectura/0007-proveedor-identidad.md` (incrementos I0 a I5, puertas abiertas).
- `docs/decisiones-arquitectura/0009-nfc-voz-acceso-fisico.md` (pegatina, pulsera, dispositivos, alcance PWA).
- `docs/producto/estudio-mercado-ux-2026-10.md` (hallazgo 6 y recomendación R5).
