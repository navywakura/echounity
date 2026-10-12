'use strict';
const $=id=>document.getElementById(id);let selected=null,trace=null;
const pretty=x=>JSON.stringify(x,null,2);
async function get(path){const r=await fetch(path,{cache:'no-store'});if(!r.ok)throw Error('Registro no disponible ('+r.status+')');return r.json();}
function show(step){selected=step.seq;$('title').textContent='Decisión '+step.seq+' · '+step.decision.action;
 $('observation').textContent=pretty(step.observation);$('decision').textContent=pretty(step.decision);
 $('receipt').textContent=step.causes.length?pretty(step.causes):'Esta decisión no invocó un veto CAM. No se atribuye una causa inventada.';
 const cause=step.causes[0],ctx=cause?.context_sha256;
 $('observation').parentElement.open=!cause;$('receipt').parentElement.open=!!cause;
 const nodes=[step.observation?.rays?'Rayos de profundidad':'Objetos y estado observados',ctx?'Contexto (SHA-256 del JSON) '+ctx.slice(0,16)+'…':'Sin consulta CAM de veto',cause?'CAM #'+cause.slot+' · '+(cause.verified?'extracto verificado':'error de integridad'):'Sin veto registrado',cause?'Gate '+(['OK','MODIFY','BLOCK'][cause.gate.gate_result]??cause.gate.gate_result):'Gate OK', 'Comando: '+step.decision.action];
 $('chain').replaceChildren();nodes.forEach((label,i)=>{if(i){const a=document.createElement('span');a.className='arrow';a.textContent='→';$('chain').append(a);}const n=document.createElement('div');n.className='node';n.textContent=label;$('chain').append(n);});
 document.querySelectorAll('#steps button').forEach(b=>b.setAttribute('aria-current',String(Number(b.dataset.seq)===selected)));
}
async function refresh(){try{const sessions=await get('/sessions'),old=$('session').value||new URLSearchParams(location.search).get('session');
 $('session').replaceChildren(...sessions.slice().reverse().map(s=>{const o=document.createElement('option');o.value=s.session;o.textContent=s.session.slice(0,12)+(s.closed?' · cerrada':' · activa');return o;}));
 if(sessions.some(s=>s.session===old))$('session').value=old;
 if(!sessions.length){$('status').textContent='Sin sesiones. Inicia el arácnido o una misión de la estación.';return;}
 trace=await get('/trace?session='+encodeURIComponent($('session').value));$('steps').replaceChildren();
 trace.steps.slice().reverse().forEach(step=>{const b=document.createElement('button');b.dataset.seq=step.seq;b.textContent='#'+step.seq+' '+step.decision.action+(step.causes.length?' · veto CAM':'');b.className=step.causes.length?'veto':'';b.onclick=()=>show(step);$('steps').append(b);});
 $('memories').textContent=pretty(trace.receipts);const chosen=trace.steps.find(s=>s.seq===selected)||trace.steps.at(-1);if(chosen)show(chosen);
 $('status').textContent='Registro leído · '+new Date().toLocaleTimeString();
 }catch(e){$('status').textContent=e.message;}}
$('refresh').onclick=refresh;$('session').onchange=()=>{selected=null;refresh();};refresh();
