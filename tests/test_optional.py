"""Optional inputs, 13 events, five variants, real FMOD playback and runtime hooks."""
from pathlib import Path
import ctypes as C, json, math, os, subprocess, tempfile, time, uuid, wave, zipfile
import numpy as np

repo = Path(__file__).resolve().parent.parent
exe = repo/'dist/AnnouncerMod生成器.exe'
ffmpeg = Path(os.environ['FFMPEG_PATH'])
game = Path(os.environ['CELESTE_DIR'])
events = ['cornerboost','demodash','fastbubble','hyperdash','neutral','superdash','ultradash','wallbounce','wavedash','death','goldendeath','strawberry','goldenstrawberry']
compiler = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
subprocess.run([str(compiler),'/nologo','/out:'+str(repo/'dependencies/RuntimeTest.exe'),
    *['/reference:'+str(repo/'dependencies'/name) for name in ['AnnouncerRuntime.dll','Celeste.dll','FNA.dll','MMHOOK_Celeste.dll']],
    str(repo/'tests/RuntimeTest.cs')], check=True, capture_output=True)
subprocess.run([str(repo/'dependencies/RuntimeTest.exe')], check=True)
with tempfile.TemporaryDirectory(prefix='optional-test-',dir=repo/'dist') as temporary:
    target = Path(temporary)
    files=[]
    for i in range(5):
        source=target/f'death_{i+1}.wav'
        samples=(np.sin(np.arange(14400)*2*np.pi*(310+i*91)/48000)*12000).astype('<i2')
        with wave.open(str(source),'wb') as wav:
            wav.setnchannels(1);wav.setsampwidth(2);wav.setframerate(48000);wav.writeframes(samples.tobytes())
        files.append(str(source))
    def generate(name, mapping, ok=True, converter=ffmpeg):
        manifest=target/(name+'.json');manifest.write_text(json.dumps(mapping),encoding='utf-8')
        result=subprocess.run([str(exe),'--name',name,'--inputs',str(manifest),'--ffmpeg',str(converter),'--output',str(target/(name+'.zip'))],capture_output=True,encoding='utf-8-sig',timeout=120)
        assert (result.returncode==0)==ok,(name,result.stdout,result.stderr)
        return target/(name+'.zip')
    empty=generate('EmptyOptional',{},converter=target/'missing.exe')
    with zipfile.ZipFile(empty) as archive:
        assert not any(name.endswith('.bank') for name in archive.namelist())
        assert all(count==0 for count in json.loads(archive.read('build-info.json'))['counts'].values())
    partial=generate('PartialOptional',{'death':files[:2],'demodash':[],'strawberry':None})
    with zipfile.ZipFile(partial) as archive:
        assert len([n for n in archive.namelist() if n.endswith('.bank')])==2
    generate('TooManyOptional',{'death':files+[files[0]]},False)
    generate('DuplicateOptional',{'death':[files[0],files[0]]},False)
    generate('MissingOptional',{'death':['does-not-exist.wav']},False)
    generate('UnknownOptional',{'typo':files[:1]},False)
    generate('BadTypeOptional',{'death':42},False)
    legacy=generate('LegacyOptional',{'demodash':files[0]})
    full=generate('FullOptional',{event:files for event in events})
    with zipfile.ZipFile(full) as archive:
        assert archive.testzip() is None
        info=json.loads(archive.read('build-info.json'))
        assert len(info['sources'])==13 and all(value==5 for value in info['counts'].values())
        assert len([name for name in archive.namelist() if name.endswith('.bank')])==65
        archive.extractall(target/'full')
    subprocess.run([os.sys.executable,str(repo/'tests/test_detector.py'),str(full)],check=True)
    # Check auto matching of numbered files and an optional partial folder.
    result=subprocess.run([str(exe),'--name','FolderOptional','--input-dir',str(target),'--ffmpeg',str(ffmpeg),'--output',str(target/'FolderOptional.zip')],capture_output=True,encoding='utf-8-sig',timeout=120)
    assert result.returncode==0,(result.stdout,result.stderr)
    with zipfile.ZipFile(target/'FolderOptional.zip') as archive:
        assert json.loads(archive.read('build-info.json'))['counts']['death']==5
    # Audio libraries and events must coexist, including another generated pack.
    native=os.add_dll_directory(str(game/'lib64-win-x64'))
    core=C.CDLL(str(game/'lib64-win-x64/fmod64.dll'));studio=C.CDLL(str(game/'lib64-win-x64/fmodstudio.dll'))
    P=C.c_void_p;I=C.c_int;U=C.c_uint
    def call(lib,name,*args):
        value=getattr(lib,'FMOD_'+name)(*args);assert value==0,(name,value)
    checked=[]
    all_ids=[]
    for guidfile in (target/'full/Audio').glob('*.guids.txt'):
        for line in guidfile.read_text(encoding='utf-8').splitlines():
            guid,path=line.split(' ',1)
            if '/fulloptional/' in path: all_ids.append((guid,path))
    assert len(all_ids)==65 and len(set(guid for guid,path in all_ids))==65
    previous = Path.cwd()
    os.chdir(target)
    try:
        for event in events:
            # Play all five variants of each event through the game's actual FMOD engine.
            for variant in range(1,6):
                system,low=P(),P();wavpath=target/f'render-{event}-{variant}.wav'
                call(studio,'Studio_System_Create',C.byref(system),U(0x00011000))
                try:
                    call(studio,'Studio_System_GetLowLevelSystem',system,C.byref(low))
                    call(core,'System_SetOutput',low,I(5));call(core,'System_SetSoftwareFormat',low,I(48000),I(3),I(0));call(core,'System_SetDSPBufferSize',low,U(1024),I(4))
                    call(studio,'Studio_System_Initialize',system,I(128),U(20),U(3),C.c_char_p(wavpath.name.encode('ascii')))
                    for path in [game/'Content/FMOD/Desktop/Master Bank.bank',*(target/'full/Audio').glob('*.bank')]:
                        bank=P();call(studio,'Studio_System_LoadBankFile',system,C.c_char_p(os.fsencode(path)),U(0),C.byref(bank))
                        call(studio,'Studio_Bank_LoadSampleData',bank)
                    call(studio,'Studio_System_FlushSampleLoading',system)
                    guid,path=next(item for item in all_ids if item[1].endswith('/'+event+'_v'+str(variant)))
                    desc,instance=P(),P();id_=C.create_string_buffer(uuid.UUID(guid.strip('{}')).bytes_le,16)
                    call(studio,'Studio_System_GetEventByID',system,id_,C.byref(desc))
                    call(studio,'Studio_EventDescription_CreateInstance',desc,C.byref(instance));call(studio,'Studio_EventInstance_Start',instance)
                    # Allow the WAV writer's startup buffer to drain for short clips.
                    for i in range(180):
                        call(studio,'Studio_System_Update',system)
                        time.sleep(.001)
                    call(studio,'Studio_EventInstance_Release',instance)
                finally:call(studio,'Studio_System_Release',system)
                def decode(path):
                    return np.frombuffer(subprocess.check_output([str(ffmpeg),'-v','error','-i',str(path),'-ar','48000','-ac','1','-f','s16le','-']),dtype='<i2').astype(float)
                expected=decode(files[variant-1]);actual=decode(wavpath)
                size=1<<(len(expected)+len(actual)-1).bit_length()
                corr=np.fft.irfft(np.fft.rfft(actual,size)*np.conj(np.fft.rfft(expected,size)),size)
                # Loading 65 banks writes startup silence into the WAV capture.
                lag=int(np.argmax(corr[:len(actual)-len(expected)+1]));heard=actual[lag:lag+len(expected)]
                score=float(np.dot(expected,heard)/(np.linalg.norm(expected)*np.linalg.norm(heard)))
                assert score>.99,(event,variant,score)
                checked.append({'event':event,'variant':variant,'correlation':score})
            print('FMOD:',event,'all five variants passed',flush=True)
    finally:
        os.chdir(previous)
    (repo/'docs/optional-validation.json').write_text(json.dumps({'runtime_hooks':'passed','input_cases':10,'fmod_clips':checked,'gameplay_tested':False},ensure_ascii=False,indent=2),encoding='utf-8')
print('PASS: optional and invalid inputs, 13 x 5 clips, real FMOD playback and runtime behavior.')
