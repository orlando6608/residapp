const foundations = [
  {
    label: "Alcance",
    title: "Primer dominio preparado",
    detail: "D1, Drizzle y la migración inicial integrada sostienen el siguiente bloque vertical.",
  },
  {
    label: "Privacidad",
    title: "Solo datos ficticios",
    detail: "No está autorizada para tratar información real de residentes, familias o profesionales.",
  },
  {
    label: "Seguridad",
    title: "Denegación por defecto",
    detail: "Las futuras operaciones protegidas deberán autorizarse en el servidor antes de ejecutarse.",
  },
] as const;

export default function HomePage() {
  return (
    <main className="home-shell">
      <section className="status-panel" aria-labelledby="page-title">
        <p className="eyebrow">Base técnica · Prototipo local</p>
        <h1 id="page-title">La aplicación está preparada para crecer por bloques seguros.</h1>
        <p className="lead">
          Next.js, React, TypeScript, D1 y Drizzle están operativos. La migración inicial se validó
          localmente con datos sintéticos; las pantallas asistenciales continúan fuera de este incremento.
        </p>

        <div className="foundation-grid" aria-label="Principios activos de esta base">
          {foundations.map((foundation) => (
            <article className="foundation-card" key={foundation.label}>
              <p className="card-label">{foundation.label}</p>
              <h2>{foundation.title}</h2>
              <p>{foundation.detail}</p>
            </article>
          ))}
        </div>

        <aside className="scope-note" aria-label="Límite de uso del prototipo">
          <span aria-hidden="true">i</span>
          <p>
            Esta aplicación no sustituye la historia clínica oficial, los protocolos del centro ni los
            canales de emergencia.
          </p>
        </aside>
      </section>
    </main>
  );
}
