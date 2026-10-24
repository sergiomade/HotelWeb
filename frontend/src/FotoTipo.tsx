import { urlFotoTipo, type FotoTipo as Foto } from './api'

/**
 * La foto del tipo de habitacion, por su numero o por su nombre (el rack trae solo el nombre). Sin foto, nada.
 * Son ilustrativas: no siempre es la habitacion exacta. Las fotos vienen de useFotosTipos.
 */
export default function FotoTipo({ tipo, nombre, className = 'foto-tipo', fotos }: {
  tipo?: number
  nombre?: string | null
  className?: string
  fotos: Foto[]
}) {
  const f = fotos.find((x) => (tipo != null ? x.tipo === tipo : nombre != null && x.descripcion === nombre.trim()))
  if (!f) return null
  return <img className={className} src={urlFotoTipo(f)} alt={`Habitación ${f.descripcion.toLowerCase()} (foto ilustrativa)`} loading="lazy" />
}
