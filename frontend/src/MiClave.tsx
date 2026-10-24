import { useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import { cambiarMiClave, SesionVencida } from './api'

/** Cada usuario cambia su clave. Es la misma de Delphi: la nueva vale para los dos. */
export default function MiClave({ alCerrar, alVencer }: { alCerrar: () => void; alVencer: () => void }) {
  const [actual, setActual] = useState('')
  const [nueva, setNueva] = useState('')
  const [repetida, setRepetida] = useState('')
  const [error, setError] = useState('')
  const [hecho, setHecho] = useState(false)
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  const valido = actual !== '' && nueva.length >= 4 && nueva === repetida

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (!valido) return
    setGuardando(true)
    setError('')
    try {
      await cambiarMiClave(actual, nueva)
      setHecho(true)
    } catch (x) {
      if (x instanceof SesionVencida) alVencer()
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  return (
    <Fondo centro>
      <form className="dialogo" role="dialog" aria-modal="true" aria-label="Cambiar mi clave" onSubmit={guardar}>
        <h2>Cambiar mi clave</h2>
        {hecho ? (
          <>
            <p className="ok-texto" role="status">Clave cambiada. Úsela también en los programas de Delphi.</p>
            <div className="botones"><button type="button" onClick={alCerrar} autoFocus>Cerrar</button></div>
          </>
        ) : (
          <>
            <label>
              Clave actual
              <input type="password" maxLength={10} value={actual} onChange={(x) => setActual(x.target.value)} autoFocus autoComplete="current-password" />
            </label>
            <label>
              Clave nueva (4 a 10, sin acentos ni ñ)
              <input type="password" maxLength={10} value={nueva} onChange={(x) => setNueva(x.target.value)} autoComplete="new-password" />
            </label>
            <label>
              Repita la clave nueva
              <input type="password" maxLength={10} value={repetida} onChange={(x) => setRepetida(x.target.value)} autoComplete="new-password" />
              {repetida !== '' && nueva !== repetida && <span className="aviso-texto mal">No coinciden.</span>}
            </label>
            {error && <p className="error" role="alert">{error}</p>}
            <div className="botones">
              <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
              <button type="submit" disabled={!valido || guardando}>{guardando ? 'Guardando...' : 'Cambiar'}</button>
            </div>
          </>
        )}
      </form>
    </Fondo>
  )
}
