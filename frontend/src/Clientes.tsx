import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  buscarClientes, crearCliente, modificarCliente, obtenerCliente, SesionVencida,
  type Cliente, type ClienteResumen, type DatosCliente, type PantallaClientes, type TipoComprobante,
} from './api'

type Edicion = { nuevo: true } | { nuevo: false; c: Cliente }

/** Clientes (etapa 2): la misma tabla de cuentas por cobrar (CxcCliente). No se borran: se ponen inactivos. */
export default function Clientes({ alVencer }: { alVencer: () => void }) {
  const [texto, setTexto] = useState('')
  const [datos, setDatos] = useState<PantallaClientes | null>(null)
  const [error, setError] = useState('')
  const [buscando, setBuscando] = useState(false)
  const [editando, setEditando] = useState<Edicion | null>(null)
  const [aviso, setAviso] = useState<{ texto: string; avisos: string[] } | null>(null)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(e instanceof Error ? e.message : String(e))),
    [alVencer],
  )

  const buscar = useCallback((t: string) => {
    setBuscando(true)
    buscarClientes(t)
      .then((r) => {
        setDatos(r)
        setError('')
      })
      .catch(manejar)
      .finally(() => setBuscando(false))
  }, [manejar])

  // busca mientras se escribe (espera a que se deje de escribir)
  useEffect(() => {
    const t = setTimeout(() => buscar(texto), texto ? 350 : 0)
    return () => clearTimeout(t)
  }, [texto, buscar])

  async function abrir(r: ClienteResumen) {
    try {
      setEditando({ nuevo: false, c: await obtenerCliente(r.numero) })
    } catch (e) {
      manejar(e)
    }
  }

  function alGuardar(c: Cliente, avisos: string[], nuevo: boolean) {
    setEditando(null)
    setAviso({ texto: `${nuevo ? 'Creado' : 'Guardado'} el cliente ${c.numero} ${c.nombre}.`, avisos })
    buscar(texto)
  }

  if (error && !datos) return <p className="error">{error}</p>
  if (!datos) return <p className="tenue">Cargando los clientes...</p>
  const p = datos.pagina

  return (
    <section>
      <div className="rack-cabecera">
        <p className="tenue">
          La misma lista de clientes de Delphi y de cuentas por cobrar. No se borran: se ponen inactivos.
          {!datos.puedeEditar && ' Su usuario solo puede consultar.'}
        </p>
        {datos.puedeEditar && <button type="button" onClick={() => setEditando({ nuevo: true })}>Nuevo cliente</button>}
      </div>

      <div className="filtros">
        <input placeholder="Buscar por nombre, RNC, cédula o número de cliente" value={texto} autoFocus
               onChange={(e) => setTexto(e.target.value)} />
      </div>
      {aviso && (
        <div role="status">
          <p className="ok-texto">{aviso.texto}</p>
          {aviso.avisos.map((a) => <p key={a} className="alerta">{a}</p>)}
        </div>
      )}
      {error && <p className="error">{error}</p>}
      <p className="tenue">
        {buscando ? 'Buscando...' : texto
          ? `${p.total} encontrados${p.total > p.mostrados ? ` (se muestran ${p.mostrados}: escriba más para afinar)` : ''}`
          : `Los ${p.mostrados} más recientes de ${p.total}.`}
      </p>

      <div className="tabla-marco">
        <table className="tabla catalogo">
          <thead>
            <tr><th className="num">No.</th><th>Nombre</th><th>RNC / cédula</th><th>Teléfono</th><th>Comprobante</th><th></th></tr>
          </thead>
          <tbody>
            {p.clientes.map((c) => (
              <tr key={c.numero} className={`fila-editable${c.activo ? '' : ' apagada'}`} tabIndex={0}
                  onClick={() => abrir(c)} onKeyDown={(e) => e.key === 'Enter' && abrir(c)}>
                <td className="num">{c.numero}</td>
                <td><strong>{c.nombre}</strong></td>
                <td>{c.rnc}</td>
                <td>{c.telefono}</td>
                <td>{c.tipoNombre}</td>
                <td>{c.activo ? '' : 'Inactivo'}</td>
              </tr>
            ))}
            {p.clientes.length === 0 && <tr><td colSpan={6} className="tenue">Ningún cliente con esa búsqueda.</td></tr>}
          </tbody>
        </table>
      </div>

      {editando && (
        <Editor e={editando} comprobantes={datos.comprobantes} puedeEditar={datos.puedeEditar}
                alCerrar={() => setEditando(null)} alGuardar={alGuardar} alError={manejar} />
      )}
    </section>
  )
}

const VACIO: DatosCliente = {
  nombre: '', rnc: '', cedula: '', pasaporte: '', direccion: '', sector: '', ciudad: '', telefono: '', contacto: '',
  correo: '', tipoComprobante: 2, itbis: true, propina: true, limiteCredito: 0, condiciones: '30 Dias',
  extranjero: false, activo: true,
}

function Editor({ e, comprobantes, puedeEditar, alCerrar, alGuardar, alError }: {
  e: Edicion
  comprobantes: TipoComprobante[]
  puedeEditar: boolean
  alCerrar: () => void
  alGuardar: (c: Cliente, avisos: string[], nuevo: boolean) => void
  alError: (x: unknown) => void
}) {
  const c = e.nuevo ? null : e.c
  const [d, setD] = useState<DatosCliente>(() => (c ? { ...VACIO, ...limpio(c) } : VACIO))
  const [limite, setLimite] = useState(String(c?.limiteCredito ?? 0))
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)

  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])

  const cambiar = <K extends keyof DatosCliente>(k: K, v: DatosCliente[K]) => setD((x) => ({ ...x, [k]: v }))
  const tipo = comprobantes.find((t) => t.id === d.tipoComprobante)
  const soloVer = !puedeEditar

  async function enviar(ev: FormEvent) {
    ev.preventDefault()
    if (soloVer) return
    setGuardando(true)
    setError('')
    try {
      const datos = { ...d, limiteCredito: Number(limite.replace(/,/g, '')) || 0 }
      const r = e.nuevo ? await crearCliente(datos) : await modificarCliente(c!.numero, datos)
      alGuardar(r.cliente, r.avisos, e.nuevo)
    } catch (x) {
      if (x instanceof SesionVencida) alError(x)
      else setError(x instanceof Error ? x.message : String(x))
    } finally {
      setGuardando(false)
    }
  }

  const campo = (k: keyof DatosCliente, etiqueta: string, largo: number, extra?: object) => (
    <label>
      {etiqueta}
      <input maxLength={largo} value={String(d[k] ?? '')} disabled={soloVer}
             onChange={(x) => cambiar(k, x.target.value as never)} {...extra} />
    </label>
  )

  return (
    <Fondo centro>
      <form className="dialogo cliente" role="dialog" aria-modal="true" aria-label="Cliente"
            onSubmit={enviar}>
        <h2>{e.nuevo ? 'Nuevo cliente' : `Cliente ${c!.numero}`}</h2>

        {campo('nombre', 'Nombre o razón social', 60, { autoFocus: true })}
        <div className="dos">
          <label>
            Tipo de comprobante
            <select value={d.tipoComprobante} disabled={soloVer} onChange={(x) => cambiar('tipoComprobante', Number(x.target.value))}>
              {comprobantes.map((t) => <option key={t.id} value={t.id}>{t.nombre}</option>)}
              {!tipo && <option value={d.tipoComprobante}>Tipo {d.tipoComprobante} (el que tiene)</option>}
            </select>
          </label>
          {campo('rnc', tipo?.pideRnc ? 'RNC o cédula (obligatorio)' : 'RNC o cédula', 13, { inputMode: 'numeric' })}
        </div>
        <div className="dos">
          {campo('cedula', 'Cédula', 13)}
          {campo('pasaporte', 'Pasaporte', 13)}
        </div>
        <div className="dos">
          {campo('telefono', 'Teléfono', 15)}
          {campo('correo', 'Correo', 50, { type: 'email' })}
        </div>
        <div className="dos">
          {campo('contacto', 'Contacto', 30)}
          {campo('direccion', 'Dirección', 50)}
        </div>
        <div className="dos">
          {campo('sector', 'Sector', 30)}
          {campo('ciudad', 'Ciudad', 15)}
        </div>
        <div className="dos">
          <label>
            Límite de crédito
            <input inputMode="decimal" value={limite} disabled={soloVer} onChange={(x) => setLimite(x.target.value)} />
          </label>
          {campo('condiciones', 'Condiciones', 15)}
        </div>
        <div className="marcas-form">
          <label className="check"><input type="checkbox" checked={d.itbis} disabled={soloVer} onChange={(x) => cambiar('itbis', x.target.checked)} /> Lleva ITBIS</label>
          <label className="check"><input type="checkbox" checked={d.propina} disabled={soloVer} onChange={(x) => cambiar('propina', x.target.checked)} /> Lleva propina</label>
          <label className="check"><input type="checkbox" checked={d.extranjero} disabled={soloVer} onChange={(x) => cambiar('extranjero', x.target.checked)} /> Extranjero</label>
          <label className="check"><input type="checkbox" checked={d.activo} disabled={soloVer} onChange={(x) => cambiar('activo', x.target.checked)} /> Activo</label>
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>{soloVer ? 'Cerrar' : 'Cancelar'}</button>
          {!soloVer && <button type="submit" disabled={guardando || d.nombre.trim() === ''}>{guardando ? 'Guardando...' : 'Guardar'}</button>}
        </div>
      </form>
    </Fondo>
  )
}

/** Los campos de un cliente para el formulario (sin los null, que los inputs no aceptan). */
function limpio(c: Cliente): DatosCliente {
  return {
    nombre: c.nombre, rnc: c.rnc ?? '', cedula: c.cedula ?? '', pasaporte: c.pasaporte ?? '', direccion: c.direccion ?? '',
    sector: c.sector ?? '', ciudad: c.ciudad ?? '', telefono: c.telefono ?? '', contacto: c.contacto ?? '',
    correo: c.correo ?? '', tipoComprobante: c.tipoComprobante, itbis: c.itbis, propina: c.propina,
    limiteCredito: c.limiteCredito, condiciones: c.condiciones ?? '', extranjero: c.extranjero, activo: c.activo,
  }
}
