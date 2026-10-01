# ADR 0006 — Perfil de plataforma y centro reservado

Estado: aceptada el 2026-10-01 (decisión del usuario). Implementada en el script `0026_perfil_plataforma`.

## Contexto

Un centro nuevo necesitaba SQL a mano: el centro, su primera unidad, la cuenta del primer administrador y su ámbito de
Administración con la unidad concedida. Sin eso, nadie podía entrar en la pantalla de Estructura ni dar de alta residentes (una
unidad solo se ve si está concedida al ámbito). Los documentos dicen que Administración «no provisiona centros»: la creación
tenía que hacerla alguien que no fuera un perfil de centro.

## Decisión

1. **Séptimo perfil del sistema, `PLATAFORMA`** («Plataforma» en pantalla). Lo usa quien opera ResidApp, no el personal de un
   centro. Hace una sola cosa: dar de alta un centro con su primera unidad y su primer administrador, en una transacción
   (`/Plataforma`). No ve residentes ni contenido clínico, y los demás servicios lo deniegan (hay un test por servicio).
2. **Centro reservado «Plataforma»** (código `PLATAFORMA`, id fijo `5F3A1C00-0000-4000-8000-000000000001`). `ambitos_perfil.centro_id`
   es `NOT NULL` y toda la autorización, la cookie del ámbito activo y la selección de ámbito trabajan con un centro; un perfil sin
   centro obligaba a cambiarlo todo. El ámbito del operador pertenece a este centro. `TR_ps_platform_center` hace que `PLATAFORMA`
   solo valga ahí y que ahí no valga ningún otro perfil. Este centro no tiene unidades ni residentes y no sale en la lista de centros.
3. **No se concede desde Usuarios** (`ProfessionalAccount.GrantableProfiles` no lo incluye) y no tiene permisos configurables.
   Suspender o revocar a un operador se hace por SQL.
4. **El administrador del centro nuevo es una cuenta nueva.** Si el identificador ya existe (en cualquier centro), es un conflicto:
   vincular una cuenta existente sigue fuera, como en Usuarios.
5. **Auditoría sin datos**, con perfil activo `PLATAFORMA` y el centro nuevo como centro del evento: `CENTER_CREATE`,
   `UNIT_CREATE`, `ACCOUNT_CREATE`, `PROFILE_SCOPE_GRANT` y `PROFILE_UNIT_GRANT`.

## Raíz de confianza

La primera cuenta de plataforma no la crea la aplicación: alguien tiene que ser de confianza antes de que exista ninguna cuenta.
- **Desarrollo:** `database/seed/dev_seed_plataforma.sql` crea `dev-plataforma`.
- **Entorno real:** un `INSERT` único, hecho por quien administra la base de datos, de la cuenta y de su ámbito en el centro
  reservado (ver el seed como plantilla: una fila en `cuentas` y otra en `ambitos_perfil` con `perfil_codigo = 'PLATAFORMA'` y
  `centro_id = '5F3A1C00-0000-4000-8000-000000000001'`). No hay arranque automático por configuración.

## Consecuencias

- Cambia la frase «Administración no crea centros; los centros los provisiona la plataforma»: sigue siendo cierta, y «la plataforma»
  es ahora este perfil.
- Quien conceda el perfil Plataforma por SQL concede la capacidad de crear centros y cuentas de Administración: es la cuenta más
  sensible del sistema. Cuando exista el proveedor de identidad real, estas cuentas deberían exigir su autenticación reforzada.
- No hay editar, inactivar ni borrar centros desde la aplicación; llegarán si hace falta.
