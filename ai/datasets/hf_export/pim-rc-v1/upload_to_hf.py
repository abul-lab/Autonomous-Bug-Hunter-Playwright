#!/usr/bin/env python3
"""
Hugging Face Hub Upload Script for PIM-RC.
Usage:
    export HUGGING_FACE_HUB_TOKEN="hf_..."
    python upload_to_hf.py --repo_id abul-lab/pim-root-cause-classifier
"""
import argparse
from pathlib import Path
from huggingface_hub import HfApi

def main():
    parser = argparse.ArgumentParser(description="Upload PIM-RC model to Hugging Face Hub")
    parser.add_argument("--repo_id", default="abul-lab/pim-root-cause-classifier", help="Destination repository ID (org/name)")
    parser.add_argument("--private", action="store_true", help="Create as private repository")
    args = parser.parse_args()

    api = HfApi()
    print(f"Creating repo (if not exists): {args.repo_id}")
    api.create_repo(repo_id=args.repo_id, repo_type="model", private=args.private, exist_ok=True)

    print(f"Uploading artifacts from {Path(__file__).parent} to {args.repo_id}...")
    api.upload_folder(
        folder_path=str(Path(__file__).parent),
        repo_id=args.repo_id,
        repo_type="model",
        ignore_patterns=["upload_to_hf.py", "__pycache__/*", "*.pyc"]
    )
    print("Upload completed successfully!")

if __name__ == "__main__":
    main()
