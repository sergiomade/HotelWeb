import { useCallback, useEffect, useState, type FormEvent } from 'react'
import Fondo from './Fondo'
import {
  activarSeguridad, agregarUsuarioExistente, crearGrupo, crearUsuario, modificarGrupo, modificarUsuario, obtenerSeguridad,
  ponerClave, SesionVencida, type GrupoSeguridad, type PantallaSeguridad, type PermisoHotel, type UsuarioSeguridad,
} from './api'

type Pestana = 'usuarios' | 'grupos'
type EdicionUsuario = { tipo: 'nuevo' } | { tipo: 'existente' } | { tipo: 'editar'; u: UsuarioSeguridad }
type EdicionGrupo = { nuevo: true } | { nuevo: false; g: GrupoSeguridad }

const mensaje = (x: unknown) => (x instanceof Error ? x.message : String(x))

/**
 * Seguridad de la web (etapa 10): usuarios, grupos y permisos del hotel, en las mismas tablas del modulo de Seguridad de
 * Delphi. Solo se ven los usuarios que estan en grupos del hotel. No se borra nada: se desactiva.
 */
export default function Seguridad({ alVencer, miId, alCambiar }: { alVencer: () => void; miId: number; alCambiar: () => void }) {
  const [datos, setDatos] = useState<PantallaSeguridad | null>(null)
  const [error, setError] = useState('')
  const [aviso, setAviso] = useState('')
  const [p, setP] = useState<Pestana>('usuarios')
  const [usuario, setUsuario] = useState<EdicionUsuario | null>(null)
  const [grupo, setGrupo] = useState<EdicionGrupo | null>(null)
  const [activando, setActivando] = useState(false)

  const manejar = useCallback(
    (e: unknown) => (e instanceof SesionVencida ? alVencer() : setError(mensaje(e))),
    [alVencer],
  )

  const cargar = useCallback(() => {
    obtenerSeguridad().then((d) => { setDatos(d); setError('') }).catch(manejar)
  }, [manejar])

  useEffect(cargar, [cargar])

  function listo(texto: string) {
    setUsuario(null)
    setGrupo(null)
    setAviso(texto)
    cargar()
    alCambiar()
  }

  async function activar() {
    setActivando(true)
    setError('')
    try {
      await activarSeguridad()
      listo('Seguridad activada. Agregue ahora a los demás usuarios del hotel a sus grupos.')
    } catch (x) {
      manejar(x)
    } finally {
      setActivando(false)
    }
  }

  if (error && !datos) return <p className="error">{error}</p>
  if (!datos) return <p className="tenue">Cargando la seguridad...</p>

  if (!datos.activada) {
    return (
      <section className="catalogos">
        <h2>Seguridad</h2>
        <div className="dialogo ancho seguridad-activar">
          <h3>Activar la seguridad de la web</h3>
          <p>
            Hoy todos los usuarios ven todas las áreas, y las acciones delicadas las hacen los usuarios de la lista de
            configuración. Al activar, se agrega a la base Seguridad de Delphi el módulo <strong>HOTEL WEB</strong> con sus
            permisos y dos grupos:
          </p>
          <ul>
            <li><strong>ADMIN HOTEL</strong>: todos los permisos. Entran usted y los que hoy editan catálogos.</li>
            <li><strong>RECEPCION</strong>: Hospedaje, Reservas, Recepción, Walk-in y Cafetería. Entran los usuarios activos del grupo HOTEL de Delphi.</li>
          </ul>
          <p className="alerta">
            Después de activar, los usuarios que no estén en un grupo del hotel solo verán el Tablero. Agréguelos enseguida
            con «Agregar usuario existente». No se toca nada de los otros programas que usan la base Seguridad.
          </p>
          {error && <p className="error" role="alert">{error}</p>}
          <div className="botones">
            <button type="button" onClick={activar} disabled={activando}>{activando ? 'Activando...' : 'Activar la seguridad'}</button>
          </div>
        </div>
      </section>
    )
  }

  const nombreGrupo = (id: number) => datos.grupos.find((g) => g.id === id)?.descripcion ?? `#${id}`
  // "Recepción, Catálogos (1 de 6) · 2 acciones"
  function resumen(g: GrupoSeguridad) {
    if (g.permisos.length === datos!.permisos.length) return 'Todos'
    const areas = datos!.permisos.filter((a) => a.tipo === 'Area' && g.permisos.includes(a.codigo)).map((a) => {
      const pestanas = datos!.permisos.filter((p) => p.tipo === 'Opcion' && p.area === a.clave)
      const tiene = pestanas.filter((p) => g.permisos.includes(p.codigo)).length
      return tiene < pestanas.length ? `${a.nombre} (${tiene} de ${pestanas.length})` : a.nombre
    })
    const acciones = datos!.permisos.filter((p) => p.tipo === 'Accion' && g.permisos.includes(p.codigo)).length
    const texto = [areas.join(', '), acciones ? `${acciones} ${acciones === 1 ? 'acción' : 'acciones'}` : ''].filter(Boolean).join(' · ')
    return texto || <span className="tenue">Ninguno</span>
  }

  return (
    <section className="catalogos">
      <h2>Seguridad</h2>
      <div className="pestanas" role="tablist">
        <button type="button" role="tab" aria-selected={p === 'usuarios'} className={p === 'usuarios' ? 'activo' : ''} onClick={() => setP('usuarios')}>Usuarios</button>
        <button type="button" role="tab" aria-selected={p === 'grupos'} className={p === 'grupos' ? 'activo' : ''} onClick={() => setP('grupos')}>Grupos</button>
      </div>
      {aviso && <p className="ok-texto" role="status">{aviso}</p>}
      {error && <p className="error" role="alert">{error}</p>}

      {p === 'usuarios' && (
        <>
          <div className="rack-cabecera">
            <p className="tenue">
              Los usuarios del hotel (los que están en algún grupo del hotel). Son los mismos de Delphi: la clave sirve
              para los dos. No se borran: se desactivan.
            </p>
            <span className="botones">
              <button type="button" className="secundario" onClick={() => setUsuario({ tipo: 'existente' })}>Agregar usuario existente</button>
              <button type="button" onClick={() => setUsuario({ tipo: 'nuevo' })}>Nuevo usuario</button>
            </span>
          </div>
          <div className="tabla-marco">
            <table className="tabla catalogo">
              <thead>
                <tr><th>Usuario</th><th>Nombre</th><th>Grupos</th><th>Notas de crédito</th><th>Estado</th></tr>
              </thead>
              <tbody>
                {datos.usuarios.map((u) => (
                  <tr key={u.id} className={`fila-editable${u.activo ? '' : ' apagada'}`} tabIndex={0}
                      onClick={() => setUsuario({ tipo: 'editar', u })}
                      onKeyDown={(e) => e.key === 'Enter' && setUsuario({ tipo: 'editar', u })}>
                    <td><strong>{u.login}</strong></td>
                    <td>{u.nombre}</td>
                    <td>{u.grupos.map(nombreGrupo).join(', ')}</td>
                    <td>{u.cancelaFactura ? 'Autoriza' : ''}</td>
                    <td>{u.activo ? 'Activo' : 'Inactivo'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {p === 'grupos' && (
        <>
          <div className="rack-cabecera">
            <p className="tenue">Cada grupo da permisos; un usuario tiene la suma de los de sus grupos activos.</p>
            <button type="button" onClick={() => setGrupo({ nuevo: true })}>Nuevo grupo</button>
          </div>
          <div className="tabla-marco">
            <table className="tabla catalogo">
              <thead>
                <tr><th>Grupo</th><th>Permisos</th><th className="num">Usuarios</th><th>Estado</th></tr>
              </thead>
              <tbody>
                {datos.grupos.map((g) => (
                  <tr key={g.id} className={`fila-editable${g.activo ? '' : ' apagada'}`} tabIndex={0}
                      onClick={() => setGrupo({ nuevo: false, g })}
                      onKeyDown={(e) => e.key === 'Enter' && setGrupo({ nuevo: false, g })}>
                    <td><strong>{g.descripcion}</strong></td>
                    <td>{resumen(g)}</td>
                    <td className="num">{g.miembros}</td>
                    <td>{g.activo ? 'Activo' : 'Inactivo'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {usuario && (
        <EditorUsuario e={usuario} grupos={datos.grupos} miId={miId} alCerrar={() => setUsuario(null)}
                       alGuardar={listo} alVencer={alVencer} />
      )}
      {grupo && (
        <EditorGrupo e={grupo} permisos={datos.permisos} alCerrar={() => setGrupo(null)} alGuardar={listo} alVencer={alVencer} />
      )}
    </section>
  )
}

function useEscape(alCerrar: () => void) {
  useEffect(() => {
    const tecla = (k: KeyboardEvent) => k.key === 'Escape' && alCerrar()
    window.addEventListener('keydown', tecla)
    return () => window.removeEventListener('keydown', tecla)
  }, [alCerrar])
}

function alternar(lista: number[], id: number, si: boolean) {
  return si ? [...lista.filter((x) => x !== id), id] : lista.filter((x) => x !== id)
}

function EditorUsuario({ e, grupos, miId, alCerrar, alGuardar, alVencer }: {
  e: EdicionUsuario
  grupos: GrupoSeguridad[]
  miId: number
  alCerrar: () => void
  alGuardar: (texto: string) => void
  alVencer: () => void
}) {
  const u = e.tipo === 'editar' ? e.u : null
  const [login, setLogin] = useState(u?.login ?? '')
  const [nombre, setNombre] = useState(u?.nombre ?? '')
  const [clave, setClave] = useState('')
  const [clave2, setClave2] = useState('')
  const [activo, setActivo] = useState(u?.activo ?? true)
  const [cancelaFactura, setCancelaFactura] = useState(u?.cancelaFactura ?? false)
  const [sel, setSel] = useState<number[]>(u?.grupos ?? [])
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)
  const [claveNueva, setClaveNueva] = useState('')
  const [claveAviso, setClaveAviso] = useState('')
  useEscape(alCerrar)

  const titulo = e.tipo === 'nuevo' ? 'Nuevo usuario' : e.tipo === 'existente' ? 'Agregar usuario existente' : `Usuario ${u!.login}`
  const clavesBien = e.tipo !== 'nuevo' || (clave.length >= 4 && clave === clave2)
  const valido = (e.tipo === 'editar' || login.trim() !== '') && (e.tipo === 'existente' || nombre.trim() !== '')
    && (e.tipo === 'editar' || sel.length > 0) && clavesBien

  function fallo(x: unknown) {
    if (x instanceof SesionVencida) alVencer()
    else setError(mensaje(x))
  }

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (!valido) return
    setGuardando(true)
    setError('')
    try {
      if (e.tipo === 'nuevo') {
        await crearUsuario({ login, nombre, clave, cancelaFactura, grupos: sel })
        alGuardar(`Creado el usuario ${login.trim()}.`)
      } else if (e.tipo === 'existente') {
        await agregarUsuarioExistente(login, sel)
        alGuardar(`Agregado al hotel el usuario ${login.trim()}.`)
      } else {
        await modificarUsuario(u!.id, { nombre, activo, cancelaFactura, grupos: sel })
        alGuardar(sel.length === 0 ? `El usuario ${u!.login} ya no está en ningún grupo del hotel.` : `Guardado el usuario ${u!.login}.`)
      }
    } catch (x) {
      fallo(x)
    } finally {
      setGuardando(false)
    }
  }

  async function cambiarClave() {
    setError('')
    setClaveAviso('')
    try {
      await ponerClave(u!.id, claveNueva)
      setClaveNueva('')
      setClaveAviso('Clave cambiada. Désela al usuario; vale también para Delphi.')
    } catch (x) {
      fallo(x)
    }
  }

  return (
    <Fondo centro>
      <form className="dialogo ancho seguridad" role="dialog" aria-modal="true" aria-label={titulo} onSubmit={guardar}>
        <h2>{titulo}</h2>
        {e.tipo === 'existente' && (
          <p className="tenue">Para un usuario que ya existe en Delphi o en otro programa. Escriba su usuario (login) exacto; conserva su clave.</p>
        )}
        {e.tipo !== 'editar' && (
          <label>
            Usuario (login)
            <input maxLength={30} value={login} onChange={(x) => setLogin(x.target.value)} autoFocus autoComplete="off" />
          </label>
        )}
        {e.tipo !== 'existente' && (
          <label>
            Nombre
            <input maxLength={30} value={nombre} onChange={(x) => setNombre(x.target.value)} autoFocus={e.tipo === 'editar'} />
          </label>
        )}
        {e.tipo === 'nuevo' && (
          <div className="dos">
            <label>
              Clave (4 a 10)
              <input type="password" maxLength={10} value={clave} onChange={(x) => setClave(x.target.value)} autoComplete="new-password" />
            </label>
            <label>
              Repítala
              <input type="password" maxLength={10} value={clave2} onChange={(x) => setClave2(x.target.value)} autoComplete="new-password" />
              {clave2 !== '' && clave !== clave2 && <span className="aviso-texto mal">No coinciden.</span>}
            </label>
          </div>
        )}

        <fieldset className="casillas">
          <legend>Grupos del hotel</legend>
          {grupos.map((g) => (
            <label key={g.id} className="check">
              <input type="checkbox" checked={sel.includes(g.id)} onChange={(x) => setSel((l) => alternar(l, g.id, x.target.checked))} />
              {g.descripcion}{!g.activo && <span className="tenue"> (inactivo)</span>}
            </label>
          ))}
        </fieldset>
        {e.tipo === 'editar' && sel.length === 0 && (
          <p className="alerta">Sin grupos del hotel el usuario deja de verse aquí y no tendrá acceso a la web (sigue existiendo en Delphi).</p>
        )}

        {e.tipo !== 'existente' && (
          <label className="check">
            <input type="checkbox" checked={cancelaFactura} onChange={(x) => setCancelaFactura(x.target.checked)} />
            Autoriza notas de crédito (la misma marca de Delphi)
          </label>
        )}
        {e.tipo === 'editar' && (
          <>
            <label className="check">
              <input type="checkbox" checked={activo} disabled={u!.id === miId} onChange={(x) => setActivo(x.target.checked)} />
              Activo {u!.id === miId && <span className="tenue">(no puede desactivarse a sí mismo)</span>}
            </label>
            {!activo && <p className="alerta">Inactivo no entra ni a la web ni a los programas de Delphi.</p>}
            <div className="clave-nueva">
              <label>
                Poner clave nueva
                <input type="password" maxLength={10} value={claveNueva} onChange={(x) => setClaveNueva(x.target.value)} autoComplete="new-password" />
              </label>
              <button type="button" className="secundario" disabled={claveNueva.length < 4} onClick={cambiarClave}>Cambiar clave</button>
            </div>
            {claveAviso && <p className="ok-texto" role="status">{claveAviso}</p>}
          </>
        )}

        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
          <button type="submit" disabled={!valido || guardando}>{guardando ? 'Guardando...' : e.tipo === 'existente' ? 'Agregar' : 'Guardar'}</button>
        </div>
      </form>
    </Fondo>
  )
}

function EditorGrupo({ e, permisos, alCerrar, alGuardar, alVencer }: {
  e: EdicionGrupo
  permisos: PermisoHotel[]
  alCerrar: () => void
  alGuardar: (texto: string) => void
  alVencer: () => void
}) {
  const g = e.nuevo ? null : e.g
  const [descripcion, setDescripcion] = useState(g?.descripcion ?? '')
  const [activo, setActivo] = useState(g?.activo ?? true)
  const [sel, setSel] = useState<number[]>(g?.permisos ?? [])
  const [error, setError] = useState('')
  const [guardando, setGuardando] = useState(false)
  useEscape(alCerrar)

  async function guardar(ev: FormEvent) {
    ev.preventDefault()
    if (descripcion.trim() === '') return
    setGuardando(true)
    setError('')
    try {
      const d = { descripcion, activo, permisos: sel }
      if (e.nuevo) await crearGrupo(d)
      else await modificarGrupo(g!.id, d)
      alGuardar(`${e.nuevo ? 'Creado' : 'Guardado'} el grupo ${descripcion.trim().toUpperCase()}.`)
    } catch (x) {
      if (x instanceof SesionVencida) alVencer()
      else setError(mensaje(x))
    } finally {
      setGuardando(false)
    }
  }

  const casilla = (p: PermisoHotel, apagada = false) => (
    <label key={p.codigo} className={`check${apagada ? ' apagada' : ''}`} title={p.ayuda}>
      <input type="checkbox" checked={sel.includes(p.codigo)} onChange={(x) => setSel((l) => alternar(l, p.codigo, x.target.checked))} />
      <span>{p.nombre}{p.tipo === 'Accion' && p.ayuda && <span className="tenue fila-sub">{p.ayuda}</span>}</span>
    </label>
  )

  // al marcar un area por primera vez se marcan todas sus pestanas (luego se quitan las que no)
  function marcarArea(area: PermisoHotel, si: boolean) {
    setSel((l) => {
      if (!si) return alternar(l, area.codigo, false)
      const pestanas = permisos.filter((p) => p.tipo === 'Opcion' && p.area === area.clave).map((p) => p.codigo)
      const ninguna = !pestanas.some((c) => l.includes(c))
      return [...new Set([...l, area.codigo, ...(ninguna ? pestanas : [])])]
    })
  }

  // una caja por area: el area (entrada del menu), sus pestanas y sus acciones
  const areas = permisos.filter((p) => p.tipo === 'Area').map((a) => {
    const conArea = sel.includes(a.codigo)
    const pestanas = permisos.filter((p) => p.tipo === 'Opcion' && p.area === a.clave)
    const acciones = permisos.filter((p) => p.tipo === 'Accion' && p.area === a.clave)
    return (
      <fieldset key={a.codigo} className="casillas area-permisos">
        <legend>
          <label className="check">
            <input type="checkbox" checked={conArea} onChange={(x) => marcarArea(a, x.target.checked)} />
            <strong>{a.nombre}</strong>
          </label>
        </legend>
        {pestanas.length > 0 && (
          <div className="permisos-fila">
            <span className="tenue">Pestañas:</span>
            {pestanas.map((p) => casilla(p, !conArea))}
          </div>
        )}
        {acciones.length > 0 && (
          <div className="permisos-col">
            <span className="tenue">Puede:</span>
            {acciones.map((p) => casilla(p))}
          </div>
        )}
        {!conArea && pestanas.length > 0 && <span className="tenue fila-sub">Sin el área no ve sus pestañas.</span>}
      </fieldset>
    )
  })

  return (
    <Fondo centro>
      <form className="dialogo ancho seguridad grupo" role="dialog" aria-modal="true" aria-label="Grupo" onSubmit={guardar}>
        <h2>{e.nuevo ? 'Nuevo grupo' : `Grupo ${g!.descripcion}`}</h2>
        <label>
          Nombre
          <input maxLength={30} value={descripcion} onChange={(x) => setDescripcion(x.target.value)} autoFocus />
        </label>
        <p className="tenue">Marque las áreas del menú que ve el grupo, qué pestañas de cada una y qué puede hacer.</p>
        <div className="areas-permisos">{areas}</div>
        <fieldset className="casillas">
          <legend>Otras acciones</legend>
          {permisos.filter((p) => p.tipo === 'Accion' && !p.area).map((p) => casilla(p))}
        </fieldset>
        {!e.nuevo && (
          <label className="check">
            <input type="checkbox" checked={activo} onChange={(x) => setActivo(x.target.checked)} /> Activo
          </label>
        )}
        {error && <p className="error" role="alert">{error}</p>}
        <div className="botones">
          <button type="button" className="secundario" onClick={alCerrar}>Cancelar</button>
          <button type="submit" disabled={descripcion.trim() === '' || guardando}>{guardando ? 'Guardando...' : 'Guardar'}</button>
        </div>
      </form>
    </Fondo>
  )
}
