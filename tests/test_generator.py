from pathlib import Path
import subprocess, json, zipfile, ctypes as C, os, uuid, time, math
import numpy as np

TEST = Path(__file__).resolve().parent
TOOL = TEST.parent
ROOT = Path(os.environ['ANNOUNCER_AUDIO_DIR']).resolve()
EXE = TOOL / 'dist' / 'AnnouncerMod生成器.exe'
FFMPEG = Path(os.environ['FFMPEG_PATH']).resolve()
GAME = Path(os.environ['CELESTE_DIR']).resolve()
SOURCES = {'cornerboost':'CornerBoost.mp3','demodash':'Demodash.mp3','fastbubble':'fastbubble.mp3','hyperdash':'Hyperdash.mp3','neutral':'NeutralJump.mp3','superdash':'superdash.mp3','ultradash':'UltraDash.mp3','wallbounce':'WallBounce.mp3','wavedash':'wavedash.mp3'}

def run(args, ok=True):
    r = subprocess.run([str(EXE), *args], capture_output=True, encoding='utf-8-sig', timeout=90)
    assert (r.returncode == 0) == ok, (r.returncode, r.stdout, r.stderr)
    return r

def pcm(path):
    b = subprocess.check_output([str(FFMPEG), '-v','error','-i',str(path),'-ar','48000','-ac','1','-f','s16le','-'])
    return np.frombuffer(b, dtype='<i2').astype(np.float64)

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
            elif 'Celeste.Audio::Play(' in value: sounds.append(stack.pop());stack.append('soundhandle')
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

def render(package, sources, other_package):
    dll_handle=os.add_dll_directory(str(GAME/'lib64-win-x64'))
    core=C.CDLL(str(GAME/'lib64-win-x64/fmod64.dll')); studio=C.CDLL(str(GAME/'lib64-win-x64/fmodstudio.dll'))
    P=C.c_void_p; I=C.c_int; U=C.c_uint
    def call(lib,name,*args):
        result=getattr(lib,'FMOD_'+name)(*args); assert result==0,(name,result)
    with zipfile.ZipFile(package) as z:
        assert z.testzip() is None
        bankname=next(n for n in z.namelist() if n.endswith('.bank'))
        bankfile=TEST/(package.stem+'.bank'); bankfile.write_bytes(z.read(bankname))
        guids=z.read(next(n for n in z.namelist() if n.endswith('.guids.txt'))).decode()
        info=json.loads(z.read('build-info.json'));slug=info['name'].lower()
        assert 'TechAnnouncerConfig.yaml' not in z.namelist()
        assert f'bin/{info["name"]}.dll' in z.namelist()
        assert f'{info["name"]}Config.yaml' in z.namelist()
    with zipfile.ZipFile(other_package) as z:
        otherfile=TEST/'coexist.bank';otherfile.write_bytes(z.read(next(n for n in z.namelist() if n.endswith('.bank'))))
    old_cwd=Path.cwd();os.chdir(TEST)
    checks=[]
    try:
        for line in guids.splitlines():
            guid,path=line.split(' ',1)
            if f'/{slug}/' not in path or path.endswith('/farewell'):continue
            event=path.rsplit('/',1)[-1];expected=pcm(sources[event]);system,low,bank,desc,instance=P(),P(),P(),P(),P()
            call(studio,'Studio_System_Create',C.byref(system),U(0x00011000))
            try:
                call(studio,'Studio_System_GetLowLevelSystem',system,C.byref(low));call(core,'System_SetOutput',low,I(5));call(core,'System_SetSoftwareFormat',low,I(48000),I(3),I(0));call(core,'System_SetDSPBufferSize',low,U(1024),I(4))
                wav=(package.stem+'_'+event+'.wav').encode('ascii')
                call(studio,'Studio_System_Initialize',system,I(64),U(20),U(3),C.c_char_p(wav))
                for p in [GAME/'Content/FMOD/Desktop/Master Bank.bank',TEST/'original-template.bank',otherfile,bankfile]:
                    loaded=P();call(studio,'Studio_System_LoadBankFile',system,C.c_char_p(os.fsencode(p)),U(0),C.byref(loaded));bank=loaded
                call(studio,'Studio_Bank_LoadSampleData',bank);call(studio,'Studio_System_FlushSampleLoading',system)
                id_=C.create_string_buffer(uuid.UUID(guid.strip('{}')).bytes_le,16);call(studio,'Studio_System_GetEventByID',system,id_,C.byref(desc))
                length=I();call(studio,'Studio_EventDescription_GetLength',desc,C.byref(length))
                assert length.value >= len(expected)/48,(event,length.value,len(expected)/48)
                call(studio,'Studio_EventDescription_CreateInstance',desc,C.byref(instance));call(studio,'Studio_EventInstance_Start',instance)
                for i in range(math.ceil((len(expected)+24000)/1024)+30):
                    call(studio,'Studio_System_Update',system);time.sleep(.001)
                call(studio,'Studio_EventInstance_Release',instance)
            finally:call(studio,'Studio_System_Release',system)
            actual=pcm(TEST/wav.decode());size=1<<(len(expected)+len(actual)-1).bit_length()
            corr=np.fft.irfft(np.fft.rfft(actual,size)*np.conj(np.fft.rfft(expected,size)),size)
            lag=int(np.argmax(corr[:min(24000,len(actual)-len(expected))]));heard=actual[lag:lag+len(expected)]
            score=float(np.dot(expected,heard)/(np.linalg.norm(expected)*np.linalg.norm(heard)))
            tail=expected[len(expected)*3//4:];h_tail=heard[len(expected)*3//4:]
            tail_score=float(np.dot(tail,h_tail)/(np.linalg.norm(tail)*np.linalg.norm(h_tail)))
            assert score>.99 and tail_score>.9,(event,score,tail_score)
            checks.append(dict(event=event,correlation=score,tail_correlation=tail_score,timeline_ms=length.value))
            print(package.stem,event,round(score,4),length.value,flush=True)
    finally:os.chdir(old_cwd)
    assert len(checks)==9
    return checks

def main():
    with zipfile.ZipFile(TOOL/'dist/TechAnnouncer.zip') as template:
        (TEST/'original-template.bank').write_bytes(template.read('Audio/TechAnnouncer.bank'))
    basic=TEST/'RosmontisAnnouncer.zip'
    run(['--name','RosmontisAnnouncer','--input-dir',str(ROOT),'--output',str(basic),'--overwrite'])
    run(['--name','../bad','--input-dir',str(ROOT),'--output',str(TEST/'invalid.zip')],False)
    before=basic.read_bytes();run(['--name','RosmontisAnnouncer','--input-dir',str(ROOT),'--output',str(basic)],False);assert before==basic.read_bytes()
    inputs=TEST/'长语音 inputs';inputs.mkdir(exist_ok=True)
    source_map={}
    for i,(event,filename) in enumerate(SOURCES.items()):
        ext='.flac' if i%2==0 else '.wav';path=inputs/('任意文件名 '+str(i)+ext)
        subprocess.run([str(FFMPEG),'-v','error','-y','-stream_loop','4','-i',str(ROOT/filename),str(path)],check=True,capture_output=True)
        source_map[event]=str(path)
    manifest=TEST/'inputs.json';manifest.write_text(json.dumps(source_map,ensure_ascii=False),encoding='utf-8')
    longpack=TEST/'LongDemoAnnouncer.zip'
    run(['--name','LongDemoAnnouncer','--inputs',str(manifest),'--output',str(longpack),'--overwrite'])
    missing=dict(source_map);del missing['demodash'];manifest.write_text(json.dumps(missing),encoding='utf-8')
    run(['--name','MissingPack','--inputs',str(manifest),'--output',str(TEST/'missing.zip')],False);assert not (TEST/'missing.zip').exists()
    manifest.write_text(json.dumps(source_map,ensure_ascii=False),encoding='utf-8')
    assertions=detector_tests(basic)
    subprocess.run([str(EXE),'--screenshot',str(TEST/'界面.png')],check=True,timeout=30)
    report=dict(detector_cases=assertions,input_errors_and_overwrite='passed',regular=render(basic,{e:ROOT/f for e,f in SOURCES.items()},longpack),long=render(longpack,source_map,basic))
    (TEST/'测试报告.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Generator, detector and both sets of nine audio events passed.',flush=True)
if __name__=='__main__':main()
