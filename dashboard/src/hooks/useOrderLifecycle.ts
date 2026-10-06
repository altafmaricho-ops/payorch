import { useEffect, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useAuth } from '../context/AuthContext';
import type { OrderStatusEvent } from '../types';

const HUB_URL = import.meta.env.VITE_HUB_URL;

if (!HUB_URL) {
  throw new Error('VITE_HUB_URL is not configured.');
}

const MAX_FEED_LENGTH = 100;

export function useOrderLifecycle() {
  const { token } = useAuth();

  const [events, setEvents] = useState<OrderStatusEvent[]>([]);
  const [connected, setConnected] = useState(false);

  useEffect(() => {
    if (!token) {
      setConnected(false);
      return;
    }

    let disposed = false;
    let startCompleted = false;

    const connection =
      new signalR.HubConnectionBuilder()
        .withUrl(HUB_URL, {
          accessTokenFactory: () => token
        })
        .withAutomaticReconnect([
          0,
          2000,
          5000,
          10000,
          30000
        ])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    connection.on(
      'orderStatusChanged',
      (payload: OrderStatusEvent) => {
        if (disposed) {
          return;
        }

        setEvents((previous) =>
          [payload, ...previous].slice(0, MAX_FEED_LENGTH)
        );
      }
    );

    connection.onreconnecting(() => {
      if (!disposed) {
        setConnected(false);
      }
    });

    connection.onreconnected(() => {
      if (!disposed) {
        setConnected(true);
      }
    });

    connection.onclose(() => {
      if (!disposed) {
        setConnected(false);
      }
    });

    const startConnection = async () => {
      try {
        await connection.start();

        startCompleted = true;

        if (disposed) {
          await connection.stop().catch(() => undefined);
          return;
        }

        setConnected(true);
      } catch (error) {
        startCompleted = true;

        if (!disposed) {
          setConnected(false);
          console.warn(
            'SignalR connection unavailable:',
            error
          );
        }
      }
    };

    void startConnection();

    return () => {
      disposed = true;
      setConnected(false);

      /*
       * Important:
       *
       * React StrictMode can execute an effect cleanup while
       * SignalR is still negotiating. Calling stop() at that
       * exact moment produces:
       *
       * "The connection was stopped during negotiation."
       *
       * Wait until start() has completed before stopping.
       */
      if (startCompleted) {
        void connection.stop().catch(() => undefined);
      }
    };
  }, [token]);

  return {
    events,
    connected
  };
}