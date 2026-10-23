import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { dinero, fecha, sesionGuardada, SesionVencida, ticketGasto, type TicketGasto as Datos } from './api'

/**
 * El gasto menor impreso (rollo de 80 mm, como ImprimirGastoMenor de Delphi): emisor, e-NCF, el gasto, a quien se pago y por
 * que, las firmas de quien recibe y quien autoriza, y el QR de la DGII. Se abre en otra pestana: /gasto/{numero}.
 */
export default function TicketGasto({ numero }: { numero: number }) {
  const [t, setT] = useState<Datos | null>(null)
  const [qr, setQr] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir el gasto.')
      return
    }
    ticketGasto(numero).then(setT).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrirlo.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  useEffect(() => {
    if (t?.timbre.url) QRCode.toDataURL(t.timbre.url, { margin: 1, width: 200, errorCorrectionLevel: 'M' }).then(setQr)
    if (t) document.title = `Gasto menor ${t.gasto.encf}`
  }, [t])

  if (error) return <main className="ticket"><p className="error">{error}</p></main>
  if (!t) return <main className="ticket"><p>Cargando el gasto...</p></main>
  const g = t.gasto

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
        <strong>Comprobante electrónico para gastos menores</strong>
        <p><strong>e-NCF: {g.encf}</strong></p>
        {t.vencimiento && <p>Fecha de vencimiento: {fecha(t.vencimiento, true)}</p>}
      </div>
      <p>Gasto #{g.numero} · {fecha(g.fecha, true)}</p>
      <hr />
      <p><strong>{g.descripcion}</strong> ({g.bienServicio === 2 ? 'servicio' : 'bien'})</p>
      <table className="ticket-tabla">
        <tbody>
          <tr><td>{g.cantidad} x {dinero(g.precio)}</td><td className="num">{dinero(g.total)}</td></tr>
          <tr className="ticket-total"><td>TOTAL (exento)</td><td className="num">{dinero(g.total)}</td></tr>
        </tbody>
      </table>
      <hr />
      <p>Pagado a: {g.beneficiario}</p>
      {g.cedula && <p>Cédula: {g.cedula}</p>}
      <p>Pago: {t.formaPago}</p>
      <p>Gasto: {t.tipoGasto}</p>
      <p>Detalle: {g.detalle}</p>
      <hr />
      <p className="ticket-firma">Recibido por: ______________________</p>
      <p className="ticket-firma">Autorizado: ________________________</p>
      {g.usuario && <p>Registrado por: {g.usuario}</p>}
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
