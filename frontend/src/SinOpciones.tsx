/** Un area del menu sin ninguna pestana permitida para el usuario (la seguridad le dio el area pero ninguna opcion). */
export default function SinOpciones({ area }: { area: string }) {
  return (
    <section className="catalogos">
      <h2>{area}</h2>
      <p className="tenue">Su usuario no tiene ninguna opción de {area}. Pídasela a quien administra la seguridad.</p>
    </section>
  )
}
