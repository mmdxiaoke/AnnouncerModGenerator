from pathlib import Path
import subprocess, json, sys
TEST=Path(__file__).resolve().parent
def simulate(code, player, enabled=True, announce=True):
    stack, locals_, statics, sounds = [], {}, {}, []
    original_calls = 0
    args = ['delegate', player, {'X':1.0,'Y':0.0}]
    pos = 0
    while pos < len(code):
        instruction = code[pos]; op, value = instruction['op'], instruction['operand']; pos += 1
        if op.startswith('ldarg.'): stack.append(args[int(op.rsplit('.',1)[-1])])
        elif op.startswith('ldloc.'): stack.append(locals_[int(op.rsplit('.',1)[-1])])
        elif op.startswith('stloc.'): locals_[int(op.rsplit('.',1)[-1])] = stack.pop()
        elif op in ('ldfld','ldflda'):
            owner = stack.pop(); stack.append(owner[value.rsplit('::',1)[-1]])
        elif op == 'ldsfld': stack.append(statics.get(value, False))
        elif op == 'stsfld': statics[value] = stack.pop()
        elif op == 'ldstr' or op == 'ldc.r4': stack.append(value)
        elif op == 'ldc.i4.1': stack.append(1)
        elif op == 'ldc.i4.0': stack.append(0)
        elif op == 'pop': stack.pop()
        elif op == 'cgt': b,a=stack.pop(),stack.pop(); stack.append(a>b)
        elif op in ('br','br.s'): pos=value
        elif op in ('brtrue','brtrue.s','brfalse','brfalse.s'):
            truth=bool(stack.pop())
            if truth == op.startswith('brtrue'): pos=value
        elif op in ('beq','bne.un'):
            b,a=stack.pop(),stack.pop()
            if (a==b) == (op=='beq'): pos=value
        elif op in ('call','callvirt'):
            if '::get_Settings()' in value: stack.append('settings')
            elif '::get_Enabled()' in value: stack.pop();stack.append(enabled)
            elif '::get_AnnounceDemodash()' in value or '::get_AnnounceNeutralJump()' in value: stack.pop();stack.append(announce)
            elif '::get_Ducking()' in value: stack.append(stack.pop()['Ducking'])
            elif '::AudioPath(' in value: stack.append('event:'+stack.pop())
            elif 'Celeste.Audio::Play(' in value or '::PlayOptional(' in value: sounds.append(stack.pop());stack.append('soundhandle')
            elif 'orig_CallDashEvents::Invoke' in value:
                stack.pop();stack.pop();original_calls+=1;player['calledDashEvents']=True
            elif 'orig_WallJump::Invoke' in value:
                stack.pop();stack.pop();stack.pop();original_calls+=1
            elif 'orig_CorrectDashPrecision::Invoke' in value:
                vector=stack.pop();stack.pop();stack.pop();stack.append(vector);original_calls+=1
            else: raise AssertionError(value)
        elif op == 'ret': break
        else: raise AssertionError(op)
    assert original_calls == 1
    return sounds

def detector_tests(package):
    target=TEST/'detector_il.json'
    subprocess.run(['powershell.exe','-NoProfile','-ExecutionPolicy','Bypass','-File',str(TEST/'export_il.ps1'),str(package),str(target)],check=True,capture_output=True,timeout=30)
    il=json.loads(target.read_text(encoding='utf-8-sig'))
    assert il['old_namespace_references']==0
    code=il['Player_CallDashEvents']
    def player(**kw):
        p=dict(calledDashEvents=False,demoDashed=False,Ducking=False,DashDir={'X':1.0,'Y':0.0},onGround=False,jumpGraceTimer=0.0,moveX=0);p.update(kw);return p
    cases = [
        ('专用 Demo 键，动画未蹲下',player(demoDashed=True),True,True,1),
        ('手动水平蹲冲',player(Ducking=True),True,True,1),
        ('向左 Demo',player(demoDashed=True,DashDir={'X':-1.0,'Y':0.0}),True,True,1),
        ('普通水平冲刺',player(),True,True,0),
        ('普通下斜冲刺',player(Ducking=True,DashDir={'X':.707,'Y':.707}),True,True,0),
        ('重复冲刺事件',player(demoDashed=True,calledDashEvents=True),True,True,0),
        ('关掉 Mod',player(demoDashed=True),False,True,0),
        ('关掉 Demo 播报',player(demoDashed=True),True,False,0),
    ]
    for label,p,en,ann,count in cases:
        assert len(simulate(code,p,en,ann))==count,label
    assert not simulate(il['Player_CorrectDashPrecision'],player(demoDashed=True,Ducking=True))
    assert len(simulate(il['Player_WallJump'],player(moveX=0)))==1
    assert not simulate(il['Player_WallJump'],player(moveX=1))
    return len(cases)+3


if __name__ == "__main__":
    print("PASS:", detector_tests(Path(sys.argv[1]).resolve()), "Demo Dash / Neutral regression cases")
