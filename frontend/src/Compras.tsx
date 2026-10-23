import Gastos from './Gastos'

/** Compras y gastos con e-CF (etapa 9d): gastos menores (E43). Las compras a informales (E41) van en el siguiente paso. */
export default function Compras({ alVencer }: { alVencer: () => void }) {
  return (
    <section className="catalogos">
      <h2>Compras y gastos</h2>
      <div className="pestanas" role="tablist">
        <button type="button" role="tab" aria-selected className="activo">Gastos menores (E43)</button>
      </div>
      <Gastos alVencer={alVencer} />
    </section>
  )
}
