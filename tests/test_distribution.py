"""Check release contents and generation without any template."""
from pathlib import Path
import json, os, shutil, subprocess, tempfile, zipfile

repo = Path(__file__).resolve().parent.parent
with tempfile.TemporaryDirectory(prefix='distribution-test-', dir=repo/'dist') as temporary:
    target = Path(temporary)
    with zipfile.ZipFile(repo/'dist/AnnouncerModGenerator-v1.4.1.zip') as archive:
        assert archive.testzip() is None
        names = archive.namelist()
        assert not any('techannouncer' in name.lower() or 'ffmpeg' in name.lower() for name in names), names
        assert sum(name.lower().endswith('.exe') for name in names) == 1
        archive.extractall(target)
    tool = target/'AnnouncerModGenerator'
    exe = tool/'AnnouncerMod生成器.exe'
    # The extracted release has never had a template. Only an external converter is provided.
    output = target/'ExternalDependencyTest.zip'
    result = subprocess.run([str(exe), '--name', 'ExternalDependencyTest', '--input-dir',
        os.environ['ANNOUNCER_AUDIO_DIR'], '--ffmpeg', os.environ['FFMPEG_PATH'],
        '--output', str(output)], capture_output=True, encoding='utf-8-sig', timeout=90)
    assert result.returncode == 0, (result.stdout, result.stderr)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        assert len(json.loads(archive.read('build-info.json'))['sources']) == 9
        assert 'bin/ExternalDependencyTest.dll' in archive.namelist()
        assert len([n for n in archive.namelist() if n.endswith('.dll')]) == 1
        assert not any(n.endswith(('.bank','.guids.txt')) for n in archive.namelist())
        assert json.loads(archive.read('build-info.json'))['runtime'] == 'IndependentAnnouncer'
    manifest=target/'empty.json';manifest.write_text('{}',encoding='utf-8')
    result=subprocess.run([str(exe),'--name','EmptyDistribution','--inputs',str(manifest),'--ffmpeg',str(target/'missing.exe'),
        '--output',str(target/'EmptyDistribution.zip')],capture_output=True,encoding='utf-8-sig',timeout=30)
    assert result.returncode==0,(result.stdout,result.stderr)
    print('PASS: clean release generates nine-event mod without template and empty mod without FFmpeg.')
