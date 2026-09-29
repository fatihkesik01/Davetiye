import { Component, type ErrorInfo, type ReactNode } from 'react'

import { ErrorState } from './ErrorState'

type AppErrorBoundaryProps = {
  children: ReactNode
  fallback?: ReactNode
  onError?: (error: Error, info: ErrorInfo) => void
}

type AppErrorBoundaryState = {
  hasError: boolean
}

export class AppErrorBoundary extends Component<
  AppErrorBoundaryProps,
  AppErrorBoundaryState
> {
  override state: AppErrorBoundaryState = { hasError: false }

  static getDerivedStateFromError(): AppErrorBoundaryState {
    return { hasError: true }
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    this.props.onError?.(error, info)
  }

  override render() {
    if (this.state.hasError) {
      return this.props.fallback ?? <ErrorState />
    }

    return this.props.children
  }
}
