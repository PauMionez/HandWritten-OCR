# PyOCR — Handwriting & Text Recognition

A Windows desktop application that uses Microsoft's **TrOCR** AI model to extract text from images.  
Drag & drop or browse an image → get the recognized text → copy to clipboard.

> **100% offline after first run. No API key. No internet needed. Images never leave your machine.**

---

## How It Works

| Step | Component                         | Role                                                 |
| ---- | --------------------------------- | ---------------------------------------------------- |
| 1    | WPF App (C#)                      | UI — sends image via HTTP POST to `localhost:5005`   |
| 2    | trocr_server.exe (Python + Flask) | Local server — receives image, passes to HuggingFace |
| 3    | HuggingFace Transformers          | Loads and runs the TrOCR model                       |
| 4    | PyTorch                           | Math engine — does the actual AI computation         |
| 5    | TrOCR Model                       | Reads image → outputs text                           |

```
User drops image
      ↓
  WPF App (C#)
      ↓  HTTP POST  localhost:5005
  trocr_server.exe
      ↓  HuggingFace + PyTorch
  TrOCR Model
      ↓
  Recognized Text  →  back to WPF app
```

---

## Technology Stack

| Library                      | Purpose                                                      |
| ---------------------------- | ------------------------------------------------------------ |
| **WPF (.NET 8)**             | Windows desktop UI                                           |
| **CommunityToolkit.Mvvm**    | MVVM pattern — `[ObservableProperty]`, `[RelayCommand]`      |
| **Flask**                    | Wraps TrOCR as a local HTTP server so C# can call it         |
| **HuggingFace Transformers** | Loads TrOCR — handles preprocessing, decoder loop, tokenizer |
| **PyTorch**                  | Math engine under HuggingFace — runs the AI computation      |
| **PyInstaller**              | Bundles the entire Python runtime into a single `.exe`       |

> **Why Python + localhost?**  
> HuggingFace and PyTorch are Python-only. The server is a bridge so C# can use TrOCR without requiring Python installed on the user's machine — it is all bundled inside `trocr_server.exe`.

---

## Building the Python Server (Step by Step)

This section covers how `trocr_server.exe` was built from scratch.

### Prerequisites

- Python 3.10 installed at `D:\Python310`
- `pip` available
- `D:\` as the working directory

---

### Step 1 — Create a virtual environment

A virtual environment keeps all the AI libraries isolated so they don't conflict with other Python projects.

```cmd
cd D:\
D:\Python310\python.exe -m venv trocr_env
```

This creates `D:\trocr_env\` — a self-contained Python environment.

---

### Step 2 — Activate the virtual environment

```cmd
D:\trocr_env\Scripts\activate
```

Your prompt will change to `(trocr_env)`. All `pip install` commands from here go into this environment.

---

### Step 3 — Install dependencies

```cmd
pip install flask
pip install transformers
pip install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cpu
pip install Pillow
pip install pyinstaller
```

| Package        | Why                                                  |
| -------------- | ---------------------------------------------------- |
| `flask`        | HTTP server framework                                |
| `transformers` | HuggingFace library — loads and runs TrOCR           |
| `torch`        | PyTorch — math engine for the AI model (CPU version) |
| `Pillow`       | Image loading and conversion                         |
| `pyinstaller`  | Bundles everything into a single `.exe`              |

> **CPU vs GPU:** `--index-url https://download.pytorch.org/whl/cpu` installs the CPU-only version of PyTorch (~200 MB). For GPU support remove that flag but the download becomes ~2 GB and requires CUDA drivers.

---

### Step 4 — Write the server (`trocr_server.py`)

Create `D:\trocr_server.py` with the following content:

---

### Step 5 — Test the server directly

Before building the `.exe`, verify it runs correctly:

```cmd
cd D:\
D:\trocr_env\Scripts\activate
python trocr_server.py
```

**What happens on first run:**

- HuggingFace downloads `microsoft/trocr-large-handwritten` (~2 GB)
- Saved to `%APPDATA%\TrOCRServer\models`
- Flask starts on `http://127.0.0.1:5005`

Test it in a second CMD window:

```cmd
curl http://127.0.0.1:5005/health
curl -X POST http://127.0.0.1:5005/ocr/file -F "image=@C:\path\to\test.png"
```

---

### Step 6 — Write the PyInstaller spec (`build.spec`)

Create `D:\build.spec`:

> **Why a spec file instead of just `pyinstaller trocr_server.py`?**  
> HuggingFace and Transformers have many data files (tokenizer configs, model configs) that PyInstaller won't find automatically. `collect_data_files()` and `collect_submodules()` make sure everything is bundled correctly.

---

### Step 7 — Build the `.exe`

```cmd
cd D:\
D:\trocr_env\Scripts\activate
pyinstaller build.spec
```

Build time: **10–20 minutes** depending on hardware.  
Output: `D:\dist\trocr_server.exe` (~200 MB)

> PyInstaller bundles Python runtime + Flask + HuggingFace + PyTorch + all dependencies into one file. Users do not need Python installed.

---

### Step 8 — How the model gets downloaded (automatic)

The model is **not bundled** inside the `.exe` — it is too large (~2 GB).  
Instead, `trocr_server.exe` downloads it on first run using HuggingFace's `from_pretrained()`:

```python
# This line in trocr_server.py triggers the download:
processor = TrOCRProcessor.from_pretrained("microsoft/trocr-large-handwritten", cache_dir=CACHE_DIR)
model     = VisionEncoderDecoderModel.from_pretrained("microsoft/trocr-large-handwritten", cache_dir=CACHE_DIR)
```

**Download flow:**

```
trocr_server.exe starts
      ↓
checks CACHE_DIR for existing model files
      ↓
not found  →  connects to huggingface.co
              downloads processor_config.json
              downloads tokenizer files
              downloads pytorch_model.bin  (~2 GB — the weights)
              saves all to CACHE_DIR
      ↓
found      →  loads directly from disk, no download
      ↓
Flask starts on port 5005
      ↓
WPF app detects /health  →  "Server ready"
```

**Cache location (set by WPF app via environment variable):**

```
D:\01- Pam\AppData\TrOCRServer\models\
└── models--microsoft--trocr-large-handwritten\
    ├── blobs\         ← actual model weight files
    ├── refs\          ← version tracking
    └── snapshots\     ← current model version symlinked here
```

> Once downloaded, the model **never downloads again** unless you delete the cache folder.

---

## System Requirements

|              | Minimum           | Recommended |
| ------------ | ----------------- | ----------- |
| **OS**       | Windows 10 64-bit | Windows 11  |
| **RAM**      | 4 GB              | 8 GB        |
| **Disk**     | 4 GB free         | 6 GB free   |
| **Internet** | First run only    | —           |
| **.NET**     | .NET 8.0 Runtime  | —           |

---

## First Run Behavior

```
1. App starts  →  launches trocr_server.exe in background
2. Server checks cache at D:\01- Pam\AppData\TrOCRServer\models
      Cache found   →  loads into RAM (~60 sec), no download
      No cache      →  downloads ~2 GB from HuggingFace (30–60 min)
3. Status bar shows "Server ready"  →  app is usable
4. On close  →  server stays alive if still downloading (preserves progress)
               server is killed on close only when fully ready
5. Next open  →  attaches to running server, or starts fresh from cache
```

> The model is only downloaded **once**. Every run after that loads from local cache.

---

## Model Information

**Current model:** `microsoft/trocr-large-handwritten`  
**Cache location:** `D:\01- Pam\AppData\TrOCRServer\models`  
**Source:** https://huggingface.co/microsoft/trocr-large-handwritten

### Available Variants

| Model                     | Size    | Best For                                     |
| ------------------------- | ------- | -------------------------------------------- |
| `trocr-small-handwritten` | ~330 MB | Handwriting — fast                           |
| `trocr-base-handwritten`  | ~600 MB | Handwriting — balanced                       |
| `trocr-large-handwritten` | ~1.4 GB | Handwriting — most accurate ✅ **(current)** |
| `trocr-small-printed`     | ~330 MB | Printed / typed text — fast                  |
| `trocr-base-printed`      | ~600 MB | Printed / typed text                         |
| `trocr-large-printed`     | ~1.4 GB | Printed / typed text — most accurate         |

To switch models, edit `MODEL_NAME` in `D:\trocr_server.py` and rebuild.

---

## Server API Endpoints

Base URL: `http://127.0.0.1:5005`

### `GET /health`

Health check. Polled by the WPF app on startup.

```json
{
  "status": "ok",
  "device": "cpu",
  "model": "microsoft/trocr-large-handwritten"
}
```

### `POST /ocr/file`

Multipart file upload. Form field name must be `image`.

```bash
curl -X POST http://127.0.0.1:5005/ocr/file -F "image=@photo.png"
```

```json
{ "text": "recognized text here" }
```

### `POST /ocr/base64`

Base64-encoded image in JSON body.

```json
{ "image": "<base64 string>" }
```

```json
{ "text": "recognized text here" }
```

---

## Environment Variables

Set automatically by the WPF app when launching `trocr_server.exe`:

| Variable         | Value                                | Purpose                                                                  |
| ---------------- | ------------------------------------ | ------------------------------------------------------------------------ |
| `APPDATA`        | `D:\01- Pam\AppData`                 | Redirects model cache to D drive                                         |
| `TEMP` / `TMP`   | `D:\01- Pam\Temp`                    | Redirects PyInstaller temp extraction to D drive                         |
| `HF_HUB_OFFLINE` | `1` (cache exists) / `0` (first run) | Skips HuggingFace network calls when model is cached — speeds up startup |

---

## Project Structure

```
PyOCR/
├── PyOCR/
│   ├── ViewModel/
│   │   └── MainVM.cs                        ← all business logic (MVVM)
│   ├── Behaviors/
│   │   └── DropBehavior.cs                  ← drag & drop attached behavior
│   ├── Converters/
│   │   └── InverseBoolToVisibilityConverter.cs
│   ├── MainWindow.xaml                      ← UI layout (pure bindings, no code-behind)
│   ├── MainWindow.xaml.cs                   ← InitializeComponent() only
│   └── PyOCR.csproj
├── .gitignore
└── README.md

Server (separate):
D:\dist\trocr_server.exe    ← bundled Python server (PyInstaller)
D:\trocr_server.py          ← Python source
D:\build.spec               ← PyInstaller build config
```

---

## Command Reference

### Build the WPF project

```bash
dotnet build PyOCR/PyOCR.csproj -c Debug
```

### Kill the server

```powershell
# PowerShell
Stop-Process -Name "trocr_server" -Force
```

```cmd
# Command Prompt
taskkill /F /IM trocr_server.exe
```

### Check if server is running

```cmd
netstat -ano | findstr "5005"
tasklist | findstr trocr_server
```

### Check model cache size

```powershell
$s = (Get-ChildItem "D:\01- Pam\AppData\TrOCRServer" -Recurse -File | Measure-Object Length -Sum).Sum
"$([math]::Round($s/1GB, 2)) GB"
```

### Test server endpoints

```bash
curl http://127.0.0.1:5005/health
curl -X POST http://127.0.0.1:5005/ocr/file -F "image=@C:\path\to\image.png"
```

### Build trocr_server.exe from Python source

```bash
cd D:\
pyinstaller build.spec
```

### Run Python server directly (development)

```bash
cd D:\
D:\trocr_env\Scripts\activate
python trocr_server.py
```

---

## Troubleshooting

<details>
<summary><b>Server not found</b></summary>

```
Server not found at D:\dist\trocr_server.exe
```

Make sure `trocr_server.exe` exists at `D:\dist\`.  
Rebuild with: `pyinstaller build.spec`

</details>

<details>
<summary><b>Server crashed</b></summary>

Read the error in the status bar. To see the full error output:

```cmd
D:\dist\trocr_server.exe > out.txt 2>&1
```

</details>

<details>
<summary><b>Long loading / still downloading after 30+ min</b></summary>

- First run downloads ~2 GB — can take 30–60 min on slow internet
- Do **NOT** close the app mid-download — server keeps running in background to preserve progress
- Check for duplicate processes:

```cmd
tasklist | findstr trocr_server
```

- Kill duplicates and restart:

```cmd
taskkill /F /IM trocr_server.exe
```

</details>

<details>
<summary><b>404 NOT FOUND when doing OCR</b></summary>

- Correct endpoint: `POST /ocr/file`
- Form field must be named `image` (not `file`)
</details>

<details>
<summary><b>C drive filling up</b></summary>

Model cache and temp files should go to D drive automatically.  
If C drive fills up, clean leftover PyInstaller temp folders:

```powershell
# PowerShell
Remove-Item "$env:TEMP\_MEI*" -Recurse -Force
```

```cmd
# CMD
for /d %i in ("%TEMP%\_MEI*") do rd /s /q "%i"
```

</details>

<details>
<summary><b>Poor OCR results</b></summary>

You are using the **handwritten** model.  
For printed or typed documents, switch to `trocr-large-printed` in `D:\trocr_server.py` and rebuild.

</details>
