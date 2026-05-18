# build.spec
# PyInstaller spec file for trocr_server.py
# Run: pyinstaller build.spec

from PyInstaller.utils.hooks import collect_data_files, collect_submodules

block_cipher = None

# Collect all necessary data files and submodules
datas = []
datas += collect_data_files('transformers')
datas += collect_data_files('tokenizers')
datas += collect_data_files('huggingface_hub')

hiddenimports = []
hiddenimports += collect_submodules('transformers')
hiddenimports += collect_submodules('PIL')
hiddenimports += ['flask', 'werkzeug', 'jinja2', 'click', 'itsdangerous']

a = Analysis(
    ['trocr_server.py'],
    pathex=[],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name='trocr_server',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,          # no CMD window shown to user
    disable_windowed_traceback=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
