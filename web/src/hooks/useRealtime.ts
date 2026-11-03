import { useEffect, useRef, useState } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { realtimeUrl } from '../api/client'
import { getSession } from '../api/session'

export function useRealtime(onEvent: () => void) {
  const callbackRef = useRef(onEvent)
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    callbackRef.current = onEvent
  }, [onEvent])

  useEffect(() => {
    const session = getSession()
    if (!session) return

    let active = true
    let timer: number | undefined
    const connection = new HubConnectionBuilder()
      .withUrl(realtimeUrl(), {
        accessTokenFactory: () => getSession()?.token ?? '',
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    const notify = () => {
      window.clearTimeout(timer)
      timer = window.setTimeout(() => callbackRef.current(), 250)
    }

    for (const eventName of [
      'agent.changed',
      'agents.changed',
      'telemetry.received',
      'alerts.changed',
      'deployment.changed',
    ]) {
      connection.on(eventName, notify)
    }

    connection.onreconnected(() => {
      if (active) {
        setConnected(true)
        notify()
      }
    })
    connection.onreconnecting(() => {
      if (active) setConnected(false)
    })
    connection.onclose(() => {
      if (active) setConnected(false)
    })

    connection
      .start()
      .then(() => {
        if (active) setConnected(true)
      })
      .catch(() => {
        if (active) setConnected(false)
      })

    return () => {
      active = false
      window.clearTimeout(timer)
      void connection.stop()
    }
  }, [])

  return connected
}
