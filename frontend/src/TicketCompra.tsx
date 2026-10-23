import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { dinero, fecha, sesionGuardada, SesionVencida, ticketCompra, type TicketCompra as Datos } from './api'

/**
 * La compra a informal impresa (rollo de 80 mm): emisor, e-NCF, el proveedor, las lineas, las retenciones, el neto que se le
 * pagó, la firma de quien recibe y el QR de la DGII. Se abre en otra pestana: /compra/{numero}.
 */
export default function TicketCompra({ numero }: { numero: number }) {
  const [t, setT] = useState<Datos | null>(null)
  const [qr, setQr] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir la compra.')
      return
    }
    ticketCompra(numero).then(setT).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrirla.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  useEffect(() => {
    if (t?.timbre.url) QRCode.toDataURL(t.timbre.url, { margin: 1, width: 200, errorCorrectionLevel: 'M' }).then(setQr)
    if (t) document.title = `Compra ${t.compra.encf}`
  }, [t])

  if (error) return <main className="ticket"><p className="error">{error}</p></main>
  if (!t) return <main className="ticket"><p>Cargando la compra...</p></main>
  const c = t.compra

  return (
    <main className="ticket">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>
      <header className="ticket-centro">
        <strong>{t.emisor.razonSocial}</strong>
        <p>RNC {t.emisor.rnc}</p>
        <p>{t.emisor.direccion}</p>
      </header>
      <hr />
      <div className="ticket-centro">
        <strong>Comprobante electrónico de compras</strong>
        <p><strong>e-NCF: {c.encf}</strong></p>
        {t.vencimiento && <p>Fecha de vencimiento: {fecha(t.vencimiento, true)}</p>}
      </div>
      <p>Compra #{c.numero} · {fecha(c.fecha, true)}</p>
      <p>Proveedor: {c.proveedor}</p>
      <p>Cédula: {c.cedula}</p>
      {c.direccion && <p>{c.direccion}</p>}
      <hr />
      <table className="ticket-tabla">
        <tbody>
          {c.lineas.map((l, i) => (
            <tr key={i}>
              <td>{l.cantidad > 1 ? `${l.cantidad} x ` : ''}{l.descripcion}
                <span className="ticket-sub">{l.bienServicio === 2 ? 'servicio' : 'bien'}{l.gravado ? ' · ITBIS 18 %' : ' · exento'}{l.cantidad > 1 ? ` · ${dinero(l.precio)} c/u` : ''}</span>
              </td>
              <td className="num">{dinero(l.monto)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <hr />
      <table className="ticket-tabla">
        <tbody>
          {c.montoGravado > 0 && <tr><td>Gravado</td><td className="num">{dinero(c.montoGravado)}</td></tr>}
          {c.montoExento > 0 && <tr><td>Exento</td><td className="num">{dinero(c.montoExento)}</td></tr>}
          {c.itbis > 0 && <tr><td>ITBIS 18 %</td><td className="num">{dinero(c.itbis)}</td></tr>}
          <tr className="ticket-total"><td>TOTAL</td><td className="num">{dinero(c.total)}</td></tr>
          {c.itbisRetenido > 0 && <tr><td>ITBIS retenido</td><td className="num">-{dinero(c.itbisRetenido)}</td></tr>}
          {c.isrRetenido > 0 && <tr><td>ISR retenido</td><td className="num">-{dinero(c.isrRetenido)}</td></tr>}
          <tr className="ticket-total"><td>NETO PAGADO</td><td className="num">{dinero(c.netoPagar)}</td></tr>
        </tbody>
      </table>
      <p>Pago: {t.formaPago}{c.formaPago === '04' ? ` · vence ${fecha(c.fechaPago, true)}` : ''}</p>
      <p>Retención ISR: {t.retencionIsr}</p>
      <p>Gasto: {t.tipoGasto}</p>
      {c.observacion && <p>Observación: {c.observacion}</p>}
      <hr />
      <p className="ticket-firma">Recibido por: ______________________</p>
      {c.usuario && <p>Registrado por: {c.usuario}</p>}
      <hr />
      <div className="ticket-centro">
        {qr && <img src={qr} alt="Código QR de consulta en la DGII" width={150} height={150} />}
        {t.timbre.codigoSeguridad
          ? <><p>Código de seguridad: <strong>{t.timbre.codigoSeguridad}</strong></p><p>Fecha de firma: {t.timbre.fechaHoraFirma}</p></>
          : <p className="no-imprimir error">El e-CF no aparece firmado en la DGII: sin QR.</p>}
      </div>
    </main>
  )
}
