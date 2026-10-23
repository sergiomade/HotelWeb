import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { dinero, fecha, sesionGuardada, SesionVencida, ticketCafeteria, type TicketCafeteria as Datos } from './api'

/**
 * El ticket de la cafeteria (rollo POS de 80 mm, como ImprimirSolucionNoFiscal de Delphi) con lo que pide la DGII: e-NCF, vencimiento de la
 * secuencia, codigo de seguridad, fecha de la firma y el QR. Se abre en otra pestana: /cafeteria/{factura}.
 */
export default function TicketCafeteria({ numero }: { numero: number }) {
  const [t, setT] = useState<Datos | null>(null)
  const [qr, setQr] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir el ticket.')
      return
    }
    ticketCafeteria(numero).then(setT).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir el ticket.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  useEffect(() => {
    if (t?.timbre.url) QRCode.toDataURL(t.timbre.url, { margin: 1, width: 200, errorCorrectionLevel: 'M' }).then(setQr)
    if (t) document.title = `Cafetería ${t.encf ?? t.factura}`
  }, [t])

  if (error) return <main className="ticket"><p className="error">{error}</p></main>
  if (!t) return <main className="ticket"><p>Cargando el ticket...</p></main>

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
        {t.emisor.telefono && <p>Tel. {t.emisor.telefono}</p>}
      </header>
      <hr />
      <div className="ticket-centro">
        <strong>{t.tipoNombre}</strong>
        {t.encf && <p><strong>e-NCF: {t.encf}</strong></p>}
        {t.vencimiento && <p>Fecha de vencimiento: {fecha(t.vencimiento, true)}</p>}
      </div>
      <p>Mesa {t.mesa} · Factura #{t.factura} · {fecha(t.fecha, true)}</p>
      <p>Cliente: {t.nombre}</p>
      {t.identificacion && <p>RNC/Cédula: {t.identificacion}</p>}
      <hr />
      <table className="ticket-tabla">
        <tbody>
          {t.items.map((l, i) => (
            <tr key={i}>
              <td>{l.cantidad > 1 ? `${l.cantidad} x ` : ''}{l.descripcion}{l.cantidad > 1 && <span className="ticket-sub">{dinero(l.precio)} c/u</span>}</td>
              <td className="num">{dinero(l.importe)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <hr />
      <table className="ticket-tabla">
        <tbody>
          <tr><td>Subtotal</td><td className="num">{dinero(t.subTotal)}</td></tr>
          <tr><td>ITBIS 18 %</td><td className="num">{dinero(t.itbis)}</td></tr>
          {t.propina !== 0 && <tr><td>Propina 10 %</td><td className="num">{dinero(t.propina)}</td></tr>}
          <tr className="ticket-total"><td>TOTAL</td><td className="num">{dinero(t.total)}</td></tr>
          {t.pagos.map((p, i) => <tr key={i}><td>{p.formaPago}</td><td className="num">{dinero(p.monto)}</td></tr>)}
        </tbody>
      </table>
      {t.devuelta > 0 && <p className="ticket-centro">Devuelta: {dinero(t.devuelta)}</p>}
      <hr />
      <div className="ticket-centro">
        {qr && <img src={qr} alt="Código QR de consulta en la DGII" width={150} height={150} />}
        {t.timbre.codigoSeguridad
          ? <><p>Código de seguridad: <strong>{t.timbre.codigoSeguridad}</strong></p><p>Fecha de firma: {t.timbre.fechaHoraFirma}</p></>
          : t.encf && <p className="no-imprimir error">El e-CF no aparece firmado en la DGII: sin QR.</p>}
        {t.usuario && <p>Atendido por: {t.usuario}</p>}
        <p>¡Gracias por su visita!</p>
      </div>
    </main>
  )
}
