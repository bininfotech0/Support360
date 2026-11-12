import { Link } from 'react-router-dom'
import { EmptyState, PageHeader } from '../components/ui'

export function NotFoundPage() {
  return (
    <>
      <PageHeader eyebrow="Navigation" title="Page not found" />
      <EmptyState title="That route does not exist" detail="Return to the operations overview." />
      <Link className="button button-primary" to="/">
        Return to overview
      </Link>
    </>
  )
}
