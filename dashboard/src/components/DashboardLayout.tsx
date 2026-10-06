import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useEffect, useMemo, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useOrderLifecycle } from '../hooks/useOrderLifecycle';
import { getAuditLogs } from '../api/platform';
import type { AuditLog, UserRole } from '../types';

type NavItem = { to: string; label: string; icon: string; roles?: UserRole[]; group: string };

const items: NavItem[] = [
  { to: '/', label: 'Overview', icon: '◈', group: 'Workspace' },
  { to: '/payments', label: 'Payments', icon: '↗', group: 'Workspace' },
  { to: '/websites', label: 'Websites', icon: '▦', group: 'Workspace' },
  { to: '/reports', label: 'Reports & Audit', icon: '▤', group: 'Workspace' },
  { to: '/providers', label: 'PSP Accounts', icon: '◉', roles: ['SuperAdmin', 'Admin'], group: 'Configuration' },
  { to: '/routing', label: 'Smart Routing', icon: '⌁', roles: ['SuperAdmin', 'Admin'], group: 'Configuration' },
  { to: '/users', label: 'Team & Access', icon: '♙', roles: ['SuperAdmin', 'Admin'], group: 'Security' },
  { to: '/fraud', label: 'Risk Control', icon: '◇', roles: ['SuperAdmin', 'Admin'], group: 'Security' },
  { to: '/sellers', label: 'Sellers', icon: '◆', group: 'Marketplace' },
];

export function DashboardLayout() {
  const { user, logout } = useAuth();
  const { connected } = useOrderLifecycle();
  const location = useLocation();
  const [accountOpen, setAccountOpen] = useState(false);
  const [activityOpen, setActivityOpen] = useState(false);
  const [activity, setActivity] = useState<AuditLog[]>([]);
  const [activityFrom, setActivityFrom] = useState('');
  const [activityTo, setActivityTo] = useState('');
  const [activityLoading, setActivityLoading] = useState(false);

  const visible = items.filter(x => !x.roles || (user && x.roles.includes(user.role)));

  async function loadActivity() {
    setActivityLoading(true);
    try {
      const rows = await getAuditLogs(500);
      setActivity(rows.filter(x => x.action === 'login' || x.action === 'logout'));
    } finally {
      setActivityLoading(false);
    }
  }

  useEffect(() => { void loadActivity(); }, []);

  const filteredActivity = useMemo(() => activity.filter(x => {
    const t = new Date(x.createdAt).getTime();
    const from = activityFrom ? new Date(activityFrom).getTime() : Number.NEGATIVE_INFINITY;
    const to = activityTo ? new Date(activityTo).getTime() + 86_399_999 : Number.POSITIVE_INFINITY;
    return t >= from && t <= to;
  }), [activity, activityFrom, activityTo]);

  let group = '';
  return <div className="app-shell">
    <aside className="sidebar">
      <div className="brand">
        <div className="brand-mark">P</div>
        <div><div className="brand-name">PayOrchestrator</div><div className="brand-sub">PAYMENT CONTROL PLANE</div></div>
      </div>
      <div className="live-pill"><span className={connected ? 'dot online' : 'dot'}></span>{connected ? 'Live operations' : 'Offline feed'}</div>
      <nav className="nav-scroll">
        <nav className="nav">{visible.map(item => {
          const active = item.to === '/' ? location.pathname === '/' : location.pathname.startsWith(item.to);
          const heading = item.group !== group;
          group = item.group;
          return <div key={item.to}>{heading && <div className="nav-group">{item.group}</div>}<NavLink to={item.to} className={active ? 'nav-item active' : 'nav-item'}><span className="nav-icon">{item.icon}</span><span>{item.label}</span></NavLink></div>;
        })}</nav>
      </nav>
      <div className="sidebar-footer-note">Role: <b>{user?.role}</b><br />Protected control-plane session</div>
    </aside>

    <main className="main">
      <header className="topbar">
        <div><span className="eyebrow">CONTROL CENTER</span><h1>{title(location.pathname)}</h1></div>
        <div className="top-actions">
          <span className="secure-badge">● Secure session</span>
          <div className="account-wrap">
            <button className="account-button" onClick={() => setAccountOpen(v => !v)} aria-expanded={accountOpen}>
              <div className="top-avatar">{user?.displayName?.slice(0, 1).toUpperCase()}</div>
              <div className="account-copy"><b>{user?.displayName}</b><span>{user?.role}</span></div>
              <span className="chevron">⌄</span>
            </button>
            {accountOpen && <div className="account-menu">
              <div className="account-menu-head"><b>{user?.email}</b><span>{user?.status}</span></div>
              <button className="menu-action" onClick={() => { setActivityOpen(v => !v); setAccountOpen(false); }}>Security activity <span>›</span></button>
              <button className="menu-action danger-text" onClick={() => void logout()}>Sign out <span>↪</span></button>
            </div>}
          </div>
        </div>
      </header>

      {activityOpen && <section className="activity-panel">
        <div className="activity-panel-head"><div><b>Login & logout activity</b><span>Security events for your permitted scope</span></div><button className="icon-btn" onClick={() => setActivityOpen(false)}>×</button></div>
        <div className="activity-filter-row"><label>From<input className="input" type="date" value={activityFrom} onChange={e => setActivityFrom(e.target.value)} /></label><label>To<input className="input" type="date" value={activityTo} onChange={e => setActivityTo(e.target.value)} /></label><button className="btn secondary" onClick={() => { setActivityFrom(''); setActivityTo(''); }}>Reset</button><button className="btn secondary" onClick={() => void loadActivity()}>Refresh</button></div>
        {activityLoading ? <div className="empty">Loading security activity…</div> : <div className="activity-table-wrap"><table className="table"><thead><tr><th>Time</th><th>Event</th><th>Result</th></tr></thead><tbody>{filteredActivity.map(x => <tr key={x.id}><td>{new Date(x.createdAt).toLocaleString()}</td><td><span className={`event-pill ${x.action === 'login' ? 'event-login' : 'event-logout'}`}>{x.action === 'login' ? 'Login' : 'Logout'}</span></td><td><span className={`status ${x.allowed ? 'ok' : 'bad'}`}>{x.allowed ? 'Allowed' : 'Denied'}</span></td></tr>)}</tbody></table>{!filteredActivity.length && <div className="empty">No login/logout events for the selected dates.</div>}</div>}
      </section>}

      <div className="content"><Outlet /></div>
    </main>
  </div>;
}

function title(path: string) {
  if (path === '/') return 'Platform overview';
  if (path.startsWith('/payments')) return 'Payments';
  if (path.startsWith('/websites')) return 'Merchant websites';
  if (path.startsWith('/providers')) return 'PSP accounts';
  if (path.startsWith('/routing')) return 'Smart routing';
  if (path.startsWith('/users')) return 'Team & access';
  if (path.startsWith('/reports')) return 'Reports & audit';
  if (path.startsWith('/fraud')) return 'Risk control';
  return 'Marketplace operations';
}
