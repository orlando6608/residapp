# ADR 0018 — Toda la aplicación de los centros en castellano y catalán

Estado: **propuesta** (2026-11-24). Decididas por Orlando las siete cuestiones que dejó la lectura del DPO; falta la validación de una persona experta (DPO real o abogado) en lo que se marca abajo. **Nada está construido.** Amplía el [ADR 0013](0013-portal-familiar-dos-idiomas.md): donde el 0013 deja la parte profesional fuera de alcance, manda este; lo demás del 0013 sigue vigente (valor cerrado `es`/`ca`, idioma nunca inferido, parámetro `?idioma=` que no escribe en la cuenta, cookie solo de idioma en las pantallas de entrada).

Fuentes: traspaso `docs/tareas/alta-prioridad/pendientes-catalan.md` (respuestas de CJ y decisiones de Orlando), lectura `docs/privacidad/app-catalan-castellano-lectura-dpo.md`.

## Contexto

La aplicación está solo en castellano (`Program.cs` admite únicamente `es-ES`). Hacen falta los dos idiomas desde el primer uso en Cataluña, también para el personal. Tamaño estimado: 6.000 a 9.000 textos (262 vistas, controladores, 154 mensajes de validación, enums, catálogos, correos, PDF, Manual y guías).

## Decisión

1. **Alcance:** toda la aplicación de los centros (personal y familias) en castellano y catalán. **Plataforma queda fuera**, siempre en castellano. Lo que escribe o dicta el personal no se traduce, ni con IA (ADR 0011).
2. **Quién decide el idioma de cada petición:** un solo punto. Personal y familias: el idioma de su cuenta; si no lo ha elegido, el del centro (que fija el administrador del centro); antes de entrar, selector, parámetro o castellano (ADR 0013, punto 4).
3. **Idioma del personal:** valor cerrado `es`/`ca`, lo cambia la propia persona en su perfil. Es dato laboral: ninguna pantalla ni exportación lo muestra de otras personas, y no entra en controles de la persona trabajadora (LOPDGDD arts. 87 a 91).
4. **Documentos hacia residentes y familias** (PDF e impresión): el idioma se **elige al generarlos**, con el del centro por defecto, y queda **impreso en el documento**. No se crea un «idioma del residente».
5. **Textos legales** (privacidad, cookies, cláusulas): las dos versiones son oficiales y se mantienen idénticas. La fórmula la fija un abogado. No se traduce `Privacy.cshtml` hasta que el castellano esté validado.
6. **Lo que se guarda va como código, no como frase traducida** (registros clínicos, auditoría, catálogos): se guarda el código y se traduce la etiqueta. Hoy `audit_events` ya guarda códigos. Los nombres de equipo (`equipos.nombre_visible`) son texto libre del centro y no se traducen.
7. **Cuidados de construcción:** mensajes de acceso y recuperación idénticos en los dos idiomas (no deben delatar si una cuenta existe); búsqueda de personas insensible a acentos y orden; números clínicos probados con `ca-ES` antes de cambiar la cultura; una aceptación o consentimiento guardado lleva versión e idioma.
8. **Traducción:** ficheros de traducción por área con castellano como respaldo por clave. La prueba «los dos ficheros tienen las mismas claves» se exige en las áreas marcadas como traducidas. Glosario versionado (TERMCAT, DEMCAT; forma oficial por defecto, con las excepciones de CJ). La traducción con IA en desarrollo solo sobre ficheros de texto: nunca capturas, pantallas con datos, respuestas de CJ ni el Manual con imágenes.
9. **Revisión:** CJ y personas del centro de cada perfil revisan pantallas, Manual y guías; la persona experta que valide el castellano revisa el catalán jurídico. Sin traductor externo por ahora (si lo hubiera, solo con ficheros de texto, sin Azure ni datos reales).
10. **Dictado en catalán:** fuera de la primera versión. Antes, probar Voxtral con voz real en catalán y actualizar el ADR 0011 con el DPO.
11. **Módulo base:** el catalán no se cobra aparte.
12. **Entrega:** por áreas completas en desarrollo; a producción no sale con áreas a medias. Orden orientativo (CJ, 4.2): mecanismo, entrada y Portal Familiar; campo de la cuenta, selector e idioma del centro; correos; Medicina, Administración y Dirección; resto del personal; PDF. Cada área lleva su Manual y su guía.

## Pendiente de validación experta

Regla de idioma por documento; cláusula de prevalencia y obligación lingüística (normativa catalana, no está en `normativa-local`); texto catalán de privacidad, cookies y cláusulas; base jurídica del idioma del personal; autoridad de control (AEPD o APDCAT, según la titularidad y financiación del centro piloto, que se desconocen); caducidad de la cookie; régimen del traductor y de las herramientas de IA.

## Fuera de alcance

Más idiomas que `es` y `ca`; Plataforma; traducir lo que escribe el personal; dictado en catalán (primera versión).

## Consecuencias

- Hay que reescribir la parte de la landing catalana y del correo que dice que la aplicación está en castellano, y actualizar `docs/INDICE.md`, al aceptar el ADR.
- Un script nuevo para el idioma de la cuenta y del centro (siguiente número libre al empezar; comprobar `origin/main`).
- El número 0018 se comprueba contra `origin/main` antes de fusionar (otras sesiones pueden haberlo tomado).
