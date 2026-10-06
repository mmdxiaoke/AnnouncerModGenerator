"""Check release contents and generation using user-supplied dependencies."""
from pathlib import Path
import json, os, shutil, subprocess, tempfile, zipfile

repo = Path(__file__).resolve().parent.parent
with tempfile.TemporaryDirectory(prefix='distribution-test-', dir=repo/'dist') as temporary:
    target = Path(temporary)
    with zipfile.ZipFile(repo/'dist/AnnouncerModGenerator-v1.1.2.zip') as archive:
        assert archive.testzip() is None
        names = archive.namelist()
        assert not any('techannouncer' in name.lower() or 'ffmpeg' in name.lower() for name in names), names
        assert sum(name.lower().endswith('.exe') for name in names) == 1
        archive.extractall(target)
    tool = target/'AnnouncerModGenerator'
    exe = tool/'AnnouncerMod生成器.exe'
    # Supply dependencies locally after checking the distributed package.
    shutil.copyfile(os.environ['ANNOUNCER_TEMPLATE_ZIP'], tool/'TechAnnouncer.zip')
    output = target/'ExternalDependencyTest.zip'
    result = subprocess.run([str(exe), '--name', 'ExternalDependencyTest', '--input-dir',
        os.environ['ANNOUNCER_AUDIO_DIR'], '--ffmpeg', os.environ['FFMPEG_PATH'],
        '--output', str(output)], capture_output=True, encoding='utf-8-sig', timeout=90)
    assert result.returncode == 0, (result.stdout, result.stderr)
    with zipfile.ZipFile(output) as archive:
        assert archive.testzip() is None
        assert len(json.loads(archive.read('build-info.json'))['sources']) == 9
        assert 'bin/ExternalDependencyTest.dll' in archive.namelist()
    print('PASS: release excludes template/FFmpeg and generates nine-event mod with supplied dependencies.')
