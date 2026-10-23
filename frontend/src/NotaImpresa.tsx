import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { dinero, fecha, notaImpresa, sesionGuardada, SesionVencida, type NotaImpresa as Datos } from './api'

const CODIGOS: Record<number, string> = { 1: 'Anula el NCF modificado', 2: 'Corrige texto del NCF modificado', 3: 'Corrige montos del NCF modificado' }

/**
 * La nota de credito electronica (E34) para imprimir (carta, como la factura), con lo que pide la DGII: e-NCF, la
 * factura que modifica, codigo de seguridad, fecha de la firma y el QR. Se abre en otra pestana: /nota/{origen}/{numero}.
 */
export default function NotaImpresa({ origen, numero }: { origen: string; numero: number }) {
  const [n, setN] = useState<Datos | null>(null)
  const [qr, setQr] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir la nota.')
      return
    }
    notaImpresa(origen, numero).then(setN).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir la nota.' : e instanceof Error ? e.message : String(e)))
  }, [origen, numero])

  useEffect(() => {
    if (n?.timbre.url) QRCode.toDataURL(n.timbre.url, { margin: 2, width: 220, errorCorrectionLevel: 'M' }).then(setQr)
    if (n) document.title = `Nota de crédito ${n.encf}`
  }, [n])

  if (error) return <main className="factura-hoja"><p className="error">{error}</p></main>
  if (!n) return <main className="factura-hoja"><p className="tenue">Cargando la nota...</p></main>
  const d = n.desglose

  return (
    <main className="factura-hoja">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>
      <header className="factura-cabeza">
        <div>
          <h1>{n.emisor.razonSocial}</h1>
          <p>RNC {n.emisor.rnc}</p>
          <p>{n.emisor.direccion}</p>
          {n.emisor.telefono && <p>Tel. {n.emisor.telefono}</p>}
        </div>
        <div className="factura-ncf">
          <h2>Nota de crédito electrónica</h2>
          <p><strong>e-NCF: {n.encf}</strong></p>
          <p>Nota #{n.numero} · {fecha(n.fecha, true)}</p>
        </div>
      </header>

      <section className="factura-cliente">
        <p><strong>Cliente:</strong> {n.cliente}{n.rnc ? ` · RNC/Cédula ${n.rnc}` : ''}</p>
        <p><strong>Modifica:</strong> {n.ncfModificado} del {fecha(n.fechaModificado, true)}</p>
        <p><strong>Motivo:</strong> {CODIGOS[n.codigo] ?? `Código ${n.codigo}`}{n.razon ? ` · ${n.razon}` : ''}</p>
        {n.sinItbis && <p>Emitida después de 30 días de la factura: no rebaja el ITBIS.</p>}
      </section>

      <table className="factura-tabla">
        <thead><tr><th>Descripción</th><th className="num">Importe</th></tr></thead>
        <tbody><tr><td>NOTA DE CRÉDITO A {n.ncfModificado}</td><td className="num">{dinero(d.gravado + d.exento)}</td></tr></tbody>
      </table>

      <div className="factura-pie">
        <div className="factura-timbre">
          {qr && <img src={qr} alt="Código QR de consulta en la DGII" width={150} height={150} />}
          {n.timbre.codigoSeguridad
            ? <><p>Código de seguridad: <strong>{n.timbre.codigoSeguridad}</strong></p><p>Fecha de firma digital: {n.timbre.fechaHoraFirma}</p></>
            : <p className="error no-imprimir">La nota no aparece firmada en la DGII: se imprime sin QR.</p>}
        </div>
        <dl className="factura-totales">
          <dt>{d.gravado > 0 ? 'Gravado' : 'Exento'}</dt><dd>{dinero(d.gravado + d.exento)}</dd>
          {d.itbis !== 0 && <><dt>ITBIS 18 %</dt><dd>{dinero(d.itbis)}</dd></>}
          {d.propina !== 0 && <><dt>Propina 10 %</dt><dd>{dinero(d.propina)}</dd></>}
          <dt className="grande">Total acreditado</dt><dd className="grande">{dinero(d.monto)}</dd>
        </dl>
      </div>
    </main>
  )
}
