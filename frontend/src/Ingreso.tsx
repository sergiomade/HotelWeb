import { useState, type FormEvent } from 'react'
import { ingresar, type Sesion } from './api'

/** Ingreso con el mismo usuario y clave de Delphi. */
export default function Ingreso({ alIngresar }: { alIngresar: (s: Sesion) => void }) {
  const [usuario, setUsuario] = useState('')
  const [clave, setClave] = useState('')
  const [error, setError] = useState('')
  const [enviando, setEnviando] = useState(false)

  async function enviar(e: FormEvent) {
    e.preventDefault()
    setError('')
    setEnviando(true)
    try {
      alIngresar(await ingresar(usuario, clave))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setClave('')
    } finally {
      setEnviando(false)
    }
  }

  return (
    <main className="ingreso">
      <form className="tarjeta" onSubmit={enviar}>
        <img src="/hotel.svg" alt="" width={48} height={48} />
        <h1>Hotel</h1>
        <p className="tenue">Use el mismo usuario y clave del programa del hotel.</p>
        <label>
          Usuario
          <input value={usuario} onChange={(e) => setUsuario(e.target.value)} autoFocus autoComplete="username" />
        </label>
        <label>
          Clave
          <input type="password" value={clave} onChange={(e) => setClave(e.target.value)}
                 maxLength={10} autoComplete="current-password" />
        </label>
        {error && <p className="error" role="alert">{error}</p>}
        <button type="submit" disabled={enviando || !usuario || !clave}>
          {enviando ? 'Entrando...' : 'Entrar'}
        </button>
      </form>
    </main>
  )
}
