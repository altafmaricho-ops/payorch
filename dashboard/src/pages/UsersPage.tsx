import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { adjustCredit, changeUserStatus, createUser, resetPassword } from '../api/admin';
import { getUsers } from '../api/platform';
import { useAuth } from '../context/AuthContext';
import type { PlatformUser, UserStatus } from '../types';

const actions=(status:UserStatus)=>({PendingApproval:['approve','reject'],Active:['suspend','block'],Suspended:['activate','block'],Blocked:['activate'],Rejected:['approve']}[status]??[]);

export function UsersPage(){
  const {user}=useAuth(); const [rows,setRows]=useState<PlatformUser[]>([]); const [error,setError]=useState(''); const [open,setOpen]=useState(false); const [saving,setSaving]=useState(false);
  const [form,setForm]=useState({email:'',displayName:'',password:'',initialCreditLimit:0});
  const canManage=user?.role==='SuperAdmin'||user?.role==='Admin';
  async function load(){try{setError('');setRows(await getUsers());}catch(e:any){setError(e?.response?.data?.error??'Unable to load users.');}}
  useEffect(()=>{void load();},[]);
  const visible=useMemo(()=>rows.filter(x=>x.id!==user?.id),[rows,user?.id]);
  async function save(){try{setSaving(true);setError('');await createUser(form);setOpen(false);setForm({email:'',displayName:'',password:'',initialCreditLimit:0});await load();}catch(e:any){setError(e?.response?.data?.error??'Unable to create user.');}finally{setSaving(false);}}
  async function reset(id:string){const password=window.prompt('New temporary password (minimum 8 characters):','');if(!password)return;try{await resetPassword(id,password);window.alert('Password reset and existing sessions revoked.');}catch(e:any){setError(e?.response?.data?.error??'Unable to reset password.');}}
  async function credit(id:string){const value=Number(window.prompt('Credit adjustment (+ add, - remove):','0'));if(!Number.isFinite(value)||value===0)return;const reason=window.prompt('Reason:')??'';try{await adjustCredit(id,{delta:value,reason});await load();}catch(e:any){setError(e?.response?.data?.error??'Unable to adjust credit.');}}
  async function act(id:string,action:string){const reason=window.prompt(`Reason for ${action} (optional):`)??'';try{await changeUserStatus(id,action,reason);await load();}catch(e:any){setError(e?.response?.data?.error??`Unable to ${action} user.`);}}
  return <>
    <div className="hero"><div><h2>Team & access</h2><p>Controlled hierarchy with approval, suspension, blocking and credit management.</p></div>{canManage&&<button className="btn" onClick={()=>setOpen(true)}>+ Create {user?.role==='SuperAdmin'?'Admin':'SubAdmin'}</button>}</div>
    {error&&<div className="notice">{error}</div>}
    <div className="card section"><div className="toolbar"><span className="muted">{rows.length} accounts in your scope</span><button className="btn secondary" onClick={()=>void load()}>Refresh</button></div><table className="table"><thead><tr><th>Name</th><th>Role</th><th>Email</th><th>Parent</th><th>Credit</th><th>Status</th><th>Actions</th></tr></thead><tbody>{visible.map(u=><tr key={u.id}><td><b>{u.displayName}</b><div className="small">{u.statusReason??''}</div></td><td><span className="tag">{u.role}</span></td><td>{u.email}</td><td className="mono">{u.parentId?u.parentId.slice(0,8)+'…':'Root'}</td><td>₹{Number(u.creditBalance).toLocaleString('en-IN')} / ₹{Number(u.creditLimit).toLocaleString('en-IN')}</td><td><span className={`status ${u.status==='Active'?'ok':u.status==='Blocked'||u.status==='Rejected'?'bad':'wait'}`}>{u.status}</span></td><td><div style={{display:'flex',gap:6,flexWrap:'wrap'}}><button className="btn secondary" onClick={()=>void credit(u.id)}>Credit</button><button className="btn secondary" onClick={()=>void reset(u.id)}>Reset password</button>{actions(u.status).map(a=><button key={a} className={`btn ${a==='block'||a==='reject'?'danger':'secondary'}`} onClick={()=>void act(u.id,a)}>{a}</button>)}</div></td></tr>)}</tbody></table>{!visible.length&&<div className="empty">No subordinate accounts yet.</div>}</div>
    {open&&<div className="modal-backdrop"><div className="modal"><h2>Create {user?.role==='SuperAdmin'?'Admin':'SubAdmin'}</h2><div className="form-grid"><F l="Display name"><input className="input" value={form.displayName} onChange={e=>setForm({...form,displayName:e.target.value})}/></F><F l="Email"><input className="input" type="email" value={form.email} onChange={e=>setForm({...form,email:e.target.value})}/></F><F l="Temporary password"><input className="input" type="password" value={form.password} onChange={e=>setForm({...form,password:e.target.value})}/></F><F l="Credit limit"><input className="input" type="number" min="0" value={form.initialCreditLimit} onChange={e=>setForm({...form,initialCreditLimit:Number(e.target.value)})}/></F></div><div className="notice">New accounts start as Pending Approval. Approve the account before it can sign in.</div><div className="modal-actions"><button className="btn secondary" onClick={()=>setOpen(false)}>Cancel</button><button className="btn" disabled={saving} onClick={()=>void save()}>{saving?'Creating…':'Create account'}</button></div></div></div>}
  </>;
}
function F({l,children}:{l:string;children:ReactNode}){return <div className="field"><label>{l}</label>{children}</div>}
