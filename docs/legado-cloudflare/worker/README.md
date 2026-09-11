# Runtime de Cloudflare Workers

La base usa vinext, Vite y el adaptador oficial de Cloudflare para validar compatibilidad con Workers. El punto de entrada estándar es `vinext/server/app-router-entry`, declarado en `wrangler.jsonc`, que también declara el binding D1 `DB` y el directorio de migraciones integrado.

No se añade un worker personalizado ni infraestructura remota. La migración `0001` solo se aplica a una D1 local desechable con datos sintéticos; no existe ni se autoriza una aplicación remota. Un punto de entrada propio solo se incorporará cuando una necesidad funcional revisada lo justifique.
