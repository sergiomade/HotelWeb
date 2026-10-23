import { useEffect } from 'react'

/**
 * Pantalla de espera mientras se habla con la DGII (como MostrarEspera de Delphi: "CONECTANDO CON LA DGII, ESPERE...").
 * Tapa todo y no deja cerrar el panel (ni con Escape) hasta que llegue la respuesta, para no perderla.
 */
export default function Espera({ texto }: { texto: string }) {
  useEffect(() => {
    // va antes que el Escape del panel (captura) y lo detiene
    const tecla = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopImmediatePropagation()
        e.preventDefault()
      }
    }
    window.addEventListener('keydown', tecla, true)
    return () => window.removeEventListener('keydown', tecla, true)
  }, [])

  return (
    <div className="espera" role="alertdialog" aria-modal="true" aria-live="assertive" aria-label={texto}>
      <div className="espera-caja">
        <div className="espera-rueda" aria-hidden="true" />
        <p><strong>{texto}</strong></p>
        <p className="tenue">No cierre esta ventana. Puede tardar hasta 2 minutos.</p>
      </div>
    </div>
  )
}
