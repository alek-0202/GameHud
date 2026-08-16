import { Navigate, useNavigate, useParams } from 'react-router-dom'
import { ContainerDetails } from '../components/ContainerDetails'

export function ContainerDetailsPage() {
  const navigate = useNavigate()
  const { containerId } = useParams()

  if (!containerId) {
    return <Navigate replace to="/containers" />
  }

  return (
    <ContainerDetails
      containerId={containerId}
      onBack={() => {
        navigate('/containers')
      }}
    />
  )
}
