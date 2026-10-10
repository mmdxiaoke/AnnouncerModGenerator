"""Template-free generation, all runtime hooks, and real FMOD 1.10 playback."""
from pathlib import Path
import ctypes as C
import io, json, os, subprocess, tempfile, time, wave, zipfile
import numpy as np

repo = Path(__file__).resolve().parent.parent
exe = repo / 'dist/AnnouncerMod生成器.exe'
ffmpeg = Path(os.environ['FFMPEG_PATH'])
game = Path(os.environ['CELESTE_DIR'])
compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
events = ['cornerboost','demodash','fastbubble','hyperdash','neutral','superdash','ultradash','wallbounce','wavedash','death','goldendeath','strawberry','goldenstrawberry']

def compile_test():
    result = subprocess.run([str(compiler), '/nologo', '/out:dependencies\\IndependentRuntimeTest.exe',
        '/reference:dependencies\\Celeste.dll', '/reference:dependencies\\FNA.dll', '/reference:dependencies\\MMHOOK_Celeste.dll',
        'tests\\IndependentRuntimeTest.cs'], cwd=repo, capture_output=True, text=True)
    assert result.returncode == 0, result.stdout + result.stderr

# Layout from the installed game's public API; only cbsize/length are set.
class ExInfo(C.Structure):
    _fields_ = [(name, kind) for name, kind in [
        ('cbsize',C.c_int),('length',C.c_uint),('fileoffset',C.c_uint),('numchannels',C.c_int),('defaultfrequency',C.c_int),
        ('format',C.c_int),('decodebuffersize',C.c_uint),('initialsubsound',C.c_int),('numsubsounds',C.c_int),
        ('inclusionlist',C.c_void_p),('inclusionlistnum',C.c_int),('pcmreadcallback',C.c_void_p),('pcmsetposcallback',C.c_void_p),
        ('nonblockcallback',C.c_void_p),('dlsname',C.c_void_p),('encryptionkey',C.c_void_p),('maxpolyphony',C.c_int),
        ('userdata',C.c_void_p),('suggestedsoundtype',C.c_int),('fileuseropen',C.c_void_p),('fileuserclose',C.c_void_p),
        ('fileuserread',C.c_void_p),('fileuserseek',C.c_void_p),('fileuserasyncread',C.c_void_p),('fileuserasynccancel',C.c_void_p),
        ('fileuserdata',C.c_void_p),('filebuffersize',C.c_int),('channelorder',C.c_int),('channelmask',C.c_uint),
        ('initialsoundgroup',C.c_void_p),('initialseekposition',C.c_uint),('initialseekpostype',C.c_uint),
        ('ignoresetfilesystem',C.c_int),('audioqueuepolicy',C.c_uint),('minmidigranularity',C.c_uint),('nonblockthreadid',C.c_int),('fsbguid',C.c_void_p)]]

compile_test()
with tempfile.TemporaryDirectory(prefix='independent-', dir=repo/'dist') as temporary:
    target = Path(temporary)
    mapping = {}
    for event_idx, event in enumerate(events):
        mapping[event] = []
        for variant in range(5):
            path = target / f'{event}_{variant+1}.wav'
            # Different waveform per slot; ramp + chirp verifies beginnings/tails too.
            length = 288000 if event_idx == 0 and variant == 0 else 14400
            t = np.arange(length) / 48000
            phase = 2*np.pi*((210+event_idx*41+variant*7)*t + 27*t*t)
            audio = (np.sin(phase)*12000*np.minimum(1,t*40)).astype('<i2')
            with wave.open(str(path),'wb') as wav:
                wav.setnchannels(1); wav.setsampwidth(2); wav.setframerate(48000); wav.writeframes(audio.tobytes())
            mapping[event].append(str(path))
    def generate(name, inputs, success=True, converter=ffmpeg, output=None, extra=()):
        manifest = target / (name+'.json'); manifest.write_text(json.dumps(inputs),encoding='utf-8')
        output = output or target / (name+'.zip')
        result = subprocess.run([str(exe),'--name',name,'--inputs',str(manifest),'--ffmpeg',str(converter),'--output',str(output),*extra],
            capture_output=True,encoding='utf-8-sig',timeout=150)
        assert (result.returncode == 0) == success, (name,result.stdout,result.stderr)
        return output
    # No template beside generator, no bank, no bank metadata, one DLL only.
    volume_file=target/'volumes.json';volume_file.write_text(json.dumps({'death':70,'ultradash':40,'fastbubble':60}),encoding='utf-8')
    full = generate('FullIndependent',mapping,extra=('--volumes',str(volume_file)))
    with zipfile.ZipFile(full) as archive:
        assert archive.testzip() is None
        assert set(archive.namelist()) == {'everest.yaml','bin/FullIndependent.dll','README.txt','build-info.json'}
        info=json.loads(archive.read('build-info.json'))
        assert info['runtime']=='IndependentAnnouncer' and all(v==5 for v in info['counts'].values())
        assert info['version']=='1.4.2'
        assert info['volumes']['death']==70 and info['volumes']['ultradash']==40 and info['volumes']['fastbubble']==60
        assert info['volumes']['demodash']==100
        archive.extract('bin/FullIndependent.dll',target)
    dll = target / 'bin/FullIndependent.dll'
    runtime = subprocess.run([str(repo/'dependencies/IndependentRuntimeTest.exe'),str(dll)], capture_output=True,encoding='utf-8',errors='replace')
    assert runtime.returncode == 0, runtime.stdout + runtime.stderr
    print(runtime.stdout.strip(),flush=True)
    # Export embedded WAVs and validate external type/member references against installed game.
    subprocess.run(['powershell','-NoProfile','-ExecutionPolicy','Bypass','-File',str(repo/'tests/validate_independent.ps1'),
        '-ModulePath',str(dll),'-GamePath',str(game),'-AudioDirectory',str(target/'audio')],check=True)
    empty = generate('EmptyIndependent',{},converter=target/'absent.exe')
    with zipfile.ZipFile(empty) as archive:
        assert all(v==0 for v in json.loads(archive.read('build-info.json'))['counts'].values())
    generate('PartialIndependent',{'death':mapping['death'][:2],'strawberry':None,'demodash':[]})
    generate('LegacyIndependent',{'demodash':mapping['demodash'][0]})
    generate('NullIndependent',None,False)
    generate('SixIndependent',{'death':mapping['death']+[mapping['death'][0]]},False)
    generate('DuplicateIndependent',{'death':[mapping['death'][0]]*2},False)
    generate('MissingIndependent',{'death':['missing.wav']},False)
    generate('UnknownIndependent',{'no_event':[]},False)
    generate('TypeIndependent',{'death':42},False)
    generate('NoConverterIndependent',{'death':mapping['death'][:1]},False,converter=target/'absent.exe')
    generate('BadName',{},False,extra=('--name','../unsafe'))
    generate('FullIndependent',mapping,False)
    generate('FullIndependent',mapping,extra=('--overwrite',))
    for index,bad in enumerate([{'death':-1},{'death':101},{'death':1.5},{'death':True},{'death':'50'},{'oops':50},None]):
        bad_volume=target/'bad-volume.json';bad_volume.write_text(json.dumps(bad),encoding='utf-8')
        generate('BadVolume'+str(index),{},False,extra=('--volumes',str(bad_volume)))
    # Decode silence, duration overflow and truncated/corrupt files.
    silent=target/'silent.wav'
    with wave.open(str(silent),'wb') as wav:
        wav.setnchannels(1);wav.setsampwidth(2);wav.setframerate(48000);wav.writeframes(bytes(9600))
    generate('SilentIndependent',{'death':[str(silent)]},False)
    broken=target/'broken.wav';broken.write_bytes(b'broken')
    generate('BrokenIndependent',{'death':[str(broken)]},False)
    generate('ProtectInput',{},False,output=target/'wrong.txt')
    result=subprocess.run([str(exe),'--name','FolderIndependent','--input-dir',str(target),'--ffmpeg',str(ffmpeg),
        '--output',str(target/'FolderIndependent.zip')],capture_output=True,encoding='utf-8-sig',timeout=150)
    assert result.returncode==0,(result.stdout,result.stderr)

    native=os.add_dll_directory(str(game/'lib64-win-x64'))
    core=C.CDLL(str(game/'lib64-win-x64/fmod64.dll'))
    studio=C.CDLL(str(game/'lib64-win-x64/fmodstudio.dll'))
    P=C.c_void_p; I=C.c_int; U=C.c_uint
    def call(lib,name,*args):
        code=getattr(lib,'FMOD_'+name)(*args); assert code==0,(name,code)
    checked=[]; previous=Path.cwd();os.chdir(target)
    try:
        for event in events:
            for variant in range(1,6):
                data=(target/'audio'/f'announcer.{event}.v{variant}.wav').read_bytes()
                with wave.open(io.BytesIO(data),'rb') as wav:
                    assert (wav.getframerate(),wav.getnchannels(),wav.getsampwidth())==(48000,1,2)
                    expected=np.frombuffer(wav.readframes(wav.getnframes()),dtype='<i2').astype(float)
                system,low,bank,bus,group,sound,channel=P(),P(),P(),P(),P(),P(),P()
                capture=target/f'render-{event}-{variant}.wav'
                call(studio,'Studio_System_Create',C.byref(system),U(0x00011000))
                try:
                    call(studio,'Studio_System_GetLowLevelSystem',system,C.byref(low))
                    call(core,'System_SetOutput',low,I(5));call(core,'System_SetSoftwareFormat',low,I(48000),I(3),I(0));call(core,'System_SetDSPBufferSize',low,U(1024),I(4))
                    call(studio,'Studio_System_Initialize',system,I(64),U(20),U(3),C.c_char_p(capture.name.encode('ascii')))
                    call(studio,'Studio_System_LoadBankFile',system,C.c_char_p(os.fsencode(game/'Content/FMOD/Desktop/Master Bank.bank')),U(0),C.byref(bank))
                    strings=P();call(studio,'Studio_System_LoadBankFile',system,C.c_char_p(os.fsencode(game/'Content/FMOD/Desktop/Master Bank.strings.bank')),U(0),C.byref(strings))
                    call(studio,'Studio_System_GetBus',system,C.c_char_p(b'bus:/gameplay_sfx'),C.byref(bus))
                    call(studio,'Studio_Bus_LockChannelGroup',bus)
                    call(studio,'Studio_System_FlushCommands',system)
                    call(studio,'Studio_Bus_GetChannelGroup',bus,C.byref(group))
                    raw=C.create_string_buffer(data);info=ExInfo();info.cbsize=C.sizeof(info);info.length=len(data)
                    call(core,'System_CreateSound',low,raw,U(2048|8|1),C.byref(info),C.byref(sound))
                    call(core,'System_PlaySound',low,sound,group,I(1),C.byref(channel))
                    call(core,'Channel_SetVolume',channel,C.c_float(.8));call(core,'Channel_SetPaused',channel,I(0))
                    for frame in range(int(len(expected)/1024)+120):
                        call(studio,'Studio_System_Update',system);time.sleep(.001)
                    call(core,'Sound_Release',sound);call(studio,'Studio_Bus_UnlockChannelGroup',bus)
                finally: call(studio,'Studio_System_Release',system)
                actual=np.frombuffer(subprocess.check_output([str(ffmpeg),'-v','error','-i',str(capture),'-ar','48000','-ac','1','-f','s16le','-']),dtype='<i2').astype(float)
                size=1<<(len(expected)+len(actual)-1).bit_length()
                corr=np.fft.irfft(np.fft.rfft(actual,size)*np.conj(np.fft.rfft(expected,size)),size)
                lag=int(np.argmax(corr[:len(actual)-len(expected)+1]));heard=actual[lag:lag+len(expected)]
                score=float(np.dot(expected,heard)/(np.linalg.norm(expected)*np.linalg.norm(heard)))
                assert score>.99,(event,variant,score)
                checked.append({'event':event,'variant':variant,'seconds':len(expected)/48000,'correlation':score})
            print('FMOD direct WAV:',event,'5/5',flush=True)
    finally: os.chdir(previous)
    Path(os.environ.get('ANNOUNCER_VALIDATION_REPORT', str(repo/'docs/v1.4.2-validation.json'))).write_text(json.dumps({'version':'1.4.2','runtime_hooks':'passed','ultra_regressions':'24 consecutive waves, post-dash 1.2x landing followed by normal/super jump, one announcement, invalid multipliers, stale landing cancellation, coyote jumps in both directions and grace expiration, strict pre-jump speed magnitude >240 boundaries and vertical component passed','bubble_regressions':'manual green and red exits, duplicate guard passed','event_volumes':'13 controls, defaults, live changes, mute and master multiplication passed','game_api_members':'92 verified', 'runtime_checks':int(runtime.stdout.strip().split()[-1]),
        'template_required':False,'fmod_version':'1.10.20','clips':checked,'gameplay_tested':False},indent=2),encoding='utf-8')
print('PASS: template-free generation, runtime hooks, inputs, 65 direct WAV clips through gameplay SFX bus.')
