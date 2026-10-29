"""Run on the image build host; never on the API or user's device."""
import argparse
import json
import pathlib
import re
import subprocess
from huggingface_hub import snapshot_download

parser = argparse.ArgumentParser()
parser.add_argument("--birefnet-revision", required=True)
parser.add_argument("--lama-revision", required=True)
args = parser.parse_args()
for revision in (args.birefnet_revision, args.lama_revision):
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        parser.error("Use an audited full 40-character commit SHA, not a moving branch.")
root = pathlib.Path(__file__).resolve().parent / "models"
root.mkdir(exist_ok=True)
snapshot_download("ZhengPeng7/BiRefNet_HR-matting", revision=args.birefnet_revision,
                  local_dir=root / "birefnet")
destination = root / "lama-source"
if destination.exists():
    raise SystemExit("lama-source already exists; inspect it before preparing another revision.")
subprocess.run(["git", "clone", "https://github.com/advimman/lama.git", str(destination)], check=True)
subprocess.run(["git", "-C", str(destination), "checkout", "--detach", args.lama_revision], check=True)
(root / "versions.json").write_text(json.dumps({"birefnet": args.birefnet_revision,
    "lama": args.lama_revision}), encoding="utf-8")
print("Place the official big-lama config.yaml and models/best.ckpt in models/big-lama before building.")
