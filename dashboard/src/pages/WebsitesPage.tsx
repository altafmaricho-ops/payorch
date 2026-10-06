import { useEffect, useState, type ReactNode } from 'react';
import {
  changeWebsiteStatus,
  createWebsite,
  getUsers,
  getWebsites,
  rotateWebsiteSecrets
} from '../api/platform';
import { useAuth } from '../context/AuthContext';
import type { PlatformUser, Website } from '../types';

export function WebsitesPage() {
  const { user } = useAuth();

  const [rows, setRows] = useState<Website[]>([]);
  const [users, setUsers] = useState<PlatformUser[]>([]);
  const [open, setOpen] = useState(false);
  const [credentials, setCredentials] = useState<any>(null);
  const [error, setError] = useState('');

  const [form, setForm] = useState({
    merchantCode: '',
    displayName: '',
    vertical: 'ecommerce',
    callbackUrl: '',
    commissionPercent: 0,
    subAdminId: ''
  });

  const canManage =
    user?.role === 'SuperAdmin' || user?.role === 'Admin';

  const canApprove = canManage;

  async function load() {
    try {
      setError('');

      const [websites, platformUsers] = await Promise.all([
        getWebsites(),
        getUsers()
      ]);

      setRows(websites);
      setUsers(platformUsers);
    } catch (e: any) {
      setError(
        e?.response?.data?.error ??
          'Unable to load websites.'
      );
    }
  }

  useEffect(() => {
    void load();
  }, []);

  /*
   * Show every SubAdmin so the administrator can see
   * whether the SubAdmin is Active, PendingApproval,
   * Suspended or Blocked.
   *
   * Only Active SubAdmins can actually be selected.
   */
  const subAdmins = users.filter(
    x => x.role === 'SubAdmin'
  );

  const activeSubAdmins = subAdmins.filter(
    x => x.status === 'Active'
  );

  async function save() {
    if (!form.subAdminId) {
      setError('Please select an active SubAdmin owner.');
      return;
    }

    const selectedSubAdmin = activeSubAdmins.find(
      x => x.id === form.subAdminId
    );

    if (!selectedSubAdmin) {
      setError(
        'The selected SubAdmin is not active. Please approve or activate the SubAdmin first.'
      );
      return;
    }

    try {
      setError('');

      const data = await createWebsite({
        ...form,
        commissionPercent: Number(form.commissionPercent),
        subAdminId: form.subAdminId
      });

      setOpen(false);
      setCredentials(data);

      setForm({
        merchantCode: '',
        displayName: '',
        vertical: 'ecommerce',
        callbackUrl: '',
        commissionPercent: 0,
        subAdminId: ''
      });

      await load();
    } catch (e: any) {
      setError(
        e?.response?.data?.error ??
          'Unable to create website.'
      );
    }
  }

  async function rotate(id: string) {
    if (
      !window.confirm(
        'Rotate API and callback secrets for this website? Existing credentials will stop working.'
      )
    ) {
      return;
    }

    try {
      setError('');
      setCredentials(await rotateWebsiteSecrets(id));
    } catch (e: any) {
      setError(
        e?.response?.data?.error ??
          'Unable to rotate website secrets.'
      );
    }
  }

  async function act(id: string, action: string) {
    const reason =
      window.prompt(
        `Reason for ${action} (optional):`
      ) ?? '';

    try {
      setError('');

      await changeWebsiteStatus(
        id,
        action,
        reason
      );

      await load();
    } catch (e: any) {
      setError(
        e?.response?.data?.error ??
          `Unable to ${action} website.`
      );
    }
  }

  function getSubAdminStatusLabel(status: string) {
    switch (status) {
      case 'Active':
        return 'Active';

      case 'PendingApproval':
        return 'Pending approval';

      case 'Suspended':
        return 'Suspended';

      case 'Blocked':
        return 'Blocked';

      case 'Rejected':
        return 'Rejected';

      default:
        return status;
    }
  }

  return (
    <>
      <div className="hero">
        <div>
          <h2>Websites</h2>
          <p>
            Merchant sites connected to the payment control plane.
          </p>
        </div>

        {canManage && (
          <button
            className="btn"
            onClick={() => {
              setError('');
              setOpen(true);
            }}
          >
            + Connect website
          </button>
        )}
      </div>

      {error && (
        <div className="notice">
          {error}
        </div>
      )}

      <div className="card section">
        <div className="toolbar">
          <span className="muted">
            {rows.length} websites in scope
          </span>

          <button
            className="btn secondary"
            onClick={() => void load()}
          >
            Refresh
          </button>
        </div>

        <table className="table">
          <thead>
            <tr>
              <th>Website</th>
              <th>Vertical</th>
              <th>Owner</th>
              <th>Commission</th>
              <th>API</th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </thead>

          <tbody>
            {rows.map(w => (
              <tr key={w.id}>
                <td>
                  <b>{w.displayName}</b>
                  <div className="small">
                    {w.merchantCode}
                  </div>
                </td>

                <td>{w.vertical}</td>

                <td>
                  {
                    users.find(
                      x => x.id === w.subAdminId
                    )?.displayName ??
                      'Assigned user'
                  }
                </td>

                <td>
                  {w.commissionPercent}%
                </td>

                <td className="mono">
                  {w.apiKey
                    ? w.apiKey.slice(0, 18) + '…'
                    : 'Hidden'}
                </td>

                <td>
                  <span
                    className={`status ${
                      w.status === 'Active'
                        ? 'ok'
                        : w.status === 'Blocked' ||
                            w.status === 'Rejected'
                          ? 'bad'
                          : 'wait'
                    }`}
                  >
                    {w.status}
                  </span>
                </td>

                <td>
                  {canApprove && (
                    <div
                      style={{
                        display: 'flex',
                        gap: 5,
                        flexWrap: 'wrap'
                      }}
                    >
                      <Actions
                        status={w.status}
                        onAction={a =>
                          void act(w.id, a)
                        }
                      />

                      <button
                        className="btn secondary"
                        onClick={() =>
                          void rotate(w.id)
                        }
                      >
                        Rotate secrets
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {!rows.length && (
          <div className="empty">
            No websites connected yet.
          </div>
        )}
      </div>

      {open && (
        <div className="modal-backdrop">
          <div className="modal">
            <h2>Connect a website</h2>

            <div className="form-grid">
              <F l="Merchant code">
                <input
                  className="input"
                  value={form.merchantCode}
                  onChange={e =>
                    setForm({
                      ...form,
                      merchantCode: e.target.value
                    })
                  }
                />
              </F>

              <F l="Display name">
                <input
                  className="input"
                  value={form.displayName}
                  onChange={e =>
                    setForm({
                      ...form,
                      displayName: e.target.value
                    })
                  }
                />
              </F>

              <F l="Vertical">
                <select
                  className="select"
                  value={form.vertical}
                  onChange={e =>
                    setForm({
                      ...form,
                      vertical: e.target.value
                    })
                  }
                >
                  <option>ecommerce</option>
                  <option>marketplace</option>
                  <option>delivery</option>
                  <option>education</option>
                  <option>subscription</option>
                  <option>restaurant</option>
                  <option>other</option>
                </select>
              </F>

              <F l="SubAdmin owner">
                <select
                  className="select"
                  value={form.subAdminId}
                  onChange={e =>
                    setForm({
                      ...form,
                      subAdminId: e.target.value
                    })
                  }
                >
                  <option value="">
                    Select active SubAdmin
                  </option>

                  {subAdmins.map(u => {
                    const isActive =
                      u.status === 'Active';

                    return (
                      <option
                        key={u.id}
                        value={u.id}
                        disabled={!isActive}
                      >
                        {u.displayName} · {u.email} ·{' '}
                        {getSubAdminStatusLabel(
                          u.status
                        )}
                      </option>
                    );
                  })}
                </select>
              </F>

              <F l="Callback URL">
                <input
                  className="input"
                  type="url"
                  value={form.callbackUrl}
                  onChange={e =>
                    setForm({
                      ...form,
                      callbackUrl: e.target.value
                    })
                  }
                />
              </F>

              <F l="Commission %">
                <input
                  className="input"
                  type="number"
                  min="0"
                  max="100"
                  value={form.commissionPercent}
                  onChange={e =>
                    setForm({
                      ...form,
                      commissionPercent:
                        Number(e.target.value)
                    })
                  }
                />
              </F>
            </div>

            {subAdmins.length === 0 ? (
              <div className="notice">
                No SubAdmin is available in your scope.
                Create a SubAdmin first.
              </div>
            ) : activeSubAdmins.length === 0 ? (
              <div className="notice">
                SubAdmin(s) exist, but none are active.
                Approve or activate a SubAdmin before
                assigning a website.
              </div>
            ) : (
              <div className="notice">
                Only active SubAdmins can own a website.
                Pending, suspended and blocked SubAdmins
                cannot be selected.
              </div>
            )}

            <div className="modal-actions">
              <button
                className="btn secondary"
                onClick={() => setOpen(false)}
              >
                Cancel
              </button>

              <button
                className="btn"
                onClick={() => void save()}
                disabled={activeSubAdmins.length === 0}
              >
                Create website
              </button>
            </div>
          </div>
        </div>
      )}

      {credentials && (
        <div className="modal-backdrop">
          <div className="modal">
            <h2>Website credentials</h2>

            <div className="notice">
              Save these values now. The API secret and
              callback secret will not be shown again.
            </div>

            <div className="detail-grid">
              <Row
                l="Merchant code"
                v={credentials.merchantCode}
              />

              <Row
                l="API key"
                v={credentials.apiKey}
              />

              <Row
                l="API secret"
                v={credentials.apiSecret}
              />

              <Row
                l="Callback secret"
                v={credentials.callbackSecret}
              />

              <Row
                l="Status"
                v={credentials.status}
              />
            </div>

            <div className="modal-actions">
              <button
                className="btn"
                onClick={() =>
                  setCredentials(null)
                }
              >
                I saved the credentials
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function Actions({
  status,
  onAction
}: {
  status: string;
  onAction: (a: string) => void;
}) {
  const list =
    status === 'PendingApproval'
      ? ['approve', 'reject']
      : status === 'Active'
        ? ['suspend', 'block']
        : status === 'Suspended' ||
            status === 'Blocked'
          ? ['activate']
          : status === 'Rejected'
            ? ['approve']
            : [];

  return (
    <div
      style={{
        display: 'flex',
        gap: 5,
        flexWrap: 'wrap'
      }}
    >
      {list.map(a => (
        <button
          key={a}
          className={`btn ${
            a === 'block' || a === 'reject'
              ? 'danger'
              : 'secondary'
          }`}
          onClick={() => onAction(a)}
        >
          {a}
        </button>
      ))}
    </div>
  );
}

function F({
  l,
  children
}: {
  l: string;
  children: ReactNode;
}) {
  return (
    <div className="field">
      <label>{l}</label>
      {children}
    </div>
  );
}

function Row({
  l,
  v
}: {
  l: string;
  v: string;
}) {
  return (
    <div className="detail-row">
      <span>{l}</span>
      <b
        className="mono"
        style={{ wordBreak: 'break-all' }}
      >
        {v}
      </b>
    </div>
  );
}