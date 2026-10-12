"""Plot existing ECHO-5 ablations; never relabel them as ECHO-4 versus ECHO-5."""
import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
import numpy as np

ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('evidence',type=Path,help='efficiency-pilot-20261012-r3 directory')
    parser.add_argument('--output',type=Path,default=ROOT/'Docs/Arachnid/figures')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    sources={};data={}
    for name in ('search_fresh','exploration','curriculum'):
        path=args.evidence/name/'result.json';raw=path.read_bytes()
        data[name]=json.loads(raw)
        sources[name]={'file':str(path.resolve()),'sha256':hashlib.sha256(raw).hexdigest()}
    search={key:group['supported_total'] for key,group in data['search_fresh']['summary'].items()}
    exploration=data['exploration']['summary']['groups']['all']['arms']
    curriculum={}
    for arm,cost in data['curriculum']['cost']['arms'].items():
        rows=[r for r in data['curriculum']['results'] if r['condition']==arm]
        final=[e for r in rows for e in r['prefixes'][-1]['evaluations']]
        curriculum[arm]={'runs':len(rows),'exact':sum(e['success'] for e in final),
                         'cases':len(final),'work':cost['total']['used']}
    metrics={'schema':'echo.arachnid.figures.v1','sources':sources,'search':search,
             'exploration':exploration,'curriculum':curriculum,
             'scope':'Descriptive ECHO-5 pilot, not ECHO-4 versus ECHO-5. Logical work is not energy or wall time.'}
    (args.output/'metrics.json').write_text(json.dumps(metrics,indent=2)+'\n')
    plt.rcParams.update({'font.family':'DejaVu Sans','font.size':11,'axes.spines.top':False,
        'axes.spines.right':False,'axes.titleweight':'bold','axes.labelcolor':'#253b48',
        'text.color':'#172e3c','axes.edgecolor':'#9cadb7','figure.facecolor':'#f5f8fa',
        'axes.facecolor':'#f5f8fa','savefig.facecolor':'#f5f8fa'})
    colors=['#889ba7','#6e91ab','#ce8958','#168a91']
    arms=['baseline','memoized','certified','certified_bound']
    labels=['Baseline','Memoized','Certified','Certified\nbound']

    def save(fig,name,footnote):
        fig.text(.07,.035,footnote,fontsize=9,color='#526c7b')
        fig.subplots_adjust(left=.09,right=.97,top=.83,bottom=.24,wspace=.25)
        fig.savefig(args.output/(name+'.png'),dpi=180)
        fig.savefig(args.output/(name+'.svg'))
        plt.close(fig)

    fig,axes=plt.subplots(1,2,figsize=(12,5.4))
    fig.suptitle('ECHO-5 · tareas resueltas con el mismo presupuesto',x=.07,ha='left',fontsize=18)
    for ax,key,title in zip(axes,('independent','template'),('Generador independiente','Plantillas')):
        vals=[search[key][a] for a in arms]
        bars=ax.bar(labels,[v['exact'] for v in vals],color=colors,width=.66)
        for b,v in zip(bars,vals):ax.text(b.get_x()+b.get_width()/2,b.get_height()+.4,f"{v['exact']}/{v['cases']}",ha='center')
        ax.set_ylim(0,vals[0]['cases']*1.17);ax.set_title(title,loc='left',fontsize=12);ax.set_ylabel('Aciertos exactos')
        ax.set_axisbelow(True);ax.yaxis.grid(alpha=.18)
    save(fig,'01-search-success','326 operadores · 2.000.000 unidades por tarea · Piloto r3, 12 oct 2026.\nSeis tareas independientes siguen sin resolverse. No es una comparación con ECHO-4.0.')

    fig,axes=plt.subplots(1,2,figsize=(12,5.4))
    fig.suptitle('ECHO-5 · trabajo lógico consumido por la búsqueda',x=.07,ha='left',fontsize=18)
    for ax,key,title in zip(axes,('independent','template'),('18 tareas independientes','9 tareas de plantilla')):
        vals=[search[key][a]['work']/1e6 for a in arms]
        bars=ax.bar(labels,vals,color=colors,width=.66)
        for b,v in zip(bars,vals):ax.text(b.get_x()+b.get_width()/2,v+.35,f'{v:.2f}',ha='center')
        ax.set_ylim(0,max(vals)*1.18);ax.set_title(title,loc='left',fontsize=12)
        ax.set_ylabel('Millones de unidades lógicas');ax.set_axisbelow(True);ax.yaxis.grid(alpha=.18)
    save(fig,'02-search-work','Certified bound consume 27,90 % menos en independientes y 34,36 % menos en plantillas que baseline.\nSon unidades del contador del experimento: no milisegundos, vatios ni julios.')

    fig,axes=plt.subplots(1,2,figsize=(12,5.8))
    fig.suptitle('ECHO-5 · coste y resultado de exploración y currículo',x=.07,ha='left',fontsize=18)
    for ax,arms2,names,values,title,denom in (
        (axes[0],['frontier_information','frontier_nearest','frontier_random','frontier_hybrid'],['Información','Cercanía','Aleatorio','Híbrido'],exploration,'Exploración · 28 mapas',1e3),
        (axes[1],['sequential','random','adaptive','adaptive_efficient'],['Secuencial','Aleatorio','Adaptativo','Adaptativo\neficiente'],curriculum,'Currículo · 8 ejecuciones por brazo',1e3)):
        vals=[values[a]['work']/denom for a in arms2]
        bars=ax.bar(names,vals,color=colors,width=.66)
        for b,a in zip(bars,arms2):
            v=values[a];correct=v.get('successes',v.get('exact'));total=v.get('episodes',v.get('cases'))
            ax.text(b.get_x()+b.get_width()/2,b.get_height()+max(vals)*.025,f'{correct}/{total}',ha='center',fontweight='bold')
        ax.set_ylim(0,max(vals)*1.22);ax.set_ylabel('Miles de unidades lógicas');ax.set_title(title,loc='left',fontsize=12)
        ax.tick_params(axis='x',labelsize=9);ax.set_axisbelow(True);ax.yaxis.grid(alpha=.18)
    save(fig,'03-exploration-curriculum','Etiquetas = aciertos / casos; barras = trabajo total registrado. Currículo incluye preparación y evaluación.\nTodos los exploradores llegaron; el disparador informativo del híbrido no se activó en estos 28 mapas.')
    print(json.dumps({'output':str(args.output),'charts':3,'source_hashes':sources},indent=2))

if __name__=='__main__':main()
