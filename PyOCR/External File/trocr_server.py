"""
TrOCR Local REST API Server
============================
Bundled as .exe via PyInstaller — no Python needed on user's machine.
Model is cached in: %APPDATA%\TrOCRServer\models

Run standalone:   python trocr_server.py
Build to exe:     pyinstaller build.spec
"""

from flask import Flask, request, jsonify
from transformers import TrOCRProcessor, VisionEncoderDecoderModel
from PIL import Image
import torch
import base64
import io
import os
import sys
import logging

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)

# ── Model cache path (persists between runs) ──────────────────────────────────
CACHE_DIR = os.path.join(os.environ.get("APPDATA", os.path.expanduser("~")),
                         "TrOCRServer", "models")
os.makedirs(CACHE_DIR, exist_ok=True)

MODEL_NAME = "microsoft/trocr-large-handwritten"

logger.info(f"Model cache: {CACHE_DIR}")
logger.info(f"Loading model: {MODEL_NAME}")
logger.info("First run downloads ~2 GB. Subsequent runs load from cache instantly.")

try:
    processor = TrOCRProcessor.from_pretrained(MODEL_NAME, cache_dir=CACHE_DIR)
    model     = VisionEncoderDecoderModel.from_pretrained(MODEL_NAME, cache_dir=CACHE_DIR)
except Exception as e:
    logger.error(f"Failed to load model: {e}")
    sys.exit(1)

device = "cuda" if torch.cuda.is_available() else "cpu"
model.to(device)
model.eval()

logger.info(f"Model ready on {device.upper()}.")


# ── Helper ────────────────────────────────────────────────────────────────────
def ocr_image(pil_image: Image.Image) -> str:
    """Run TrOCR on a PIL image and return the recognised text."""
    if pil_image.mode != "RGB":
        pil_image = pil_image.convert("RGB")

    pixel_values = processor(images=pil_image, return_tensors="pt").pixel_values
    pixel_values = pixel_values.to(device)

    with torch.no_grad():
        generated_ids = model.generate(pixel_values)

    return processor.batch_decode(generated_ids, skip_special_tokens=True)[0]


# ── Routes ────────────────────────────────────────────────────────────────────

@app.route("/health", methods=["GET"])
def health():
    """Simple health-check — WPF app polls this on startup."""
    return jsonify({"status": "ok", "device": device, "model": MODEL_NAME})


@app.route("/ocr/base64", methods=["POST"])
def ocr_base64():
    """
    Accepts a base64-encoded image string.
    Body: { "image": "<base64 string>" }
    Returns: { "text": "recognised text" }
    """
    data = request.get_json(silent=True)
    if not data or "image" not in data:
        return jsonify({"error": "Missing 'image' field in JSON body"}), 400

    try:
        image_bytes = base64.b64decode(data["image"])
        pil_image   = Image.open(io.BytesIO(image_bytes))
        text        = ocr_image(pil_image)
        return jsonify({"text": text})
    except Exception as ex:
        logger.exception("OCR failed")
        return jsonify({"error": str(ex)}), 500


@app.route("/ocr/file", methods=["POST"])
def ocr_file():
    """
    Accepts a multipart file upload.
    Form field name: 'image'
    Returns: { "text": "recognised text" }
    """
    if "image" not in request.files:
        return jsonify({"error": "No 'image' file in request"}), 400

    try:
        file      = request.files["image"]
        pil_image = Image.open(file.stream)
        text      = ocr_image(pil_image)
        return jsonify({"text": text})
    except Exception as ex:
        logger.exception("OCR failed")
        return jsonify({"error": str(ex)}), 500


# ── Entry point ───────────────────────────────────────────────────────────────
if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5005, debug=False)
