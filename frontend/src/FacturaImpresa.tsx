import { useEffect, useState } from 'react'
import QRCode from 'qrcode'
import { dinero, fecha, obtenerFacturaImpresa, sesionGuardada, SesionVencida, type FacturaImpresa as Datos } from './api'

/**
 * La factura del check-out para imprimir (carta, como Delphi) con lo que pide la DGII en la representacion impresa:
 * e-NCF, vencimiento de la secuencia (no en E32), codigo de seguridad, fecha de la firma y el QR de consulta.
 * Se abre en otra pestana: /factura/{numero}.
 */
export default function FacturaImpresa({ numero }: { numero: number }) {
  const [f, setF] = useState<Datos | null>(null)
  const [qr, setQr] = useState('')
  const [error, setError] = useState('')

  useEffect(() => {
    if (!sesionGuardada()) {
      setError('Entre al sistema en otra pestaña y vuelva a abrir la factura.')
      return
    }
    obtenerFacturaImpresa(numero).then(setF).catch((e) =>
      setError(e instanceof SesionVencida ? 'La sesión venció: entre otra vez y vuelva a abrir la factura.' : e instanceof Error ? e.message : String(e)))
  }, [numero])

  useEffect(() => {
    if (f?.timbre.url) QRCode.toDataURL(f.timbre.url, { margin: 2, width: 220, errorCorrectionLevel: 'M' }).then(setQr)
    if (f) document.title = `Factura ${f.encf ?? f.numero}`
  }, [f])

  if (error) return <main className="factura-hoja"><p className="error">{error}</p></main>
  if (!f) return <main className="factura-hoja"><p className="tenue">Cargando la factura...</p></main>

  const pagos = f.otros.filter((o) => o.importe < 0)
  const payOut = f.otros.filter((o) => o.importe > 0)
  const totalPagos = pagos.reduce((s, x) => s + x.importe, 0)
  const totalPayOut = payOut.reduce((s, x) => s + x.importe, 0)

  return (
    <main className="factura-hoja">
      <div className="no-imprimir botones">
        <button type="button" onClick={() => window.print()}>Imprimir</button>
        <button type="button" className="secundario" onClick={() => window.close()}>Cerrar</button>
      </div>

      <header className="factura-cabeza">
        <div>
          <h1>{f.emisor.razonSocial}</h1>
          <p>RNC {f.emisor.rnc}</p>
          <p>{f.emisor.direccion}</p>
          {f.emisor.telefono && <p>Tel. {f.emisor.telefono}</p>}
        </div>
        <div className="factura-ncf">
          <h2>{f.tipoNombre}</h2>
          {f.encf && <p><strong>e-NCF: {f.encf}</strong></p>}
          {f.vencimiento && <p>Fecha de vencimiento: {fecha(f.vencimiento, true)}</p>}
          <p>Factura #{f.numero} · {fecha(f.fecha, true)}</p>
        </div>
      </header>

      <section className="factura-cliente">
        <p><strong>Cliente:</strong> {f.cliente}{f.rnc ? ` · RNC/Cédula ${f.rnc}` : ''}</p>
        {f.huesped && f.huesped !== f.cliente && <p><strong>Huésped:</strong> {f.huesped}</p>}
        <p><strong>Habitación:</strong> {f.habitacion}{f.entrada ? ` · ${fecha(f.entrada, true)} al ${fecha(f.salida, true)}` : ''}</p>
      </section>

      <table className="factura-tabla">
        <thead><tr><th>Descripción</th><th className="num">Cant.</th><th className="num">Precio</th><th className="num">Importe</th></tr></thead>
        <tbody>
          {f.items.map((l, i) => (
            <tr key={i}><td>{l.descripcion}</td><td className="num">{l.cantidad}</td><td className="num">{dinero(l.precio)}</td><td className="num">{dinero(l.importe)}</td></tr>
          ))}
        </tbody>
      </table>

      <div className="factura-pie">
        <div className="factura-timbre">
          {qr && <img src={qr} alt="Código QR de consulta en la DGII" width={150} height={150} />}
          {f.timbre.codigoSeguridad
            ? <><p>Código de seguridad: <strong>{f.timbre.codigoSeguridad}</strong></p><p>Fecha de firma digital: {f.timbre.fechaHoraFirma}</p></>
            : f.encf && <p className="error no-imprimir">El e-CF no aparece firmado en la DGII: se imprime sin QR.</p>}
        </div>
        <dl className="factura-totales">
          <dt>Subtotal</dt><dd>{dinero(f.subTotal)}</dd>
          {f.itbis !== 0 && <><dt>ITBIS 18 %</dt><dd>{dinero(f.itbis)}</dd></>}
          {f.propina !== 0 && <><dt>Propina 10 %</dt><dd>{dinero(f.propina)}</dd></>}
          <dt className="grande">Total</dt><dd className="grande">{dinero(f.total)}</dd>
          {totalPayOut !== 0 && <><dt>PAY-OUT (sin impuestos)</dt><dd>{dinero(totalPayOut)}</dd></>}
          {totalPagos !== 0 && <><dt>Pagos y adelantos</dt><dd>{dinero(totalPagos)}</dd></>}
        </dl>
      </div>

      {pagos.length > 0 && (
        <section className="factura-pagos">
          <h3>Pagos</h3>
          <ul>{pagos.map((p, i) => <li key={i}>{fecha(p.fecha, true)} · {p.descripcion} · {dinero(p.importe)}</li>)}</ul>
        </section>
      )}
    </main>
  )
}
