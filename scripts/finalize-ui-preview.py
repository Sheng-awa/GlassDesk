"""Package documentation after residency without rebuilding the tested executable."""
import hashlib, json, pathlib, shutil, zipfile, datetime

root = pathlib.Path(__file__).resolve().parent.parent
version = '0.3.0-preview.1'
release = root / 'artifacts/release' / version
runtime = root / 'artifacts/ui-preview' / version
manifest_path = release / 'release-verification.json'
manifest = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
if 'InstallerFile' in manifest and sha(release / manifest['InstallerFile']) != manifest['InstallerSha256']:
    raise RuntimeError('Tested installer changed')

for name, expected in manifest['RuntimeFiles'].items():
    if name == 'AcceptanceReport.md':
        continue
    if sha(runtime / name) != expected:
        raise RuntimeError('Tested runtime changed: ' + name)
for name, expected in manifest['SourceFiles'].items():
    if name.startswith('docs/'):
        continue
    if sha(root / name) != expected:
        raise RuntimeError('Built source changed: ' + name)
baseline = json.loads((root / 'artifacts/qa/ui-0.3.0-preview.1/baseline.json').read_text(encoding='utf-8-sig'))
for name, expected in baseline['ReleaseFiles'].items():
    if sha(root / 'artifacts/release/0.2.0' / name) != expected:
        raise RuntimeError('Protected formal release changed: ' + name)

report = root / 'docs/ui-acceptance-0.3.0-preview.1.md'
shutil.copyfile(report, runtime / 'AcceptanceReport.md')
shutil.copyfile(report, release / 'AcceptanceReport.md')
excluded = {'artifacts', '.tools', 'bin', 'obj', '.git', '.vs', '__pycache__'}
def files(directory, skip):
    result = []
    def walk(folder):
        for path in folder.iterdir():
            if path.name in skip:
                continue
            if path.is_symlink() or path.is_junction():
                raise RuntimeError('Refusing a reparse point: ' + str(path))
            if path.is_dir():
                walk(path)
            elif path.suffix != '.user':
                result.append(path)
    walk(directory)
    return sorted(result)

def archive(destination, directory, entries):
    with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for path in entries:
            output.write(path, path.relative_to(directory).as_posix())
    expected = {p.relative_to(directory).as_posix(): sha(p) for p in entries}
    with zipfile.ZipFile(destination) as source:
        if len(source.infolist()) != len(expected):
            raise RuntimeError('Archive entry count mismatch')
        for entry in source.infolist():
            if hashlib.sha256(source.read(entry)).hexdigest() != expected[entry.filename]:
                raise RuntimeError('Archive byte mismatch: ' + entry.filename)
    return expected

manifest['SourceFiles'] = archive(release / manifest['SourceArchive'], root, files(root, excluded))
manifest['RuntimeFiles'] = archive(release / manifest['RuntimeArchive'], runtime, files(runtime, {'preview-data'}))
manifest['SourceSha256'] = sha(release / manifest['SourceArchive'])
manifest['RuntimeSha256'] = sha(release / manifest['RuntimeArchive'])
manifest['AcceptanceReportIncluded'] = True
manifest['AcceptanceReportSha256'] = sha(report)
manifest['DocumentationFinalizedUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
checksums = ''.join(f'{manifest[k+"Sha256"]}  {manifest[k+"Archive"]}\n' for k in ('Runtime', 'Source'))
if 'InstallerFile' in manifest:
    checksums += f'{manifest["InstallerSha256"]}  {manifest["InstallerFile"]}\n'
(release / 'SHA256SUMS.txt').write_text(checksums, encoding='utf-8')
print('Preview archives verified; tested runtime bytes unchanged.')
