import { useRef, type ReactNode } from 'react'

/**
 * Fondo oscuro detras de un dialogo o panel.
 *
 * - Con `alCerrar`: se cierra al tocar el fondo, pero solo si el clic EMPIEZA y TERMINA en el fondo. Si se aprieta
 *   el mouse dentro (por ejemplo, seleccionando texto) y se suelta fuera, el navegador lo cuenta como un clic en el
 *   fondo: asi se cerraban los formularios a mitad del trabajo (2026-10-22).
 * - Sin `alCerrar` (los formularios donde se escribe): tocar el fondo no hace nada; se cierran con Cancelar,
 *   Guardar o Esc.
 */
export default function Fondo({ alCerrar, centro, children }: {
  alCerrar?: () => void
  centro?: boolean
  children: ReactNode
}) {
  const empezoEnFondo = useRef(false)
  return (
    <div className={`fondo-panel${centro ? ' centro' : ''}`}
         onMouseDown={(e) => { empezoEnFondo.current = e.target === e.currentTarget }}
         onClick={(e) => {
           if (alCerrar && empezoEnFondo.current && e.target === e.currentTarget) alCerrar()
           empezoEnFondo.current = false
         }}>
      {children}
    </div>
  )
}
