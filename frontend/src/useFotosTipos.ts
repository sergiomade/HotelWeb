import { useEffect, useState } from 'react'
import { fotosTipos, type FotoTipo } from './api'

/** Las fotos de los tipos (una consulta para toda la pagina). version cambia para volver a pedirlas. */
export function useFotosTipos(version = 0): FotoTipo[] {
  const [fotos, setFotos] = useState<FotoTipo[]>([])
  useEffect(() => {
    let vivo = true
    fotosTipos(version > 0).then((f) => vivo && setFotos(f)).catch(() => {
      // sin fotos no pasa nada: la pantalla sigue igual
    })
    return () => { vivo = false }
  }, [version])
  return fotos
}
